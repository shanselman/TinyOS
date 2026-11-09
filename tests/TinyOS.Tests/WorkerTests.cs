using System.Diagnostics;
using TinyOS.Protocol;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TinyOS.Tests;

public class WorkerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(60);

    private static void Run(Harness h, double speed = 0)
    {
        h.Session.Send(CommandTypes.SetSpeed, new SetSpeedCommand(speed));
        h.Session.Send(CommandTypes.Run);
    }

    [Fact]
    public async Task FirstProgram_output_matches_original_console()
    {
        var expected = OriginalConsole.ComparableLines(OriginalConsole.Run(1024, "scott1.txt"));

        await using var h = new Harness(Presets.FirstProgram, legacyDumps: true);
        h.WaitFor(EventTypes.Loaded);
        Run(h);
        h.WaitFor(EventTypes.Completed, Long);

        Assert.Equal(expected, OriginalConsole.ComparableLines(h.OutputText));
    }

    [Fact]
    public async Task Stepping_and_snapshots_do_not_change_engine_behavior()
    {
        // If observation touched the MemoryManager indexer, access counts, page faults,
        // or swap decisions would drift and the legacy dumps would differ.
        var expected = OriginalConsole.ComparableLines(OriginalConsole.Run(1024, "scott1.txt"));

        await using var h = new Harness(Presets.FirstProgram, legacyDumps: true);
        h.WaitFor(EventTypes.Loaded);
        while (true)
        {
            h.Session.Send(CommandTypes.Step);
            var m = h.WaitFor(e => e.Type is EventTypes.Paused or EventTypes.Completed, Long, "paused/completed");
            if (m.Type == EventTypes.Completed) break;
        }

        Assert.Equal(expected, OriginalConsole.ComparableLines(h.OutputText));
    }

    [Fact]
    public async Task SmallRam_output_matches_original_console()
    {
        var p = Presets.SmallRam;
        var expected = OriginalConsole.ComparableLines(OriginalConsole.Run(p.VirtualMemory, p.Programs.ToArray()));

        await using var h = new Harness(p, legacyDumps: true);
        h.WaitFor(EventTypes.Loaded);
        Run(h);
        h.WaitFor(EventTypes.Completed, Long);

        Assert.Equal(expected, OriginalConsole.ComparableLines(h.OutputText));
    }

    [Fact]
    public async Task Step_executes_exactly_one_instruction_and_aliases_sp_ip()
    {
        await using var h = new Harness(Presets.FirstProgram);
        var loaded = h.WaitFor(EventTypes.Loaded).PayloadAs<LoadedPayload>()!;
        Assert.Null(loaded.Snapshot.CurrentPid);

        h.Session.Send(CommandTypes.Step);
        var done = h.WaitFor(EventTypes.InstructionCompleted).PayloadAs<InstructionCompletedPayload>()!;
        h.WaitFor(EventTypes.Paused);

        Assert.Equal(1, done.TotalInstructions);
        Assert.Equal(1, done.InstructionsSinceLastReport);
        Assert.Equal("Movi", done.Instruction.OpCode);
        Assert.Equal(1, done.Instruction.SourceLineNumber);
        Assert.Equal(0u, done.Instruction.IpBefore);
        Assert.Equal(9u, done.Instruction.IpAfter);
        Assert.Equal(3276834u, done.Snapshot.Registers![1]);
        Assert.Equal(done.Snapshot.Registers[11], done.Instruction.IpAfter); // R11 is ip
        Assert.Equal(383u, done.Snapshot.Registers[10]);                    // R10 is sp
        Assert.Equal(1u, done.Snapshot.Registers[8]);                       // R8 is pid
        Assert.Contains(1, done.Instruction.ChangedRegisters);
        Assert.Contains(11, done.Instruction.ChangedRegisters);
        Assert.Equal(1u, done.Snapshot.Clock);

        // Nothing more runs while paused.
        await Task.Delay(300);
        h.Session.Send(CommandTypes.Step);
        var second = h.WaitFor(EventTypes.InstructionCompleted).PayloadAs<InstructionCompletedPayload>()!;
        Assert.Equal(2, second.TotalInstructions);
        Assert.Equal("Movr", second.Instruction.OpCode);
        Assert.Equal(3276834u, second.Snapshot.Registers![2]);
    }

    [Fact]
    public async Task Step_count_runs_exactly_that_many_and_pause_holds()
    {
        await using var h = new Harness(Presets.FirstProgram);
        h.WaitFor(EventTypes.Loaded);
        h.WaitFor(m => m.Type == EventTypes.Paused && m.PayloadAs<PausedPayload>()!.Reason == "loaded");

        h.Session.Send(CommandTypes.Step, new StepCommand(3));
        var a = h.WaitFor(m => m.Type == EventTypes.Paused && m.PayloadAs<PausedPayload>()!.Reason == "step").PayloadAs<PausedPayload>()!.Snapshot;
        Assert.Equal(3u, a.Clock);
        Assert.Equal(3, h.All.Count(m => m.Type == EventTypes.InstructionCompleted));

        // While paused, nothing executes.
        await Task.Delay(400);
        Assert.Equal(3, h.All.Count(m => m.Type == EventTypes.InstructionCompleted));

        h.Session.Send(CommandTypes.Step);
        var next = h.WaitFor(m => m.Type == EventTypes.InstructionCompleted && m.PayloadAs<InstructionCompletedPayload>()!.TotalInstructions == 4)
            .PayloadAs<InstructionCompletedPayload>()!;
        Assert.Equal(4u, next.Snapshot.Clock);
        Assert.Equal(a.Registers![11], next.Instruction.IpBefore);
    }
    [Fact]
    public async Task Completion_is_not_a_kill_and_session_directory_is_cleaned_up()
    {
        var h = new Harness(Presets.FirstProgram);
        try
        {
            h.WaitFor(EventTypes.Loaded);
            Run(h);
            var completed = h.WaitFor(EventTypes.Completed, Long).PayloadAs<CompletedPayload>()!;
            Assert.Equal(12, completed.TotalInstructions);
            Assert.Empty(completed.Snapshot.Processes);

            await Task.Delay(300);
            Assert.False(h.Session.HasExited);
            Assert.True(Directory.Exists(h.Session.SessionDirectory));
        }
        finally
        {
            await h.Session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(0, h.ExitCode);
        Assert.False(Directory.Exists(h.Session.SessionDirectory));
        Directory.Delete(h.SessionsRoot, true);
    }

    [Fact]
    public async Task Swap_files_stay_in_the_private_session_directory()
    {
        var outside = Paths.NewTempDirectory("tinyos-sentinel");
        var sentinel = Path.Combine(outside, "keep-me.xml");
        File.WriteAllText(sentinel, "<sentinel/>");
        var sessionsRoot = Path.Combine(outside, "sessions");
        var rootSentinel = Path.Combine(outside, "page1-16.xml");
        File.WriteAllText(rootSentinel, "<not-a-swap-file/>");

        await using (var h = new Harness(Presets.SmallRam, sessionsRoot: sessionsRoot))
        {
            h.WaitFor(EventTypes.Loaded);
            h.Session.Send(CommandTypes.HoldOnNextSwap, new HoldOnNextSwapCommand(true));
            Run(h);
            var paused = h.WaitFor(m => m.Type == EventTypes.Paused && m.PayloadAs<PausedPayload>()!.Reason != "loaded", Long, "swap pause").PayloadAs<PausedPayload>()!;
            Assert.Equal("swap", paused.Reason);

            var swap = h.All.Where(m => m.Type == EventTypes.SwapOut).Select(m => m.PayloadAs<SwapPayload>()!).First(s => s.UsedSwapFile);
            Assert.Contains("MemoryPageValue", swap.Xml);
            Assert.StartsWith("page", swap.FileName);
            Assert.True(File.Exists(Path.Combine(h.Session.SessionDirectory, swap.FileName)) || h.All.Any(m => m.Type == EventTypes.SwapIn));

            Run(h);
            h.WaitFor(EventTypes.Completed, Long);
        }

        Assert.True(File.Exists(sentinel));
        Assert.True(File.Exists(rootSentinel));
        Directory.Delete(outside, true);
    }

    [Fact]
    public void Worker_refuses_to_start_without_a_session_marker()
    {
        var dir = Paths.NewTempDirectory("tinyos-nomarker");
        var sentinel = Path.Combine(dir, "important.xml");
        File.WriteAllText(sentinel, "<keep/>");
        File.Copy(Path.Combine(Paths.ProgramsDirectory, "scott1.txt"), Path.Combine(dir, "scott1.txt"));
        try
        {
            var psi = new ProcessStartInfo(Paths.WorkerExe)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "--session", "abc", "--vmem", "1024", "scott1.txt" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(10000));
            Assert.Equal(2, p.ExitCode);
            Assert.Equal("", stdout);
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Ten_start_stop_cycles_leave_no_workers_or_directories()
    {
        var root = Paths.NewTempDirectory("tinyos-cycles");
        var pids = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            var h = new Harness(i % 2 == 0 ? Presets.FirstProgram : Presets.TakingTurns, sessionsRoot: root);
            h.WaitFor(EventTypes.Loaded);
            if (i % 3 == 0) Run(h, speed: 20);
            pids.Add(h.Session.WorkerProcessId);
            await h.Session.StopAsync(TimeSpan.FromSeconds(5));
        }

        foreach (var pid in pids)
        {
            bool alive;
            try { using var p = Process.GetProcessById(pid); alive = !p.HasExited && p.ProcessName.StartsWith("TinyOS.Worker"); }
            catch (ArgumentException) { alive = false; }
            Assert.False(alive, $"worker {pid} still running");
        }
        Assert.Empty(Directory.GetDirectories(root));
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Comfortable_ram_runs_the_same_programs_without_swapping()
    {
        static async Task<(List<SwapPayload> swaps, string[] lines)> RunToEnd(Preset p)
        {
            await using var h = new Harness(p);
            h.WaitFor(EventTypes.Loaded);
            Run(h);
            h.WaitFor(EventTypes.Completed, Long);
            var swaps = h.All.Where(m => m.Type == EventTypes.SwapOut).Select(m => m.PayloadAs<SwapPayload>()!).ToList();
            var lines = h.OutputText.Split('\n').Where(l => !l.Contains("Page Faults") && !l.Contains("bytes of physical memory")).ToArray();
            return (swaps, lines);
        }

        var small = await RunToEnd(Presets.SmallRam);
        var comfy = await RunToEnd(Presets.ComfortableRam);

        Assert.Contains(small.swaps, s => s.UsedSwapFile);
        Assert.Empty(comfy.swaps);
        Assert.Equal(small.lines, comfy.lines);
    }

    [Fact]
    public async Task TakingTurns_switches_between_processes()
    {
        await using var h = new Harness(Presets.TakingTurns);
        h.WaitFor(EventTypes.Loaded);
        Run(h, speed: 0);
        h.WaitFor(EventTypes.Completed, Long);
        Assert.Contains("Switching in Process 2", h.OutputText);
        Assert.Contains("Switching out Process 1", h.OutputText);
    }
}
