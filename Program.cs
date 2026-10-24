// ResumeTailor — top-level entry point
// Drop YOUR resume as 'resume.pdf' in the project root. On the first run the
// tool will extract its text, ask the LLM to parse it into a structured JSON
// (master.json), and cache it for all future runs. Job URLs are still read
// from jobs.xlsx and each listing receives a fully tailored PDF + DOCX output.

using System.Text.Json;
using ResumeTailor;

// ──────────────────────────────────────────────
// Configuration — adjust paths as needed
// ──────────────────────────────────────────────
string excelPath  = "jobs.xlsx";
string masterPath = "master.json";
string outputDir  = Path.Combine(AppContext.BaseDirectory, "output");
string workDir    = Directory.GetCurrentDirectory();

// ──────────────────────────────────────────────
// Validate inputs
// ──────────────────────────────────────────────
if (!File.Exists(excelPath))
{
    Console.WriteLine($"[ERROR] Excel file not found: {Path.GetFullPath(excelPath)}");
    return;
}

// ──────────────────────────────────────────────
// Initialise LlmService early (needed for resume parsing too)
// ──────────────────────────────────────────────
var llmService = new LlmService();

// ──────────────────────────────────────────────
// Load or auto-parse the master resume
// ──────────────────────────────────────────────
ResumeModel masterResume;

if (!File.Exists(masterPath))
{
    // No master.json — look for a resume.pdf to parse
    string? resumePdf = ResumeParser.FindResumeFile(workDir);

    if (resumePdf is null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] No master.json and no resume.pdf found in the working directory.");
        Console.WriteLine("        Drop your resume as 'resume.pdf' next to jobs.xlsx, then re-run.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine($"[INFO] resume.pdf found: {resumePdf}");
    Console.WriteLine("[INFO] master.json not found — parsing resume.pdf with LLM...");

    // Ensure Ollama is up before we call it
    if (!await OllamaGuard.EnsureRunningAsync())
    {
        Console.WriteLine("[ERROR] Cannot proceed without a running Ollama instance. Exiting.");
        return;
    }

    ResumeModel? parsed = await ResumeParser.ParseAndCacheAsync(resumePdf, llmService, masterPath);
    if (parsed is null)
        return;

    masterResume = parsed;
}
else
{
    // master.json exists — load it directly (fast path / subsequent runs)
    Console.WriteLine($"[INFO] Loading master resume from: {masterPath}");
    try
    {
        string json = File.ReadAllText(masterPath);
        masterResume = JsonSerializer.Deserialize<ResumeModel>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Deserialized to null.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to parse master.json: {ex.Message}");
        Console.WriteLine("        Delete master.json and drop resume.pdf to trigger a fresh parse.");
        return;
    }

    if (masterResume.Experience == null || masterResume.Experience.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[WARN] Master resume contains 0 experience entries! Triggering auto-reparse...");
        Console.ResetColor();
        string? resumePdf = ResumeParser.FindResumeFile(workDir);
        if (resumePdf != null && await OllamaGuard.EnsureRunningAsync())
        {
            var repaired = await ResumeParser.ParseAndCacheAsync(resumePdf, llmService, masterPath);
            if (repaired != null && repaired.Experience.Count > 0)
                masterResume = repaired;
        }
    }
}

Console.WriteLine($"[INFO] Loaded resume for: {masterResume.Name} ({masterResume.Experience?.Count ?? 0} roles)");


// ──────────────────────────────────────────────
// Ensure the output directory exists
// ──────────────────────────────────────────────
Directory.CreateDirectory(outputDir);
Console.WriteLine($"[INFO] Output directory: {outputDir}");

if (args.Length > 0 && args[0].Equals("--dump-pdf", StringComparison.OrdinalIgnoreCase))
{
    string? resumePdf = ResumeParser.FindResumeFile(workDir);
    if (resumePdf is null) { Console.WriteLine("[ERROR] No resume.pdf found."); return; }
    string raw = await ResumeParser.ExtractTextFromPdfAsync(resumePdf);
    await File.WriteAllTextAsync("extracted_pdf_text.txt", raw);
    Console.WriteLine($"Extracted text written to extracted_pdf_text.txt ({raw.Length} chars)");
    var sections = LlmService.SplitIntoSections(raw);
    foreach (var kvp in sections)
    {
        Console.WriteLine($"Section [{kvp.Key}]: {kvp.Value.Length} chars");
    }
    return;
}

if (args.Length > 0 && args[0].Equals("--preview", StringComparison.OrdinalIgnoreCase))

{
    Console.WriteLine("[INFO] Running preview generation for master resume...");
    string previewHtml = ResumeRenderer.RenderHtml(masterResume);
    string previewPdf  = Path.Combine(outputDir, "Preview_Master.pdf");
    string previewDocx = Path.Combine(outputDir, "Preview_Master.docx");
    string previewMd   = Path.Combine(outputDir, "Preview_Master.md");
    File.WriteAllText(previewMd, ResumeRenderer.RenderMarkdown(masterResume));
    await DocumentConverter.GeneratePdfAsync(previewHtml, previewPdf);
    await DocumentConverter.GenerateWordAsync(previewHtml, previewDocx);
    Console.WriteLine($"[INFO] ✓ Preview PDF  : {previewPdf}");
    Console.WriteLine($"[INFO] ✓ Preview DOCX : {previewDocx}");
    Console.WriteLine($"[INFO] ✓ Preview MD   : {previewMd}");
    return;
}

if (args.Length > 0 && args[0].Equals("--re-render", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("[INFO] Re-rendering existing output markdown files to PDF and DOCX...");
    foreach (var mdFile in Directory.GetFiles(outputDir, "Resume_*.md"))
    {
        string baseName = Path.GetFileNameWithoutExtension(mdFile);
        string targetPdf = Path.Combine(outputDir, $"{baseName}.pdf");
        string targetDocx = Path.Combine(outputDir, $"{baseName}.docx");
        string mdText = File.ReadAllText(mdFile);

        var (pdfOk, pPath) = await DocumentConverter.GeneratePdfWithInfoAsync(mdText, targetPdf);
        var (docxOk, dPath) = await DocumentConverter.GenerateWordWithInfoAsync(mdText, targetDocx);
        Console.WriteLine($"[INFO] ✓ {baseName} -> PDF: {(pdfOk ? "✓" : "✗")}, DOCX: {(docxOk ? "✓" : "✗")}");
    }
    return;
}

// ──────────────────────────────────────────────
// Extract job URLs from the Excel workbook
// ──────────────────────────────────────────────
var excelParser = new ExcelParser();
Console.WriteLine($"[INFO] Parsing job URLs from: {excelPath}");
List<string> urls = excelParser.GetUrls(excelPath);

if (urls.Count == 0)
{
    Console.WriteLine("[WARN] No URLs found in the Excel file. Exiting.");
    return;
}

Console.WriteLine($"[INFO] Found {urls.Count} job URL(s). Starting processing...\n");

// ──────────────────────────────────────────────
// Ensure Ollama is running (start it if not)
// ──────────────────────────────────────────────
if (!await OllamaGuard.EnsureRunningAsync())
{
    Console.WriteLine("[ERROR] Cannot proceed without a running Ollama instance. Exiting.");
    return;
}

// ──────────────────────────────────────────────
// Initialise remaining services
// ──────────────────────────────────────────────
// llmService is already initialised above
var savedFiles  = new List<string>();
var skippedUrls = new List<(int Index, string Url, string Reason)>();

await using var scraperService = new ScraperService();

var overallStart = DateTime.Now;

// ──────────────────────────────────────────────
// Main processing loop
// ──────────────────────────────────────────────
for (int i = 0; i < urls.Count; i++)
{
    string url       = urls[i];
    int    jobNumber = i + 1;
    var    jobStart  = DateTime.Now;

    Console.WriteLine($"┌─ [{jobNumber}/{urls.Count}] {url}");

    // ── Step 1 — Scrape the job description ──────────────────────────────
    Console.WriteLine("│  → Scraping job description...");
    string jobDescription = await scraperService.ScrapeJobDescriptionAsync(url);

    if (string.IsNullOrWhiteSpace(jobDescription))
    {
        Console.WriteLine("│  [SKIP] Scraping returned empty content.\n└─");
        skippedUrls.Add((jobNumber, url, "Scrape returned empty"));
        continue;
    }

    Console.WriteLine($"│  → Scraped {jobDescription.Length:N0} characters.");

    // ── Step 2 — Ask LLM for a JSON patch ────────────────────────────────
    Console.WriteLine("│  → Requesting ATS patch from LLM (qwen2.5:14b)...");
    TailoredPatch? patch = await llmService.TailorResumeAsync(jobDescription, masterResume);

    // If the LLM fails / returns bad JSON, fall back to the master resume unchanged
    ResumeModel tailored = patch is not null
        ? patch.ApplyTo(masterResume)
        : masterResume;

    // ── Step 2b — Deterministic post-processing (guaranteed, no LLM) ─────────
    // 1. Ensure every original master bullet is preserved (LLM often drops bullets)
    tailored = JdKeywordInjector.MergeExistingBullets(tailored, masterResume);
    // 2. Inject ALL JD technologies into skills — regex-based, 100% reliable
    tailored = JdKeywordInjector.InjectJdKeywords(jobDescription, tailored);

    if (patch is null)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("│  [WARN] Patch failed — using master resume unchanged.");
        Console.ResetColor();
    }

    // ── Calculate ATS Match Score ─────────────────────────────────────────
    var atsResult = AtsScorer.ScoreResume(jobDescription, tailored);

    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"│  [ATS]  ★ Match Score: {atsResult.ScorePercentage}% ({atsResult.MatchRating})");
    Console.ResetColor();

    if (atsResult.MatchedKeywords.Count > 0)
    {
        string matchedSnippet = string.Join(", ", atsResult.MatchedKeywords.Take(10));
        if (atsResult.MatchedKeywords.Count > 10)
            matchedSnippet += $", ... (+{atsResult.MatchedKeywords.Count - 10} more)";
        Console.WriteLine($"│         ✓ Matched: {matchedSnippet}");
    }

    if (atsResult.MissingKeywords.Count > 0)
    {
        string missingSnippet = string.Join(", ", atsResult.MissingKeywords.Take(6));
        if (atsResult.MissingKeywords.Count > 6)
            missingSnippet += $", ... (+{atsResult.MissingKeywords.Count - 6} more)";
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"│         ⚠ Keyword Gaps: {missingSnippet}");
        Console.ResetColor();
    }

    // ── Step 3 — Render to consistent HTML and Markdown ──────────────────
    string html     = ResumeRenderer.RenderHtml(tailored);
    string markdown = ResumeRenderer.RenderMarkdown(tailored);

    string desiredMdPath = Path.Combine(outputDir, $"Resume_{jobNumber}.md");
    string mdPath = DocumentConverter.GetAvailableOutputPath(desiredMdPath);
    File.WriteAllText(mdPath, markdown);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"│  [MD]   ✓ Saved: {mdPath}");
    Console.ResetColor();

    // ── Step 4 — Render PDF via Playwright/Chromium ───────────────────────
    string desiredPdfPath = Path.Combine(outputDir, $"Resume_{jobNumber}.pdf");
    Console.WriteLine("│  [PDF]  → Rendering with Playwright...");
    var (pdfOk, pdfPath) = await DocumentConverter.GeneratePdfWithInfoAsync(html, desiredPdfPath);

    if (pdfOk)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"│  [PDF]  ✓ Generated: {pdfPath}");
        Console.ResetColor();
    }

    // ── Step 5 — Generate Word document ───────────────────────────────────
    string desiredDocxPath = Path.Combine(outputDir, $"Resume_{jobNumber}.docx");
    Console.WriteLine("│  [DOCX] → Converting with HtmlToOpenXml...");
    var (wordOk, docxPath) = await DocumentConverter.GenerateWordWithInfoAsync(html, desiredDocxPath);

    if (wordOk)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"│  [DOCX] ✓ Generated: {docxPath}");
        Console.ResetColor();
    }

    var elapsed = DateTime.Now - jobStart;
    Console.WriteLine($"│  ── Finished in {elapsed.TotalSeconds:F1}s  " +
                      $"(PDF: {(pdfOk ? "✓" : "✗")}  DOCX: {(wordOk ? "✓" : "✗")})");
    Console.WriteLine("└─\n");
    savedFiles.Add($"{mdPath}  [ATS Match: {atsResult.ScorePercentage}%]");
}

// ──────────────────────────────────────────────
// Final summary
// ──────────────────────────────────────────────
var totalElapsed = DateTime.Now - overallStart;
Console.WriteLine("══════════════════════════════════════════");
Console.WriteLine($"  Run complete in {totalElapsed.TotalMinutes:F1} min");
Console.WriteLine($"  ✓ Saved   : {savedFiles.Count}/{urls.Count} resumes");

if (savedFiles.Count > 0)
    foreach (var f in savedFiles)
        Console.WriteLine($"      {f}");

if (skippedUrls.Count > 0)
{
    Console.WriteLine($"  ✗ Skipped : {skippedUrls.Count} URL(s)");
    foreach (var (idx, u, reason) in skippedUrls)
        Console.WriteLine($"      [{idx}] {reason} — {u}");
}

Console.WriteLine("══════════════════════════════════════════");
