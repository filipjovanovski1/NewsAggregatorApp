using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NewsApplication.Web.Summarization;

public interface IOllamaBodyBuilder
{
    Task<byte[]> BuildAsync(SummaryJob job, string? repairSummary, CancellationToken cancellationToken);
}

public sealed class PowerShellOllamaBodyBuilder : IOllamaBodyBuilder, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AiSummarizationOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PowerShellOllamaBodyBuilder> _logger;
    private Process? _process;
    private int _disposeState;

    public PowerShellOllamaBodyBuilder(
        IOptions<AiSummarizationOptions> options,
        IHostEnvironment environment,
        ILogger<PowerShellOllamaBodyBuilder> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<byte[]> BuildAsync(
        SummaryJob job, string? repairSummary, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStarted();
            var request = JsonSerializer.Serialize(new
            {
                job.Title, job.Description, job.Publisher, job.Language,
                RepairSummary = repairSummary,
                _options.Model,
                _options.ContextLength,
                _options.OutputTokenLimit,
                _options.Temperature,
                _options.Seed,
                _options.Think
            });
            await _process!.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
            var responseLine = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
                throw new InvalidOperationException(
                    _process.HasExited
                        ? $"PowerShell body-builder exited with code {_process.ExitCode}."
                        : "PowerShell body-builder returned an empty response.");

            var response = JsonSerializer.Deserialize<BridgeResponse>(responseLine, JsonOptions)
                ?? throw new InvalidOperationException("PowerShell body-builder returned invalid JSON.");
            if (!response.Success || string.IsNullOrWhiteSpace(response.BodyBase64))
                throw new InvalidOperationException(
                    response.Error ?? "PowerShell body-builder failed without an error message.");
            return Convert.FromBase64String(response.BodyBase64);
        }
        finally { _gate.Release(); }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false }) return;
        _process?.Dispose();
        var bridgePath = ResolvePath(_options.BridgeScriptPath);
        var bodyBuilderPath = ResolvePath(_options.ScriptPath);
        if (!File.Exists(bridgePath))
            throw new FileNotFoundException("Ollama PowerShell bridge was not found.", bridgePath);
        if (!File.Exists(bodyBuilderPath))
            throw new FileNotFoundException("Ollama body-builder script was not found.", bodyBuilderPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.PowerShellExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", bridgePath, "-BodyBuilderPath", bodyBuilderPath
        }) startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                _logger.LogWarning("Ollama PowerShell bridge: {Message}", args.Data);
        };
        if (!_process.Start())
            throw new InvalidOperationException("Could not start the PowerShell body-builder.");
        _process.BeginErrorReadLine();
    }

    private string ResolvePath(string configuredPath) => Path.GetFullPath(
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_environment.ContentRootPath, configuredPath));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        await _gate.WaitAsync();
        try
        {
            if (_process is null) return;
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
            }
            _process.Dispose();
            _process = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed record BridgeResponse(bool Success, string? BodyBase64, string? Error);
}
