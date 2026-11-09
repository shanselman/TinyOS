# TinyOS Time Machine

A native WinUI 3 window that runs the real 2002 CST352 TinyOS engine on a beige CRT and
shows its registers, memory, and swap files in a paper notebook beside it. Nothing in the
notebook is invented: every value comes from the running engine.

## Build, run, test

Requirements: Windows 11 x64 and the .NET 10 SDK. Visual Studio is optional; the `winapp`
tooling `dotnet run` needs comes in as a NuGet package.

```powershell
# Engine tests (no UI): real execution, observer purity, file safety, reset, completion
dotnet test tests\TinyOS.Tests

# Build and launch the packaged app (x64 only). Close any running copy first, or the build
# can't overwrite its exe. The console stays attached until you close the app.
dotnet run --project src\TinyOS.TimeMachine -p:Platform=x64
```

The WinUI app is intentionally **not** in `TinyOS.sln`, so `dotnet build` and `dotnet test`
on the solution keep working on any machine.

## The two-minute demo

1. **Disk A · My old code is still here.** Tap the disk; the original 2002 banner prints.
   Press **F10** once and point at the one register that turned orange. Press **Space** to run.
2. **Disk C · Taking turns.** Press F10 until the scheduler switches from P1 to P2 (each
   program gets five instructions); the terminal prints "Switching out Process 1 with ip at 41".
   Tap **P1** in the notebook: its saved PCB shows ip 41, the same number the 2002 code just
   printed. Tap **← back to the CPU**.
3. **Disk B · Small RAM (optional).** Turn on **Hold on swap**, choose **Fast**, press Space.
   The machine pauses on the next page eviction and shows the real XML swap file.
   "Yes, those are XML swap files."
4. **Provenance.** Open the card: what is 2002 code, what is new, the commit, and the runtimes.

## Controls

| Key / control | Action |
| --- | --- |
| Space | Run / Pause |
| F10 | Step one instruction |
| Ctrl+R | Reset (reload the same disk) |
| F11 / Esc | Enter / leave presentation mode |
| Ctrl+1 / 2 / 3 | Registers / Memory / Source tab |
| Tap a disk | Restart with that disk and play the power-on animation |
| Hex | Show registers in hexadecimal |
| CRT glass | Curvature, scanlines, and glow on or off |
| 2002 dumps | Turn on the engine's own memory and register dumps (restarts the machine) |
| Transcript | Everything TinyOS printed, with Copy all |

## Known limitations

- Swaps that happen while programs are loading are not shown; the observer attaches after boot.
- The terminal wraps at about 65 columns.
- Disk B Small RAM swaps constantly, so use Fast or Hold on swap rather than watching every step.

## Recovery

1. Press **Space** to pause, then **Ctrl+R** (or click **Reset**) to reset.
2. If that doesn't help, tap **Disk A**, the known-good disk.
3. Press **Power** off and on, which starts a fresh worker.
4. As a last resort, close and relaunch the app. Each worker runs in its own session folder
   under the app's LocalCache, which is cleaned up when the worker exits.

## Provenance

- **2002 (unchanged behavior):** the CPU, opcodes, scheduler, paging memory manager, swap
  files, and sample programs in `src\TinyOSCore` and `Sample Programs`.
- **Small 2026 changes to the core:** `HostHooks.cs` adds an observer interface the engine calls
  around each instruction and page swap; `OS.cs` and `MemoryManager.cs` call it and expose
  read-only views. With no observer attached, behavior is identical (covered by tests).
  Swap-file cleanup now deletes only `page*.xml` instead of every `*.xml` in the folder.
- **New 2026 host:** `src\TinyOS.Worker` (console worker that runs the engine in a private
  session folder and reports JSON-line snapshots), `src\TinyOS.Protocol` (shared messages and
  disk presets), and `src\TinyOS.TimeMachine` (the WinUI 3 app).
- **Visual fiction:** the beige case, curved glass, scanlines, glow, floppy disks, key caps,
  paper, and handwriting.

A future idea: rebuild the notebook with [Microsoft UI Reactor](https://github.com/microsoft/microsoft-ui-reactor).
The scene stays in XAML for now because it relies on custom templates and Composition effects.
