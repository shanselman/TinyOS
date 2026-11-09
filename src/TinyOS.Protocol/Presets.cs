namespace TinyOS.Protocol;

/// <summary>
/// A curated, validated way to boot the original OS. Programs are the unchanged
/// files from the repository's "Sample Programs" folder.
/// </summary>
public sealed record Preset(
    string Id,
    string Title,
    string Caption,
    uint VirtualMemory,
    IReadOnlyList<string> Programs,
    IReadOnlyDictionary<string, string> SettingOverrides);

public static class Presets
{
    /// <summary>The 2002 appsettings.json values, unchanged.</summary>
    public static readonly IReadOnlyDictionary<string, string> OriginalSettings = new Dictionary<string, string>
    {
        ["PhysicalMemory"] = "128",
        ["ProcessMemory"] = "384",
        ["DumpPhysicalMemory"] = "true",
        ["DumpInstruction"] = "true",
        ["DumpRegisters"] = "true",
        ["DumpProgram"] = "true",
        ["DumpContextSwitch"] = "true",
        ["PauseOnExit"] = "false",
        ["SharedMemoryRegionSize"] = "16",
        ["NumOfSharedMemoryRegions"] = "4",
        ["MemoryPageSize"] = "16",
        ["StackSize"] = "16",
        ["DataSize"] = "16",
    };

    /// <summary>
    /// The notebook replaces the giant register and memory dumps, so the CRT keeps only
    /// the instruction trace, context switches, and program output.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotebookDisplay = new Dictionary<string, string>
    {
        ["DumpPhysicalMemory"] = "false",
        ["DumpRegisters"] = "false",
    };

    private static readonly string[] SixPrograms = ["scott1.txt", "scott2.txt", "scott3.txt", "scott4.txt", "scott5.txt", "scott6.txt"];

    public static readonly Preset FirstProgram = new(
        "first-program",
        "A · My old code is still here",
        "scott1.txt: load a big number, copy it, add five seven times, print it.",
        1024,
        ["scott1.txt"],
        new Dictionary<string, string>());

    public static readonly Preset SmallRam = new(
        "small-ram",
        "B · Small RAM (128 bytes)",
        "Six sample programs, 5760 bytes of virtual memory, the original 128 bytes of physical RAM. Evicted dirty pages are written to disk as XML.",
        5760,
        SixPrograms,
        new Dictionary<string, string>());

    public static readonly Preset ComfortableRam = new(
        "comfortable-ram",
        "B · Comfortable RAM (5760 bytes)",
        "The same six programs and time slices, but every page fits in physical RAM: no page faults, no swap files.",
        5760,
        SixPrograms,
        new Dictionary<string, string> { ["PhysicalMemory"] = "5760" });

    public static readonly Preset TakingTurns = new(
        "taking-turns",
        "C · Taking turns",
        "testRegisters.bat: scott1.txt + scott3.txt share one CPU, five instructions at a time.",
        1024,
        ["scott1.txt", "scott3.txt"],
        new Dictionary<string, string>());

    public static IReadOnlyList<Preset> All { get; } = [FirstProgram, SmallRam, ComfortableRam, TakingTurns];
    public static IReadOnlyDictionary<string, string> BuildSettings(Preset preset, bool legacyDumps)
    {
        var settings = new Dictionary<string, string>(OriginalSettings);
        if (!legacyDumps)
        {
            foreach (var kv in NotebookDisplay) settings[kv.Key] = kv.Value;
        }
        foreach (var kv in preset.SettingOverrides) settings[kv.Key] = kv.Value;
        return settings;
    }
}
