using Microsoft.Playwright;

namespace ResumeTailor;

/// <summary>
/// Uses a single shared headless Chromium browser (launched once) to scrape
/// all job listing pages. Implements IAsyncDisposable so the browser and
/// Playwright runtime are cleaned up after all URLs have been processed.
/// </summary>
public class ScraperService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>
    /// Lazily initialises the shared Playwright and Chromium browser on the
    /// first call, then reuses the same browser for every subsequent URL.
    /// </summary>
    private async Task EnsureBrowserAsync()
    {
        if (_browser is not null)
            return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>
    /// Navigates to <paramref name="url"/> in a new browser page,
    /// waits for NetworkIdle so SPA frameworks (e.g. Workday, Greenhouse) fully render,
    /// then returns the inner text of the &lt;body&gt; element.
    /// Returns an empty string if navigation fails or times out.
    /// Each call opens a fresh page tab and closes it when done,
    /// but the underlying browser process is reused across all calls.
    /// </summary>
    /// <param name="url">The job listing URL to scrape.</param>
    /// <returns>The visible text content of the page body, or an empty string on failure.</returns>
    public async Task<string> ScrapeJobDescriptionAsync(string url)
    {
        await EnsureBrowserAsync();

        // Open a fresh tab for each URL, close it when done to free memory.
        var page = await _browser!.NewPageAsync();

        try
        {
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WARN] Navigation notice: {ex.Message}");
            }

            // Wait a brief moment for dynamic client-side SPA frameworks to render
            await page.WaitForTimeoutAsync(3000);

            var bodyElement = await page.QuerySelectorAsync("body");
            if (bodyElement is null)
                return string.Empty;

            string content = await bodyElement.InnerTextAsync();
            if (content.Length < 300)
            {
                // If content is very small, wait an additional 3 seconds for async widgets
                await page.WaitForTimeoutAsync(3000);
                content = await bodyElement.InnerTextAsync();
            }

            return content;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WARN] Failed to scrape '{url}': {ex.Message}");
            return string.Empty;
        }
        finally
        {
            // Always close the tab whether the scrape succeeded or failed.
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Disposes the shared browser and Playwright runtime.
    /// Call this (or use <c>await using</c>) after all URLs have been processed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}
