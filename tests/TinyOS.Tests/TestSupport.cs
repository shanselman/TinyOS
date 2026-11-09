using System.Collections.Concurrent;
using System.Diagnostics;
using TinyOS.Protocol;

namespace TinyOS.Tests;

internal static class Paths
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string WorkerExe => Path.Combine(RepoRoot, "src", "TinyOS.Worker", "bin", Configuration, "net10.0", "TinyOS.Worker.exe");

    public static string ProgramsDirectory => Path.Combine(RepoRoot, "src", "TinyOS.Worker", "bin", Configuration, "net10.0", "Programs");

    public static string OriginalConsoleDll => Path.Combine(RepoRoot, "src", "TinyOSCore", "bin", Configuration, "net6.0", "TinyOSCore.dll");

    public static string NewTempDirectory(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TinyOS.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find TinyOS.sln above the test directory.");
    }
}

/// <summary>Starts one worker session and records every message it sends.</summary>
internal sealed class Harness : IAsyncDisposable
{
    private readonly BlockingCollection<Envelope> _inbox = new();
    private readonly List<Envelope> _all = new();
    public WorkerSession Session { get; }
    public string SessionsRoot { get; }
    public int? ExitCode { get; private set; }
    public bool Exited { get; private set; }

    public Harness(Preset preset, bool legacyDumps = false, string? sessionsRoot = null)
    {
        SessionsRoot = sessionsRoot ?? Paths.NewTempDirectory("tinyos-tests-sessions");
        Session = WorkerSession.Start(new WorkerLaunchOptions(
            Paths.WorkerExe,
            SessionsRoot,
            Paths.ProgramsDirectory,
            preset,
            legacyDumps,
            OnMessage: m => _inbox.Add(m),
            OnExited: code => { ExitCode = code; Exited = true; }));
    }

    public IReadOnlyList<Envelope> All => _all;

    public Envelope WaitFor(string type, TimeSpan? timeout = null) => WaitFor(m => m.Type == type, timeout, type);

    public Envelope WaitFor(Func<Envelope, bool> match, TimeSpan? timeout = null, string what = "message")
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        while (true)
        {
            var remaining = limit - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero || !_inbox.TryTake(out var m, remaining))
                throw new TimeoutException($"Timed out waiting for {what}. Diagnostics: {string.Join(" | ", Session.Diagnostics)}");
            _all.Add(m);
            if (m.Type == EventTypes.Fault)
                throw new InvalidOperationException("Worker fault: " + m.Payload);
            if (match(m)) return m;
        }
    }

    public string OutputText => string.Concat(_all.Where(m => m.Type == EventTypes.Output).Select(m => m.PayloadAs<OutputPayload>()!.Text));

    public async ValueTask DisposeAsync()
    {
        await Session.StopAsync(TimeSpan.FromSeconds(5));
        try { Directory.Delete(SessionsRoot, true); } catch (IOException) { }
    }
}

internal static class OriginalConsole
{
    /// <summary>Runs the original console TinyOS in an isolated temp directory and returns its output.</summary>
    public static string Run(uint vmem, params string[] programs)
    {
        var dir = Paths.NewTempDirectory("tinyos-console");
        try
        {
            File.WriteAllText(Path.Combine(dir, "appsettings.json"), System.Text.Json.JsonSerializer.Serialize(Presets.OriginalSettings));
            foreach (var p in programs) File.Copy(Path.Combine(Paths.ProgramsDirectory, p), Path.Combine(dir, p));

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Paths.OriginalConsoleDll);
            psi.ArgumentList.Add(vmem.ToString());
            foreach (var p in programs) psi.ArgumentList.Add(p);
            psi.Environment["DOTNET_ROLL_FORWARD"] = "Major";

            using var proc = Process.Start(psi)!;
            proc.StandardInput.Close();
            var text = proc.StandardOutput.ReadToEnd();
            Assert.True(proc.WaitForExit(30000), "console TinyOS did not exit");
            return text;
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Lines for comparison. Line 2 is the hosting assembly's name, which legitimately differs.</summary>
    public static string[] ComparableLines(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 1) lines.RemoveAt(1);
        return lines.ToArray();
    }
}
