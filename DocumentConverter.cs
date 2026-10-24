using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlToOpenXml;
using Markdig;
using Microsoft.Playwright;

namespace ResumeTailor;

/// <summary>
/// Converts rendered HTML or Markdown into PDF (via Playwright/Chromium) and
/// Word DOCX (via HtmlToOpenXml) without external command-line tools.
/// Handles file locks gracefully if Word or Acrobat has a document open.
/// </summary>
public static class DocumentConverter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// Renders HTML or Markdown directly to PDF using headless Chromium (Playwright).
    /// </summary>
    public static async Task<bool> GeneratePdfAsync(string htmlOrMarkdown, string outputPath)
    {
        var result = await GeneratePdfWithInfoAsync(htmlOrMarkdown, outputPath);
        return result.Success;
    }

    /// <summary>
    /// Renders HTML or Markdown directly to PDF and returns the actual saved path.
    /// If the target file is locked by Acrobat/browser, an incremental fallback name is used.
    /// </summary>
    public static async Task<(bool Success, string SavedPath)> GeneratePdfWithInfoAsync(string htmlOrMarkdown, string outputPath)
    {
        string targetPath = GetAvailableOutputPath(outputPath);
        if (!string.Equals(targetPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  [PDF]  ⚠ '{Path.GetFileName(outputPath)}' is locked by another app. Saving as '{Path.GetFileName(targetPath)}'.");
            Console.ResetColor();
        }

        try
        {
            string html = IsHtml(htmlOrMarkdown)
                ? htmlOrMarkdown
                : BuildStyledHtml(htmlOrMarkdown);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true });

            var page = await browser.NewPageAsync();
            await page.SetContentAsync(html,
                new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await page.PdfAsync(new PagePdfOptions
            {
                Path            = targetPath,
                Format          = "A4",
                PrintBackground = true,
                Margin = new Margin
                {
                    Top    = "0",
                    Bottom = "0",
                    Left   = "0",
                    Right  = "0",
                },
            });

            return (true, targetPath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"│  [PDF]  ✗ PDF generation failed: {ex.Message}");
            Console.ResetColor();
            return (false, targetPath);
        }
    }

    /// <summary>
    /// Converts HTML or Markdown resume to a Word document (.docx).
    /// </summary>
    public static async Task<bool> GenerateWordAsync(string htmlOrMarkdown, string outputPath)
    {
        var result = await GenerateWordWithInfoAsync(htmlOrMarkdown, outputPath);
        return result.Success;
    }

    /// <summary>
    /// Converts HTML or Markdown resume to a Word document (.docx) and returns the actual saved path.
    /// If the target file is locked by Microsoft Word, an incremental fallback name is used.
    /// </summary>
    public static async Task<(bool Success, string SavedPath)> GenerateWordWithInfoAsync(string htmlOrMarkdown, string outputPath)
    {
        string targetPath = GetAvailableOutputPath(outputPath);
        if (!string.Equals(targetPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  [DOCX] ⚠ '{Path.GetFileName(outputPath)}' is open in Microsoft Word. Saving as '{Path.GetFileName(targetPath)}'.");
            Console.ResetColor();
        }

        try
        {
            string html = IsHtml(htmlOrMarkdown)
                ? htmlOrMarkdown
                : Markdown.ToHtml(htmlOrMarkdown, Pipeline);

            using var memStream = new MemoryStream();
            using (var wordDoc = WordprocessingDocument.Create(
                       memStream, WordprocessingDocumentType.Document))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());

                var converter = new HtmlConverter(mainPart);
                await converter.ParseBody(html);

                wordDoc.Save();
            }

            await File.WriteAllBytesAsync(targetPath, memStream.ToArray());
            return (true, targetPath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"│  [DOCX] ✗ Word generation failed: {ex.Message}");
            Console.ResetColor();
            return (false, targetPath);
        }
    }

    /// <summary>
    /// Returns the desired path if it can be written to. If it exists and is locked
    /// by another process (like Word or Acrobat), returns an available incremented path.
    /// </summary>
    public static string GetAvailableOutputPath(string desiredPath)
    {
        if (!File.Exists(desiredPath) || !IsFileLocked(desiredPath))
            return desiredPath;

        string dir       = Path.GetDirectoryName(desiredPath) ?? "";
        string fileName  = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);

        for (int i = 1; i <= 99; i++)
        {
            string candidate = Path.Combine(dir, $"{fileName} ({i}){extension}");
            if (!File.Exists(candidate) || !IsFileLocked(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{fileName}_{DateTime.Now:HHmmss}{extension}");
    }

    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsHtml(string s) =>
        s.TrimStart().StartsWith("<", StringComparison.Ordinal);

    private static string BuildStyledHtml(string markdown)
    {
        string body = Markdown.ToHtml(markdown, Pipeline);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <style>
                    @page { size: A4; margin: 0.38in 0.45in; }
                    body { font-family: 'Times New Roman', Times, serif; font-size: 9.6pt; line-height: 1.28; }
                    h1 { font-size: 17pt; text-align: center; margin-bottom: 3px; text-transform: uppercase; }
                    h2 { font-size: 10.8pt; border-bottom: 1px solid #000; margin: 8px 0 2px; padding-bottom: 1px; }
                    ul { margin: 1.5px 0 3px 0; padding-left: 16px; }
                    li { margin-bottom: 1.5px; }
                </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }
}
