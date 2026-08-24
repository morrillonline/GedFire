using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GedCore.Tests;

/// <summary>
/// Drives the packed `gedfire mcp` subprocess over real stdio for the
/// integration-level tests in McpServerIntegrationTests. Test infrastructure
/// only — not itself a production class under test, the same role
/// ApplyTestBase plays for the changeset-applier suites.
/// </summary>
internal sealed class McpStdioTestClient : IAsyncDisposable
{
    readonly Process _process;
    readonly StringBuilder _stderr = new();
    readonly Task _stderrPump;
    int _nextId;

    McpStdioTestClient(Process process)
    {
        _process = process;
        _stderrPump = PumpStderrAsync();
    }

    /// <summary>Path to the built gedfire executable next to this test assembly.</summary>
    public static string ResolveGedFireExePath()
    {
        string exeName = OperatingSystem.IsWindows() ? "GedFire.exe" : "GedFire";
        string path = Path.Combine(AppContext.BaseDirectory, exeName);
        if (!File.Exists(path))
            throw new FileNotFoundException("GedFire executable not found next to the test assembly.", path);
        return path;
    }

    public static McpStdioTestClient Start(string gedPath, params string[] extraArgs)
    {
        var psi = new ProcessStartInfo(ResolveGedFireExePath())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("mcp");
        psi.ArgumentList.Add("--input");
        psi.ArgumentList.Add(gedPath);
        foreach (var arg in extraArgs) psi.ArgumentList.Add(arg);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start gedfire.");
        return new McpStdioTestClient(process);
    }

    /// <summary>Run `gedfire &lt;args&gt;` to completion (no protocol interaction) and capture everything.</summary>
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunToCompletionAsync(
        TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo(ResolveGedFireExePath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start gedfire.");
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    async Task PumpStderrAsync()
    {
        string? line;
        while ((line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            lock (_stderr) _stderr.AppendLine(line);
    }

    public string StderrSoFar { get { lock (_stderr) return _stderr.ToString(); } }

    public async Task SendAsync(string rawJson)
    {
        await _process.StandardInput.WriteLineAsync(rawJson).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    public Task SendNotificationAsync(string method) =>
        SendAsync(JsonSerializer.Serialize(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method }));

    /// <summary>Read one raw stdout line, or throw if none arrives within <paramref name="timeout"/>.</summary>
    public async Task<string> ReadLineAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        string? line;
        try
        {
            line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No stdout line within {timeout}. Stderr so far:\n{StderrSoFar}");
        }
        return line ?? throw new TimeoutException($"stdout closed (EOF) while waiting for a line. Stderr so far:\n{StderrSoFar}");
    }

    /// <summary>Send a JSON-RPC request and wait for the response with the matching id.</summary>
    public async Task<JsonElement> SendRequestAsync(string method, object? @params, TimeSpan timeout)
    {
        int id = Interlocked.Increment(ref _nextId);
        var request = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params != null) request["params"] = @params;
        await SendAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"No response for '{method}' (id {id}) within {timeout}.");

            string line = await ReadLineAsync(remaining).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.Number && idProp.GetInt32() == id)
            {
                return doc.RootElement.Clone();
            }
            // Otherwise: a notification or a response to a different id; keep reading.
        }
    }

    public async Task InitializeAsync(TimeSpan timeout)
    {
        await SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "gedcore-tests", version = "0.0.1" },
        }, timeout).ConfigureAwait(false);
        await SendNotificationAsync("notifications/initialized").ConfigureAwait(false);
    }

    /// <summary>Close stdin and wait for the process to exit on its own.</summary>
    public async Task<int> CloseStdinAndWaitForExitAsync(TimeSpan timeout)
    {
        _process.StandardInput.Close();
        using var cts = new CancellationTokenSource(timeout);
        await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { /* already closed */ }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* fall through to Kill */ }
            }
        }
        finally
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
            try { await _stderrPump.ConfigureAwait(false); } catch { /* best effort */ }
            _process.Dispose();
        }
    }
}
