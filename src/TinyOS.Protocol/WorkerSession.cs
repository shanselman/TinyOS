using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TinyOS.Protocol;

public sealed record WorkerLaunchOptions(
    string WorkerPath,
    string SessionsRoot,
    string ProgramsDirectory,
    Preset Preset,
    bool LegacyDumps,
    Action<Envelope> OnMessage,
    Action<int?>? OnExited = null);

/// <summary>
/// Owns exactly one TinyOS worker process and its private session directory.
/// The worker always runs with its working directory set to that private directory,
/// because the 2002 memory manager reads and writes swap files in the current directory.
/// </summary>
public sealed class WorkerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly WorkerLaunchOptions _options;
    private readonly object _sendLock = new();
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private long _sequence;
    private int _stopped;

    public string SessionId { get; }
    public string SessionDirectory { get; }
    public int WorkerProcessId { get; }
    public bool HasExited => _process.HasExited;
    public IEnumerable<string> Diagnostics => _diagnostics;

    private WorkerSession(WorkerLaunchOptions options, string sessionId, string sessionDirectory, Process process)
    {
        _options = options;
        SessionId = sessionId;
        SessionDirectory = sessionDirectory;
        _process = process;
        WorkerProcessId = process.Id;
        _stdoutPump = Task.Factory.StartNew(PumpStdout, TaskCreationOptions.LongRunning);
        _stderrPump = Task.Factory.StartNew(PumpStderr, TaskCreationOptions.LongRunning);
    }

    public static WorkerSession Start(WorkerLaunchOptions options)
    {
        if (!Path.IsPathFullyQualified(options.WorkerPath))
            throw new ArgumentException("Worker path must be absolute.", nameof(options));
        if (!File.Exists(options.WorkerPath))
            throw new FileNotFoundException("TinyOS worker not found.", options.WorkerPath);

        var sessionId = Guid.NewGuid().ToString("N");
        var dir = PrepareSessionDirectory(options, sessionId);

        var psi = new ProcessStartInfo(options.WorkerPath)
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("--session");
        psi.ArgumentList.Add(sessionId);
        psi.ArgumentList.Add("--vmem");
        psi.ArgumentList.Add(options.Preset.VirtualMemory.ToString());
        foreach (var program in options.Preset.Programs) psi.ArgumentList.Add(program);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the TinyOS worker.");
        process.StandardInput.AutoFlush = true;
        process.StandardInput.NewLine = "\n";
        return new WorkerSession(options, sessionId, dir, process);
    }

    private static string PrepareSessionDirectory(WorkerLaunchOptions options, string sessionId)
    {
        Directory.CreateDirectory(options.SessionsRoot);
        var dir = Path.Combine(options.SessionsRoot, sessionId);
        Directory.CreateDirectory(dir);

        foreach (var program in options.Preset.Programs)
        {
            if (program.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(program) != program)
                throw new ArgumentException($"Invalid program name '{program}'.");
            File.Copy(Path.Combine(options.ProgramsDirectory, program), Path.Combine(dir, program));
        }

        var settings = Presets.BuildSettings(options.Preset, options.LegacyDumps);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        // Written last: the worker refuses to boot unless this marker names its session.
        File.WriteAllText(Path.Combine(dir, ProtocolInfo.SessionMarkerFileName), sessionId);
        return dir;
    }

    public void Send(string type, object? payload = null)
    {
        lock (_sendLock)
        {
            if (_process.HasExited) return;
            var element = payload is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(payload, payload.GetType(), ProtocolInfo.JsonOptions);
            var envelope = new Envelope(ProtocolInfo.Version, SessionId, ++_sequence, type, element);
            try
            {
                _process.StandardInput.WriteLine(JsonSerializer.Serialize(envelope, ProtocolInfo.JsonOptions));
            }
            catch (IOException)
            {
                // Worker already gone; exit is reported by the stdout pump.
            }
        }
    }

    private void PumpStdout()
    {
        try
        {
            string? line;
            while ((line = _process.StandardOutput.ReadLine()) is not null)
            {
                Envelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<Envelope>(line, ProtocolInfo.JsonOptions);
                }
                catch (JsonException)
                {
                    _diagnostics.Enqueue("Unparseable worker line: " + line);
                    continue;
                }
                if (envelope is null || envelope.ProtocolVersion != ProtocolInfo.Version || envelope.SessionId != SessionId)
                {
                    _diagnostics.Enqueue("Ignored message for another session or protocol: " + line);
                    continue;
                }
                _options.OnMessage(envelope);
            }
        }
        finally
        {
            int? code = null;
            try
            {
                _process.WaitForExit(2000);
                if (_process.HasExited) code = _process.ExitCode;
            }
            catch (InvalidOperationException) { }
            _options.OnExited?.Invoke(code);
        }
    }

    private void PumpStderr()
    {
        string? line;
        while ((line = _process.StandardError.ReadLine()) is not null)
        {
            _diagnostics.Enqueue(line);
            while (_diagnostics.Count > 200) _diagnostics.TryDequeue(out _);
        }
    }

    /// <summary>Asks the worker to stop, then kills only this session's worker if it does not exit in time.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;

        Send(CommandTypes.Stop);
        using (var cts = new CancellationTokenSource(timeout))
        {
            try
            {
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        await Task.WhenAny(Task.WhenAll(_stdoutPump, _stderrPump), Task.Delay(2000)).ConfigureAwait(false);
        DeleteSessionDirectory(SessionDirectory, SessionId);
        _process.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync(TimeSpan.FromSeconds(2)));

    /// <summary>Deletes a session directory only if its marker proves it belongs to <paramref name="sessionId"/>.</summary>
    public static bool DeleteSessionDirectory(string directory, string sessionId)
    {
        var marker = Path.Combine(directory, ProtocolInfo.SessionMarkerFileName);
        try
        {
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != sessionId) return false;
        }
        catch (IOException) { return false; }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
        return false;
    }

    /// <summary>Removes old, marked session directories left behind by a crash.</summary>
    public static void CleanupStaleSessions(string sessionsRoot, TimeSpan olderThan)
    {
        if (!Directory.Exists(sessionsRoot)) return;
        foreach (var dir in Directory.GetDirectories(sessionsRoot))
        {
            var marker = Path.Combine(dir, ProtocolInfo.SessionMarkerFileName);
            if (!File.Exists(marker)) continue;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < olderThan) continue;
            DeleteSessionDirectory(dir, Path.GetFileName(dir));
        }
    }
}
