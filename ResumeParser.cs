// ResumeParser.cs — Converts a raw PDF resume into a structured ResumeModel.
// Step 1: Extracts all visible text from the PDF using PdfPig (direct text-layer read).
// Step 2: Sends the raw text to the local Ollama LLM, which returns a structured
//         JSON object matching the ResumeModel schema. The result is cached as
//         master.json so subsequent runs skip the expensive parse step.

using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ResumeTailor;

/// <summary>
/// Parses a PDF resume into a <see cref="ResumeModel"/> by:
/// <list type="number">
///   <item>Reading the PDF text layer directly via PdfPig (no browser needed).</item>
///   <item>Sending the raw text to the LLM to produce a structured JSON resume.</item>
/// </list>
/// </summary>
public static class ResumeParser
{
    /// <summary>
    /// Searches <paramref name="searchDir"/> for a file named <c>resume.pdf</c> (case-insensitive).
    /// </summary>
    /// <returns>The full path to the resume file, or <c>null</c> if none is found.</returns>
    public static string? FindResumeFile(string searchDir)
    {
        string[] candidates = Directory.GetFiles(searchDir, "resume.*", SearchOption.TopDirectoryOnly);
        foreach (var candidate in candidates)
        {
            if (Path.GetExtension(candidate).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Extracts all visible text from a PDF by reading its text layer directly with PdfPig.
    /// Words are sorted by page position (top-to-bottom, left-to-right) and joined into
    /// lines, producing clean readable text similar to what you'd see on screen.
    /// </summary>
    /// <param name="pdfPath">Absolute path to the PDF file.</param>
    /// <returns>The extracted text, or an empty string if no text layer was found.</returns>
    public static Task<string> ExtractTextFromPdfAsync(string pdfPath)
    {
        Console.WriteLine($"[INFO] Extracting text from PDF: {Path.GetFileName(pdfPath)}");

        var sb = new StringBuilder();

        try
        {
            using var doc = PdfDocument.Open(pdfPath);

            foreach (var page in doc.GetPages())
            {
                // Sort words by vertical position (descending = top first), then horizontal
                var words = page.GetWords()
                    .OrderByDescending(w => w.BoundingBox.Bottom)
                    .ThenBy(w => w.BoundingBox.Left)
                    .ToList();

                if (words.Count == 0) continue;

                double? lastBottom = null;
                double  lineHeight = EstimateLineHeight(words);

                foreach (var word in words)
                {
                    double bottom = word.BoundingBox.Bottom;

                    // New line when the word's baseline drops more than ~half a line height
                    if (lastBottom.HasValue && Math.Abs(lastBottom.Value - bottom) > lineHeight * 0.45)
                        sb.AppendLine();
                    else if (sb.Length > 0 && sb[^1] != '\n')
                        sb.Append(' ');

                    sb.Append(word.Text);
                    lastBottom = bottom;
                }

                sb.AppendLine(); // page separator
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] PdfPig extraction failed: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(string.Empty);
        }

        string text = sb.ToString().Trim();
        Console.WriteLine($"[INFO] Extracted {text.Length:N0} characters from PDF.");
        return Task.FromResult(text);
    }

    /// <summary>
    /// Estimates the median line height from the bounding boxes of all words on the page,
    /// used to detect line breaks between consecutive words.
    /// </summary>
    private static double EstimateLineHeight(IList<Word> words)
    {
        if (words.Count == 0) return 10.0;
        var heights = words.Select(w => w.BoundingBox.Height).OrderBy(h => h).ToList();
        return heights[heights.Count / 2]; // median height
    }

    /// <summary>
    /// Full pipeline: extracts text from the PDF, calls the LLM to parse it into a
    /// <see cref="ResumeModel"/>, and caches the result as <paramref name="cacheJsonPath"/>
    /// (typically <c>master.json</c>) so subsequent runs skip the parse step.
    /// </summary>
    /// <param name="pdfPath">Path to the input PDF resume.</param>
    /// <param name="llmService">An <see cref="LlmService"/> instance for LLM calls.</param>
    /// <param name="cacheJsonPath">Where to save the resulting <c>master.json</c>.</param>
    /// <returns>The parsed <see cref="ResumeModel"/>, or <c>null</c> if extraction or parsing failed.</returns>
    public static async Task<ResumeModel?> ParseAndCacheAsync(
        string     pdfPath,
        LlmService llmService,
        string     cacheJsonPath)
    {
        // ── Step 1: Extract raw text ──────────────────────────────────────────
        string rawText = await ExtractTextFromPdfAsync(pdfPath);

        if (string.IsNullOrWhiteSpace(rawText))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Could not extract any text from the PDF. " +
                              "Ensure the PDF has a text layer (i.e., is not a scanned image-only PDF).");
            Console.ResetColor();
            return null;
        }

        // ── Step 2: Ask the LLM to structure the raw text ─────────────────────
        Console.WriteLine("[INFO] Asking LLM to parse resume into structured JSON (this may take ~30-60s)...");
        ResumeModel? model = await llmService.ParseResumeAsync(rawText);

        if (model is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] LLM failed to produce a valid ResumeModel from the PDF text.");
            Console.ResetColor();
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[INFO] ✓ Parsed resume for: {model.Name}");
        Console.ResetColor();

        // ── Step 3: Cache as master.json ──────────────────────────────────────
        try
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented          = true,
                PropertyNamingPolicy   = null,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            };
            string json = JsonSerializer.Serialize(model, opts);
            await File.WriteAllTextAsync(cacheJsonPath, json);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[INFO] ✓ Structured resume cached at: {cacheJsonPath}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not save master.json cache: {ex.Message}");
        }

        return model;
    }
}
