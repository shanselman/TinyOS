using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TinyOS.Protocol;

namespace TinyOS_TimeMachine;

internal enum MachineState { Off, Booting, Paused, Running, Blocked, Completed, Fault }

internal sealed record SwapRecord(bool IsOut, SwapPayload Payload);

/// <summary>
/// Worker lifecycle. Every worker message lands in a queue tagged with the session
/// generation that produced it; a 33 ms UI timer drains the queue, drops anything from
/// an older session, updates the model, and renders once if something changed.
/// </summary>
public sealed partial class MainPage
{
    private readonly record struct InboxItem(int Generation, Envelope? Message, bool Exited, int? ExitCode);

    private readonly ConcurrentQueue<InboxItem> _inbox = new();
    private readonly TerminalBuffer _terminal = new();
    private DispatcherQueueTimer? _timer;
    private WorkerSession? _session;
    private int _generation;
    private bool _dirty;

    private Preset _preset = Presets.FirstProgram;
    private Preset? _bootedPreset;
    private MachineState _state = MachineState.Off;
    private string _pauseReason = "";
    private string? _faultText;
    private string? _blockedReason;
    private bool _holdArmedThisRun;
    private bool _holdFired;

    // Latest facts from the worker. Never synthesized.
    private ReadyPayload? _ready;
    private IReadOnlyList<ProgramListing> _listings = Array.Empty<ProgramListing>();
    private int _listingsVersion;
    private uint _virtualMemory;
    private MachineSnapshot? _snapshot;
    private InstructionInfo? _lastInstruction;
    private long _lastInstructionBatch;
    private long _totalInstructions;
    private byte[]? _previousRam;
    private readonly HashSet<int> _changedBytes = new();
    private readonly List<SwapRecord> _swaps = new();
    private readonly Dictionary<string, string> _swapXml = new();
    private int _swapsVersion;
    private int _swapOuts, _swapOutFiles, _swapIns, _swapInFiles;
    private SwapRecord? _trip;

    private string? _note;
    private DateTime _noteUntil;

    private static readonly string WorkerPath = Path.Combine(AppContext.BaseDirectory, "Worker", "TinyOS.Worker.exe");
    private static readonly string ProgramsDirectory = Path.Combine(AppContext.BaseDirectory, "Worker", "Programs");
    private static readonly Lazy<string> SessionsRoot = new(() =>
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "sessions");
        }
        catch (Exception)
        {
            // Unpackaged fallback.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyOSTimeMachine", "sessions");
        }
    });

    private void InitializeSession()
    {
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => Pump();
        _timer.Start();

        // Sweep worker folders left behind by a crash; only folders carrying our marker are touched.
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(SessionsRoot.Value);
                WorkerSession.CleanupStaleSessions(SessionsRoot.Value, TimeSpan.FromMinutes(10));
            }
            catch (Exception) { }
        });
    }

    /// <summary>Stops the worker synchronously. Called when the window closes.</summary>
    public void Shutdown()
    {
        _timer?.Stop();
        var session = _session;
        _session = null;
        _generation++;
        if (session is null) return;
        try
        {
            Task.Run(() => session.StopAsync(TimeSpan.FromSeconds(2))).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception) { }
    }

    private void StartSession(string? thenCommand, bool warmUp = false)
    {
        StopSession();
        int generation = ++_generation;
        ResetModel();
        _bootedPreset = _preset;
        _state = MachineState.Booting;
        _holdArmedThisRun = HoldSwapToggle.IsChecked == true;

        try
        {
            Directory.CreateDirectory(SessionsRoot.Value);
            _session = WorkerSession.Start(new WorkerLaunchOptions(
                WorkerPath,
                SessionsRoot.Value,
                ProgramsDirectory,
                _preset,
                LegacyDumpsToggle.IsChecked == true,
                message => _inbox.Enqueue(new InboxItem(generation, message, false, null)),
                code => _inbox.Enqueue(new InboxItem(generation, null, true, code))));
        }
        catch (Exception ex)
        {
            _state = MachineState.Fault;
            _faultText = "could not start the TinyOS worker: " + ex.Message;
            _dirty = true;
            return;
        }

        Send(CommandTypes.SetSpeed, new SetSpeedCommand(SelectedSpeed));
        if (_holdArmedThisRun) Send(CommandTypes.HoldOnNextSwap, new HoldOnNextSwapCommand(true));
        if (thenCommand == CommandTypes.Run) Send(CommandTypes.Run);
        else if (thenCommand == CommandTypes.Step) Send(CommandTypes.Step, new StepCommand(1));

        PlayWarmUp(full: warmUp);
        _dirty = true;
    }

    private void PowerOff()
    {
        StopSession();
        ResetModel();
        _state = MachineState.Off;
        _note = null;
        _dirty = true;
    }

    private void StopSession()
    {
        var old = _session;
        _session = null;
        _generation++;
        if (old is not null) _ = Task.Run(() => old.StopAsync(TimeSpan.FromSeconds(2)));
    }

    private void Send(string type, object? payload = null)
    {
        try { _session?.Send(type, payload); }
        catch (Exception ex) { Note("Could not reach the worker: " + ex.Message); }
    }

    private void ResetModel()
    {
        _terminal.Clear();
        _ready = null;
        _listings = Array.Empty<ProgramListing>();
        _listingsVersion++;
        _virtualMemory = 0;
        _snapshot = null;
        _lastInstruction = null;
        _lastInstructionBatch = 0;
        _totalInstructions = 0;
        _previousRam = null;
        _changedBytes.Clear();
        _swaps.Clear();
        _swapXml.Clear();
        _swapsVersion++;
        _swapOuts = _swapOutFiles = _swapIns = _swapInFiles = 0;
        _trip = null;
        _holdFired = false;
        _pauseReason = "";
        _faultText = null;
        _blockedReason = null;
        _selectedPid = null;
        _sourcePid = null;
        _sourcePinned = false;
        _followFrames = true;
        _framePage = 0;
        _selectedFrame = 0;
        _scrollSourceToNext = true;
        HideTripCard();
    }

    private void Note(string text)
    {
        _note = text;
        _noteUntil = DateTime.UtcNow.AddSeconds(3.5);
        _dirty = true;
    }

    private void Pump()
    {
        int budget = 5000;
        while (budget-- > 0 && _inbox.TryDequeue(out var item))
        {
            if (item.Generation != _generation) continue;
            if (item.Exited) OnWorkerExited(item.ExitCode);
            else if (item.Message is { } message) Handle(message);
            _dirty = true;
        }

        if (_note is not null && DateTime.UtcNow > _noteUntil)
        {
            _note = null;
            _dirty = true;
        }

        if (_dirty)
        {
            _dirty = false;
            Render();
        }
    }

    private void Handle(Envelope message)
    {
        switch (message.Type)
        {
            case EventTypes.Ready:
                _ready = message.PayloadAs<ReadyPayload>();
                break;

            case EventTypes.Output:
                _terminal.Append(message.PayloadAs<OutputPayload>()?.Text ?? "");
                break;

            case EventTypes.Loaded:
                if (message.PayloadAs<LoadedPayload>() is { } loaded)
                {
                    _listings = loaded.Programs;
                    _listingsVersion++;
                    _virtualMemory = loaded.VirtualMemory;
                    ApplySnapshot(loaded.Snapshot, freshReport: true);
                }
                break;

            case EventTypes.InstructionCompleted:
                if (message.PayloadAs<InstructionCompletedPayload>() is { } done)
                {
                    _totalInstructions = done.TotalInstructions;
                    _lastInstruction = done.Instruction;
                    _lastInstructionBatch = done.InstructionsSinceLastReport;
                    ApplySnapshot(done.Snapshot, freshReport: true);
                    if (_state is MachineState.Blocked or MachineState.Booting) _state = MachineState.Running;
                }
                break;

            case EventTypes.Paused:
                if (message.PayloadAs<PausedPayload>() is { } paused)
                {
                    _state = MachineState.Paused;
                    _pauseReason = paused.Reason;
                    _note = null;
                    ApplySnapshot(paused.Snapshot, freshReport: false);
                    if (paused.Reason == "swap") OnHeldOnSwap();
                }
                break;

            case EventTypes.Running:
                if (_state is not (MachineState.Completed or MachineState.Fault)) _state = MachineState.Running;
                break;

            case EventTypes.Blocked:
                _state = MachineState.Blocked;
                _blockedReason = message.PayloadAs<BlockedPayload>()?.Reason;
                break;

            case EventTypes.SwapOut:
            case EventTypes.SwapIn:
                if (message.PayloadAs<SwapPayload>() is { } swap) RecordSwap(message.Type == EventTypes.SwapOut, swap);
                break;

            case EventTypes.Completed:
                if (message.PayloadAs<CompletedPayload>() is { } completed)
                {
                    _totalInstructions = completed.TotalInstructions;
                    ApplySnapshot(completed.Snapshot, freshReport: false);
                }
                _state = MachineState.Completed;
                _note = null;
                if (_holdArmedThisRun && !_holdFired)
                {
                    SetHoldToggle(false);
                    Note("Finished without touching a swap file · nothing to hold on");
                }
                break;

            case EventTypes.Fault:
                var fault = message.PayloadAs<FaultPayload>();
                _state = MachineState.Fault;
                _note = null;
                _faultText = fault is null ? "the engine reported a fault" : fault.ExceptionType + ": " + fault.Message;
                break;

            case EventTypes.CommandAcknowledged:
                if (message.PayloadAs<CommandAcknowledgedPayload>() is { Accepted: false } rejected)
                    Note("Worker refused " + rejected.CommandType + ": " + rejected.Message);
                break;
        }
    }

    private void OnWorkerExited(int? exitCode)
    {
        var session = _session;
        _session = null;
        if (_state is not (MachineState.Completed or MachineState.Fault))
        {
            _state = MachineState.Fault;
            string? last = session?.Diagnostics.LastOrDefault();
            _faultText = "the worker exited unexpectedly (code " + (exitCode?.ToString() ?? "?") + ")" + (last is null ? "" : " · " + last);
        }
        // Removes this run's private folder (including any swap files).
        if (session is not null) _ = Task.Run(() => session.StopAsync(TimeSpan.FromSeconds(1)));
    }

    private void ApplySnapshot(MachineSnapshot snapshot, bool freshReport)
    {
        byte[] ram = snapshot.PhysicalMemory;
        if (freshReport) _changedBytes.Clear();
        if (_previousRam is not null && _previousRam.Length == ram.Length)
        {
            for (int i = 0; i < ram.Length; i++)
                if (ram[i] != _previousRam[i]) _changedBytes.Add(i);
        }
        _previousRam = ram;
        _snapshot = snapshot;

        if (_followFrames && _changedBytes.Count > 0 && snapshot.PageSize > 0)
        {
            _selectedFrame = (int)(_changedBytes.Min() / snapshot.PageSize);
            _framePage = _selectedFrame / FramesPerPage;
        }
        _scrollSourceToNext = true;
    }

    private void RecordSwap(bool isOut, SwapPayload swap)
    {
        _swaps.Add(new SwapRecord(isOut, swap));
        if (_swaps.Count > 400) _swaps.RemoveRange(0, _swaps.Count - 400);
        if (isOut)
        {
            _swapOuts++;
            if (swap.UsedSwapFile) _swapOutFiles++;
            if (swap.Xml is not null) _swapXml[swap.FileName] = swap.Xml;
        }
        else
        {
            _swapIns++;
            if (swap.UsedSwapFile) _swapInFiles++;
        }
        _swapsVersion++;
    }

    private void OnHeldOnSwap()
    {
        _holdFired = true;
        SetHoldToggle(false);
        // Prefer the write (it has the XML) when the same instruction both wrote and read a file.
        var lastFile = _swaps.LastOrDefault(s => s.Payload.UsedSwapFile);
        if (lastFile is null) return;
        var trip = !lastFile.IsOut
                   && _swaps.LastOrDefault(s => s.IsOut && s.Payload.UsedSwapFile && s.Payload.Clock == lastFile.Payload.Clock) is { } sameInstructionOut
            ? sameInstructionOut
            : lastFile;

        _trip = trip;
        TabMemory.IsChecked = true;
        if (_snapshot is { PageSize: > 0 } snap)
        {
            _followFrames = false;
            _selectedFrame = (int)(trip.Payload.Page.AddrPhysical / snap.PageSize);
            _framePage = _selectedFrame / FramesPerPage;
        }
        ShowTripCard(trip);
    }

    private void SetHoldToggle(bool on)
    {
        _suppressToggleEvents = true;
        HoldSwapToggle.IsChecked = on;
        _suppressToggleEvents = false;
    }
}
