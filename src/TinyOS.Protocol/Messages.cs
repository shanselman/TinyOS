using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyOS.Protocol;

/// <summary>
/// Newline-delimited JSON protocol between the Time Machine app and the TinyOS worker process.
/// Every line in either direction is one <see cref="Envelope"/>.
/// </summary>
public static class ProtocolInfo
{
    public const int Version = 1;

    /// <summary>File that must exist in the worker's working directory before it will boot the OS.</summary>
    public const string SessionMarkerFileName = ".tinyos-session";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record Envelope(int ProtocolVersion, string SessionId, long Sequence, string Type, JsonElement? Payload)
{
    public T? PayloadAs<T>() => Payload is { } p ? p.Deserialize<T>(ProtocolInfo.JsonOptions) : default;
}

/// <summary>Events sent from the worker to the app.</summary>
public static class EventTypes
{
    public const string Ready = "ready";
    public const string Loaded = "loaded";
    public const string Output = "output";
    public const string InstructionCompleted = "instructionCompleted";
    public const string Paused = "paused";
    public const string Running = "running";
    public const string Blocked = "blocked";
    public const string SwapOut = "swapOut";
    public const string SwapIn = "swapIn";
    public const string Completed = "completed";
    public const string Fault = "fault";
    public const string CommandAcknowledged = "commandAcknowledged";
}

/// <summary>Commands sent from the app to the worker.</summary>
public static class CommandTypes
{
    public const string Run = "run";
    public const string Pause = "pause";
    public const string Step = "step";
    public const string SetSpeed = "setSpeed";
    public const string HoldOnNextSwap = "holdOnNextSwap";
    public const string Stop = "stop";
}

public sealed record ReadyPayload(int WorkerProcessId, string EngineAssembly, string Runtime);

public sealed record SourceLine(int LineNumber, uint Address, int Length, string OpCode, string Text);

public sealed record ProgramListing(uint Pid, string FileName, IReadOnlyList<SourceLine> Lines);

public sealed record LoadedPayload(
    MachineSnapshot Snapshot,
    IReadOnlyList<ProgramListing> Programs,
    IReadOnlyDictionary<string, string> Settings,
    uint VirtualMemory);

public sealed record OutputPayload(string Text);

public sealed record PageInfo(
    uint PageNumber,
    uint AddrVirtual,
    uint AddrPhysical,
    uint PidOwner,
    uint AddrProcessIndex,
    bool IsValid,
    bool IsDirty,
    uint AccessCount,
    uint PageFaults,
    uint SharedMemoryRegion);

public sealed record ProcessInfo(
    uint Pid,
    string State,
    uint[] Registers,
    bool Sf,
    bool Zf,
    int Priority,
    int ClockCycles,
    int ContextSwitches,
    uint SleepCounter,
    uint WaitingLock,
    uint WaitingEvent,
    uint CodeSize,
    uint ProcessMemorySize);

/// <summary>
/// A by-value copy of the machine. Registers are the live CPU registers when a process is
/// on the CPU, otherwise null. RAM is a raw copy of physical memory.
/// </summary>
public sealed record MachineSnapshot(
    uint Clock,
    uint? CurrentPid,
    uint[]? Registers,
    bool Sf,
    bool Zf,
    uint PageSize,
    byte[] PhysicalMemory,
    IReadOnlyList<PageInfo> Pages,
    IReadOnlyList<ProcessInfo> Processes);

public sealed record InstructionInfo(
    uint Pid,
    uint IpBefore,
    uint IpAfter,
    string OpCode,
    int? SourceLineNumber,
    string? SourceText,
    uint[] RegistersBefore,
    uint[] RegistersAfter,
    int[] ChangedRegisters,
    bool SfBefore,
    bool ZfBefore);

public sealed record InstructionCompletedPayload(long TotalInstructions, long InstructionsSinceLastReport, InstructionInfo Instruction, MachineSnapshot Snapshot);

public sealed record PausedPayload(string Reason, MachineSnapshot Snapshot);

public sealed record BlockedPayload(string Reason);

public sealed record SwapPayload(PageInfo Page, bool UsedSwapFile, string FileName, string? Xml, uint? DuringPid, uint Clock);

public sealed record CompletedPayload(long TotalInstructions, MachineSnapshot Snapshot);

public sealed record FaultPayload(string ExceptionType, string Message);

public sealed record CommandAcknowledgedPayload(long CommandSequence, string CommandType, bool Accepted, string? Message);

public sealed record StepCommand(int Count = 1);

public sealed record SetSpeedCommand(double InstructionsPerSecond);

public sealed record HoldOnNextSwapCommand(bool Enabled);
