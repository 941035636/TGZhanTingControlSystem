using System.Diagnostics;

namespace TG.Control.Launcher;

internal sealed class RuntimeSupervisor(LauncherConfiguration configuration, LauncherLog log) : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private Process? touchProcess;
    private Process? ledProcess;
    private DateTimeOffset touchRestartAfter;
    private DateTimeOffset ledRestartAfter;
    private bool touchStoppedByOperator;
    private bool ledStoppedByOperator;
    private bool stopping;

    public event Action<RuntimeSnapshot>? StatusChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        log.Write("Runtime supervision started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            var serverOnline = await IsServerOnlineAsync(cancellationToken);
            if (serverOnline)
            {
                if (configuration.AutoStartTouchClient && !touchStoppedByOperator) touchProcess = EnsureProcess(
                    touchProcess, configuration.TouchClientExecutable, "TG_TOUCH_CLIENT_CONFIG",
                    configuration.TouchClientConfiguration, configuration.TouchClientLogFile,
                    "TouchClient", ref touchRestartAfter);
                if (configuration.AutoStartLedPlayer && !ledStoppedByOperator) ledProcess = EnsureProcess(
                    ledProcess, configuration.LedPlayerExecutable, "TG_LED_PLAYER_CONFIG",
                    configuration.LedPlayerConfiguration, configuration.LedPlayerLogFile,
                    "LedPlayer", ref ledRestartAfter);
            }

            StatusChanged?.Invoke(new RuntimeSnapshot(serverOnline, IsRunning(touchProcess), IsRunning(ledProcess),
                serverOnline ? "系统服务在线" : "正在等待系统服务"));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, configuration.HealthPollSeconds)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        log.Write("Runtime supervision stopped.");
    }

    public void StartTouchNow()
    {
        touchStoppedByOperator = false;
        touchRestartAfter = default;
        touchProcess = StartProcess(configuration.TouchClientExecutable,
            "TG_TOUCH_CLIENT_CONFIG", configuration.TouchClientConfiguration,
            configuration.TouchClientLogFile, "TouchClient");
    }

    public void StartLedNow()
    {
        ledStoppedByOperator = false;
        ledRestartAfter = default;
        ledProcess = StartProcess(configuration.LedPlayerExecutable,
            "TG_LED_PLAYER_CONFIG", configuration.LedPlayerConfiguration,
            configuration.LedPlayerLogFile, "LedPlayer");
    }

    public void StopTouch()
    {
        touchStoppedByOperator = true;
        StopProcess(ref touchProcess, "TouchClient");
    }

    public void StopLed()
    {
        ledStoppedByOperator = true;
        StopProcess(ref ledProcess, "LedPlayer");
    }

    private Process? EnsureProcess(Process? current, string executable, string environmentName,
        string configurationPath, string logFile, string displayName, ref DateTimeOffset restartAfter)
    {
        if (IsRunning(current)) return current;
        if (current is not null)
        {
            log.Write($"{displayName} exited with code {SafeExitCode(current)}.");
            current.Dispose();
            current = null;
            restartAfter = DateTimeOffset.Now.AddSeconds(Math.Max(1, configuration.ClientRestartDelaySeconds));
        }
        if (!configuration.AutoRestartClients && restartAfter != default) return null;
        if (DateTimeOffset.Now < restartAfter) return null;
        return StartProcess(executable, environmentName, configurationPath, logFile, displayName);
    }

    private Process? StartProcess(string executable, string environmentName, string configurationPath,
        string logFile, string displayName)
    {
        if (stopping) return null;
        var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executable));
        if (!File.Exists(resolved))
        {
            log.Write($"{displayName} executable is missing: {resolved}");
            return null;
        }
        var existing = FindExistingProcess(resolved);
        if (existing is not null)
        {
            log.Write($"{displayName} is already running with PID {existing.Id}.");
            return existing;
        }
        var info = new ProcessStartInfo
        {
            FileName = resolved,
            WorkingDirectory = Path.GetDirectoryName(resolved)!,
            UseShellExecute = false
        };
        info.Environment[environmentName] = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configurationPath));
        if (!string.IsNullOrWhiteSpace(logFile))
        {
            var resolvedLogFile = Path.GetFullPath(Environment.ExpandEnvironmentVariables(logFile));
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedLogFile)!);
            info.ArgumentList.Add("-logFile");
            info.ArgumentList.Add(resolvedLogFile);
        }
        var process = Process.Start(info);
        log.Write(process is null
            ? $"{displayName} failed to start."
            : $"{displayName} started with PID {process.Id}.");
        return process;
    }

    private static Process? FindExistingProcess(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch { process.Dispose(); }
        }
        return null;
    }

    private async Task<bool> IsServerOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(configuration.ServerHealthUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private void StopProcess(ref Process? process, string displayName)
    {
        if (!IsRunning(process)) return;
        try
        {
            process!.CloseMainWindow();
            if (!process.WaitForExit(3000)) process.Kill(true);
            log.Write($"{displayName} was stopped by the operator.");
        }
        catch (Exception exception) { log.Write($"Could not stop {displayName}: {exception.Message}"); }
        finally { process?.Dispose(); process = null; }
    }

    private static bool IsRunning(Process? process)
    {
        try { return process is { HasExited: false }; }
        catch (InvalidOperationException) { return false; }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    public void Dispose()
    {
        stopping = true;
        http.Dispose();
        touchProcess?.Dispose();
        ledProcess?.Dispose();
    }
}

internal sealed record RuntimeSnapshot(bool ServerOnline, bool TouchClientRunning, bool LedPlayerRunning, string Message);
