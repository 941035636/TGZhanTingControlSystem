using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace TG.Control.Server;

/// <summary>
/// Owns only the optional local Python worker process. TTS jobs and content state do not depend on it.
/// </summary>
public sealed class MeloTtsWorkerSupervisor(
    IOptions<MeloTtsLocalOptions> configuredOptions,
    ILogger<MeloTtsWorkerSupervisor> logger) : BackgroundService
{
    private readonly MeloTtsLocalOptions options = configuredOptions.Value;
    private readonly object statusGate = new();
    private Process? workerProcess;
    private string? runtimeError;

    public string? GetConfigurationError()
    {
        if (!options.Enabled) return "MeloTTS 本地服务未启用。";
        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            return "MeloTTS Worker 地址必须使用 localhost/loopback。";
        var paths = ResolvePaths();
        if (!File.Exists(paths.PythonExecutable)) return "MeloTTS 本地运行时尚未安装。";
        if (!File.Exists(paths.WorkerScript)) return "MeloTTS Worker 程序缺失。";
        if (!Directory.Exists(paths.MeloTtsSource)) return "MeloTTS 程序包缺失。";
        if (!File.Exists(Path.Combine(paths.AcousticModel, "config.json")) ||
            !File.Exists(Path.Combine(paths.AcousticModel, "checkpoint.pth")))
            return "MeloTTS 中文模型尚未安装或不完整。";
        if (!File.Exists(Path.Combine(paths.BertModel, "config.json")) ||
            !File.Exists(Path.Combine(paths.BertModel, "pytorch_model.bin")) ||
            !File.Exists(Path.Combine(paths.BertModel, "vocab.txt")))
            return "MeloTTS 中文 BERT 依赖尚未安装或不完整。";
        return null;
    }

    public string? GetRuntimeError()
    {
        lock (statusGate) return runtimeError;
    }

    public MeloTtsResolvedPaths ResolvePaths() => new(
        Resolve(options.PythonExecutablePath),
        Resolve(options.WorkerScriptPath),
        Resolve(options.MeloTtsSourcePath),
        Resolve(options.AcousticModelPath),
        Resolve(options.BertModelPath),
        Resolve(options.NltkDataPath));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.AutoStartWorker) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var configurationError = GetConfigurationError();
            if (configurationError is not null)
            {
                SetRuntimeError(configurationError);
                await DelayRestartAsync(stoppingToken);
                continue;
            }

            try
            {
                var paths = ResolvePaths();
                var uri = new Uri(options.BaseAddress);
                using var process = StartWorker(paths, uri);
                lock (statusGate) workerProcess = process;
                SetRuntimeError(null);
                logger.LogInformation("MeloTTS local Worker started with PID {ProcessId} on {BaseAddress}.",
                    process.Id, options.BaseAddress);
                await process.WaitForExitAsync(stoppingToken);
                if (!stoppingToken.IsCancellationRequested)
                {
                    SetRuntimeError($"MeloTTS Worker 已退出（代码 {process.ExitCode}），正在重启。");
                    logger.LogWarning("MeloTTS local Worker exited with code {ExitCode}; restart is scheduled.",
                        process.ExitCode);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SetRuntimeError("MeloTTS Worker 启动失败，正在重试。");
                logger.LogError(exception, "Could not start MeloTTS local Worker.");
            }
            finally
            {
                lock (statusGate) workerProcess = null;
            }

            await DelayRestartAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (statusGate) process = workerProcess;
        if (process is { HasExited: false })
        {
            try { process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception exception)
            {
                logger.LogWarning(exception, "Could not stop MeloTTS local Worker process cleanly.");
            }
        }
        await base.StopAsync(cancellationToken);
    }

    private Process StartWorker(MeloTtsResolvedPaths paths, Uri uri)
    {
        var info = new ProcessStartInfo
        {
            FileName = paths.PythonExecutable,
            WorkingDirectory = Path.GetDirectoryName(paths.WorkerScript) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(paths.WorkerScript);
        info.ArgumentList.Add("--host");
        info.ArgumentList.Add(uri.Host);
        info.ArgumentList.Add("--port");
        info.ArgumentList.Add(uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--melotts-source");
        info.ArgumentList.Add(paths.MeloTtsSource);
        info.ArgumentList.Add("--acoustic-model");
        info.ArgumentList.Add(paths.AcousticModel);
        info.ArgumentList.Add("--bert-model");
        info.ArgumentList.Add(paths.BertModel);
        info.ArgumentList.Add("--nltk-data");
        info.ArgumentList.Add(paths.NltkData);
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        info.Environment["HF_DATASETS_OFFLINE"] = "1";
        info.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        info.Environment["PYTHONUTF8"] = "1";
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) logger.LogInformation("MeloTTS Worker: {Message}", eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) logger.LogWarning("MeloTTS Worker: {Message}", eventArgs.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("The MeloTTS Worker process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void SetRuntimeError(string? value)
    {
        lock (statusGate) runtimeError = value;
    }

    private async Task DelayRestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(500, options.RestartDelayMilliseconds)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static string Resolve(string configuredPath) => Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
}

public sealed record MeloTtsResolvedPaths(
    string PythonExecutable,
    string WorkerScript,
    string MeloTtsSource,
    string AcousticModel,
    string BertModel,
    string NltkData);
