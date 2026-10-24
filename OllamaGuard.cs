using System.Diagnostics;
using System.Net.Http;

namespace ResumeTailor;

/// <summary>
/// Ensures a local Ollama instance is running before LLM calls are made.
/// If the server is not reachable it launches <c>ollama serve</c> as a
/// background process and polls until the endpoint comes up (or times out).
/// </summary>
public static class OllamaGuard
{
    private const string OllamaBaseUrl   = "http://localhost:11434";
    private const int    PollIntervalMs  = 1_000;
    private const int    TimeoutSeconds  = 30;

    /// <summary>
    /// Checks whether Ollama is reachable. If not, starts <c>ollama serve</c>
    /// and waits up to <see cref="TimeoutSeconds"/> seconds for it to become
    /// available. Returns <c>true</c> when the server is ready.
    /// </summary>
    public static async Task<bool> EnsureRunningAsync()
    {
        if (await IsReachableAsync())
        {
            Console.WriteLine("[OLLAMA] Server already running.");
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[OLLAMA] Server not detected — launching 'ollama serve'...");
        Console.ResetColor();

        if (!TryStartOllamaServe())
            return false;

        // Poll until the server responds or we time out.
        var deadline = DateTime.Now.AddSeconds(TimeoutSeconds);
        while (DateTime.Now < deadline)
        {
            await Task.Delay(PollIntervalMs);

            if (await IsReachableAsync())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[OLLAMA] Server is now ready.");
                Console.ResetColor();
                return true;
            }
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[OLLAMA] Server did not become ready within {TimeoutSeconds}s. Aborting.");
        Console.ResetColor();
        return false;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var http     = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var       response = await http.GetAsync(OllamaBaseUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartOllamaServe()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = "ollama",
                Arguments       = "serve",
                // Run in the background — do NOT redirect streams here or the
                // server's output will block the pipe buffer and hang.
                UseShellExecute = false,
                CreateNoWindow  = true,
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[OLLAMA] Failed to start 'ollama serve': {ex.Message}");
            Console.WriteLine("[OLLAMA] Make sure Ollama is installed and on your PATH.");
            Console.ResetColor();
            return false;
        }
    }
}
