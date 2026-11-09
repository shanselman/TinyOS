using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Hanselman.CST352;
using Microsoft.Extensions.Configuration;
using TinyOS.Protocol;

namespace TinyOS.Worker
{
	internal sealed class WorkerStopRequestedException : Exception { }

	/// <summary>
	/// Boots the original TinyOS engine exactly as EntryPoint.Main did, then observes it through
	/// <see cref="IHostObserver"/>. All observation happens on the engine's own thread, between
	/// instructions, and never through the MemoryManager indexer (which mutates paging state).
	/// </summary>
	internal sealed class WorkerHost : IHostObserver
	{
		private const int FastReportIntervalMs = 33;

		private readonly string _sessionId;
		private readonly TextWriter _protocolOut;
		private readonly object _writeLock = new object();
		private readonly object _gate = new object();
		private readonly OutputCapture _output = new OutputCapture();
		private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
		private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
		private readonly Dictionary<uint, ProgramListing> _listings = new Dictionary<uint, ProgramListing>();
		private readonly List<SwapPayload> _swapsThisInstruction = new List<SwapPayload>();

		private long _sequence;
		private OS _os;

		// Gate state (guarded by _gate)
		private bool _running;
		private int _stepBudget;
		private bool _stopRequested;
		private double _instructionsPerSecond = 5;
		private bool _holdOnNextSwap;
		private string _pauseReason = "loaded";
		private bool _pauseAnnounced;
		private TimeSpan _lastInstructionStart = TimeSpan.Zero;

		// Per-instruction state (engine thread only)
		private uint _pidBefore;
		private uint[] _registersBefore;
		private bool _sfBefore, _zfBefore;
		private long _totalInstructions;
		private long _unreportedInstructions;
		private TimeSpan _lastReport = TimeSpan.Zero;
		private bool _blockedAnnounced;

		private WorkerHost(string sessionId, TextWriter protocolOut)
		{
			_sessionId = sessionId;
			_protocolOut = protocolOut;
		}

		public static int Main(string[] args)
		{
			var protocolOut = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

			string sessionId = null;
			uint vmem = 0;
			var files = new List<string>();
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--session" && i + 1 < args.Length) sessionId = args[++i];
				else if (args[i] == "--vmem" && i + 1 < args.Length) vmem = uint.Parse(args[++i]);
				else files.Add(args[i]);
			}

			// Refuse to run anywhere except a private session directory prepared by the app.
			// The 2002 memory manager reads, writes, and deletes files in the current directory.
			string marker = Path.Combine(Environment.CurrentDirectory, ProtocolInfo.SessionMarkerFileName);
			if (sessionId == null || !File.Exists(marker) || File.ReadAllText(marker).Trim() != sessionId)
			{
				Console.Error.WriteLine("TinyOS.Worker: refusing to start. The working directory is not a TinyOS session directory for this session id.");
				return 2;
			}
			if (vmem == 0 || files.Count == 0)
			{
				Console.Error.WriteLine("TinyOS.Worker: usage: --session <id> --vmem <bytes> program.txt [...]");
				return 3;
			}

			var host = new WorkerHost(sessionId, protocolOut);
			return host.Run(vmem, files);
		}

		private int Run(uint vmem, List<string> files)
		{
			var commandThread = new Thread(ReadCommands) { IsBackground = true, Name = "TinyOS commands" };
			Console.SetIn(TextReader.Null);
			Console.SetOut(_output);

			EntryPoint.Configuration = new ConfigurationBuilder()
				.SetBasePath(Environment.CurrentDirectory)
				.AddJsonFile("appsettings.json")
				.Build();

			Emit(EventTypes.Ready, new ReadyPayload(Environment.ProcessId, typeof(CPU).Assembly.FullName, RuntimeInformation.FrameworkDescription));
			commandThread.Start();

			try
			{
				Boot(vmem, files);
				HostHooks.Observer = this;
				Emit(EventTypes.Loaded, new LoadedPayload(Snapshot(), _listings.Values.OrderBy(l => l.Pid).ToList(), ReadSettings(), _os.memoryMgr.virtualMemSize));

				_os.execute();

				ReportPending(force: true);
				Emit(EventTypes.Completed, new CompletedPayload(_totalInstructions, Snapshot()));
			}
			catch (WorkerStopRequestedException)
			{
				return 0;
			}
			catch (Exception ex)
			{
				ReportPending(force: true);
				Emit(EventTypes.Fault, new FaultPayload(ex.GetType().Name, ex.Message));
			}

			// Stay alive (with the final state visible) until the app says stop or goes away.
			_stopSignal.Wait();
			return 0;
		}

		/// <summary>Same order as the 2002 EntryPoint.Main.</summary>
		private void Boot(uint vmem, List<string> files)
		{
			EntryPoint.PrintHeader();

			uint bytesOfPhysicalMemory = uint.Parse(EntryPoint.Configuration["PhysicalMemory"]);
			CPU.initPhysicalMemory(bytesOfPhysicalMemory);
			_os = new OS(vmem);
			CPU.theOS = _os;

			Console.WriteLine("CPU has {0} bytes of physical memory", CPU.physicalMemory.Length);
			Console.WriteLine("OS  has {0} bytes of virtual (addressable) memory", _os.memoryMgr.virtualMemSize);

			foreach (string file in files)
			{
				if (!File.Exists(file)) continue;
				Hanselman.CST352.Program p = Hanselman.CST352.Program.LoadProgram(file);
				Process rp = _os.createProcess(p, uint.Parse(EntryPoint.Configuration["ProcessMemory"]));
				Console.WriteLine("Process id {0} has {1} bytes of process memory and {2} bytes of heap", rp.PCB.pid, EntryPoint.Configuration["ProcessMemory"], rp.PCB.heapAddrEnd - rp.PCB.heapAddrStart);
				p.DumpProgram();
				_listings[rp.PCB.pid] = BuildListing(rp.PCB.pid, file);
			}
		}

		/// <summary>
		/// Builds an address-to-source map from the program file using the same encoding rules
		/// as Program.GetMemoryImage (1 opcode byte + 4 bytes per present parameter).
		/// This never reads guest memory.
		/// </summary>
		private static ProgramListing BuildListing(uint pid, string file)
		{
			var lines = new List<SourceLine>();
			uint address = 0;
			int lineNumber = 0;
			foreach (string raw in File.ReadAllLines(file))
			{
				lineNumber++;
				var instr = new Instruction(raw);
				int length = 1 + (instr.Param1 != uint.MaxValue ? 4 : 0) + (instr.Param2 != uint.MaxValue ? 4 : 0);
				lines.Add(new SourceLine(lineNumber, address, length, instr.OpCode.ToString(), raw.Replace('\t', ' ').Trim()));
				address += (uint)length;
			}
			return new ProgramListing(pid, Path.GetFileName(file), lines);
		}

		private static IReadOnlyDictionary<string, string> ReadSettings()
		{
			var d = new Dictionary<string, string>();
			foreach (var kv in EntryPoint.Configuration.AsEnumerable())
				if (kv.Value != null) d[kv.Key] = kv.Value;
			return d;
		}

		#region IHostObserver (engine thread)

		public void SchedulerPass()
		{
			lock (_gate)
			{
				if (_stopRequested) throw new WorkerStopRequestedException();
			}

			Process[] procs = _os.ProcessesForObservers;
			bool anyRunnable = procs.Any(p => p.PCB.state == ProcessState.NewProcess || p.PCB.state == ProcessState.Ready || p.PCB.state == ProcessState.Running);
			if (procs.Length > 0 && !anyRunnable)
			{
				if (!_blockedAnnounced)
				{
					_blockedAnnounced = true;
					Emit(EventTypes.Blocked, new BlockedPayload("No runnable process: every process is waiting. The 2002 scheduler keeps looping here."));
				}
				lock (_gate)
				{
					if (_stopRequested) throw new WorkerStopRequestedException();
					Monitor.Wait(_gate, 50);
				}
			}
			else
			{
				_blockedAnnounced = false;
			}
		}

		public void BeforeInstruction()
		{
			lock (_gate)
			{
				while (true)
				{
					if (_stopRequested) throw new WorkerStopRequestedException();

					if (_running)
					{
						if (_instructionsPerSecond <= 0) break;
						TimeSpan due = _lastInstructionStart + TimeSpan.FromSeconds(1.0 / _instructionsPerSecond);
						TimeSpan now = _clock.Elapsed;
						if (now >= due) break;
						Monitor.Wait(_gate, due - now);
						continue;
					}

					if (_stepBudget > 0)
					{
						_stepBudget--;
						break;
					}

					if (!_pauseAnnounced)
					{
						_pauseAnnounced = true;
						ReportPending(force: true);
						Emit(EventTypes.Paused, new PausedPayload(_pauseReason, Snapshot()));
					}
					Monitor.Wait(_gate);
				}
				_pauseAnnounced = false;
				_lastInstructionStart = _clock.Elapsed;
			}

			_pidBefore = _os.currentProcess.PCB.pid;
			_registersBefore = (uint[])CPU.registers.Clone();
			_sfBefore = CPU.sf;
			_zfBefore = CPU.zf;
			_swapsThisInstruction.Clear();
		}

		public void AfterInstruction()
		{
			_totalInstructions++;
			_unreportedInstructions++;

			uint ipBefore = _registersBefore[11];
			uint[] after = (uint[])CPU.registers.Clone();
			var changed = new List<int>();
			for (int r = 1; r < after.Length; r++)
				if (after[r] != _registersBefore[r]) changed.Add(r);

			SourceLine line = null;
			if (_listings.TryGetValue(_pidBefore, out var listing))
				line = listing.Lines.FirstOrDefault(l => l.Address == ipBefore);

			_lastInstruction = new InstructionInfo(
				_pidBefore, ipBefore, after[11],
				line?.OpCode ?? "(unknown)", line?.LineNumber, line?.Text,
				_registersBefore, after, changed.ToArray(), _sfBefore, _zfBefore);

			bool holdForSwap = false;
			lock (_gate)
			{
				if (_holdOnNextSwap && _swapsThisInstruction.Any(s => s.UsedSwapFile))
				{
					_holdOnNextSwap = false;
					_running = false;
					_stepBudget = 0;
					_pauseReason = "swap";
					holdForSwap = true;
				}
				else if (!_running && _stepBudget == 0)
				{
					_pauseReason = "step";
				}
			}

			bool fast;
			lock (_gate) fast = _running && _instructionsPerSecond <= 0;
			ReportPending(force: !fast || holdForSwap);
		}

		private InstructionInfo _lastInstruction;

		private void ReportPending(bool force)
		{
			if (_lastInstruction == null || _unreportedInstructions == 0) return;
			if (!force && (_clock.Elapsed - _lastReport).TotalMilliseconds < FastReportIntervalMs) return;

			Emit(EventTypes.InstructionCompleted, new InstructionCompletedPayload(_totalInstructions, _unreportedInstructions, _lastInstruction, Snapshot()));
			_unreportedInstructions = 0;
			_lastReport = _clock.Elapsed;
		}

		public void PageSwappedOut(MemoryManager.MemoryPage victim, bool wroteSwapFile)
		{
			string fileName = "page" + victim.pageNumber + "-" + victim.addrVirtual + ".xml";
			string xml = null;
			if (wroteSwapFile)
			{
				// Reads the swap file the 2002 code just wrote in our private session directory.
				try { xml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, fileName)); }
				catch (IOException) { }
			}
			RecordSwap(EventTypes.SwapOut, new SwapPayload(ToInfo(victim), wroteSwapFile, fileName, xml, _os?.currentProcess?.PCB.pid, CPU.clock));
		}

		public void PageSwappedIn(MemoryManager.MemoryPage page, bool readSwapFile)
		{
			string fileName = "page" + page.pageNumber + "-" + page.addrVirtual + ".xml";
			RecordSwap(EventTypes.SwapIn, new SwapPayload(ToInfo(page), readSwapFile, fileName, null, _os?.currentProcess?.PCB.pid, CPU.clock));
		}

		private void RecordSwap(string type, SwapPayload payload)
		{
			_swapsThisInstruction.Add(payload);
			Emit(type, payload);
		}

		#endregion

		#region Snapshots

		private MachineSnapshot Snapshot()
		{
			Process current = _os?.currentProcess;
			var pages = _os == null
				? new List<PageInfo>()
				: _os.memoryMgr.PageTableForObservers.Select(ToInfo).ToList();
			var processes = _os == null
				? new List<ProcessInfo>()
				: _os.ProcessesForObservers.Select(p => ToInfo(p, ReferenceEquals(p, current))).ToList();

			return new MachineSnapshot(
				CPU.clock,
				current?.PCB.pid,
				current != null ? (uint[])CPU.registers.Clone() : null,
				CPU.sf,
				CPU.zf,
				CPU.pageSize,
				(byte[])CPU.physicalMemory.Clone(),
				pages,
				processes);
		}

		private static PageInfo ToInfo(MemoryManager.MemoryPage p) => new PageInfo(
			p.pageNumber, p.addrVirtual, p.addrPhysical, p.pidOwner, p.addrProcessIndex,
			p.isValid, p.isDirty, p.accessCount, p.pageFaults, p.SharedMemoryRegion);

		private static ProcessInfo ToInfo(Process p, bool onCpu) => new ProcessInfo(
			p.PCB.pid,
			p.PCB.state.ToString(),
			onCpu ? (uint[])CPU.registers.Clone() : (uint[])p.PCB.registers.Clone(),
			onCpu ? CPU.sf : p.PCB.sf,
			onCpu ? CPU.zf : p.PCB.zf,
			p.PCB.priority,
			p.PCB.clockCycles,
			p.PCB.contextSwitches,
			p.PCB.sleepCounter,
			p.PCB.waitingLock,
			p.PCB.waitingEvent,
			p.PCB.codeSize,
			p.PCB.processMemorySize);

		#endregion

		#region Protocol

		private void Emit(string type, object payload)
		{
			lock (_writeLock)
			{
				if (type != EventTypes.Output)
				{
					string text = _output.Drain(includePartial: type == EventTypes.Completed || type == EventTypes.Fault);
					if (text != null) WriteEnvelope(EventTypes.Output, new OutputPayload(text));
				}
				WriteEnvelope(type, payload);
			}
		}

		private void WriteEnvelope(string type, object payload)
		{
			JsonElement? element = payload == null ? (JsonElement?)null : JsonSerializer.SerializeToElement(payload, payload.GetType(), ProtocolInfo.JsonOptions);
			var envelope = new Envelope(ProtocolInfo.Version, _sessionId, ++_sequence, type, element);
			_protocolOut.WriteLine(JsonSerializer.Serialize(envelope, ProtocolInfo.JsonOptions));
		}

		private void ReadCommands()
		{
			using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				Envelope command;
				try { command = JsonSerializer.Deserialize<Envelope>(line, ProtocolInfo.JsonOptions); }
				catch (JsonException) { Console.Error.WriteLine("Bad command: " + line); continue; }
				if (command == null || command.SessionId != _sessionId || command.ProtocolVersion != ProtocolInfo.Version)
				{
					Console.Error.WriteLine("Ignored command for another session: " + line);
					continue;
				}

				bool accepted = true;
				string message = null;
				lock (_gate)
				{
					switch (command.Type)
					{
						case CommandTypes.Run:
							_running = true;
							_stepBudget = 0;
							break;
						case CommandTypes.Pause:
							_running = false;
							_stepBudget = 0;
							_pauseReason = "user";
							break;
						case CommandTypes.Step:
							_running = false;
							_stepBudget += Math.Max(1, command.PayloadAs<StepCommand>()?.Count ?? 1);
							break;
						case CommandTypes.SetSpeed:
							_instructionsPerSecond = command.PayloadAs<SetSpeedCommand>()?.InstructionsPerSecond ?? 5;
							break;
						case CommandTypes.HoldOnNextSwap:
							_holdOnNextSwap = command.PayloadAs<HoldOnNextSwapCommand>()?.Enabled ?? false;
							break;
						case CommandTypes.Stop:
							_stopRequested = true;
							break;
						default:
							accepted = false;
							message = "Unknown command";
							break;
					}
					Monitor.PulseAll(_gate);
				}

				Emit(EventTypes.CommandAcknowledged, new CommandAcknowledgedPayload(command.Sequence, command.Type, accepted, message));
				if (command.Type == CommandTypes.Run)
					Emit(EventTypes.Running, null);
				if (command.Type == CommandTypes.Stop) break;
			}

			// Stop requested or the app went away: let the engine thread unwind, then exit.
			lock (_gate)
			{
				_stopRequested = true;
				Monitor.PulseAll(_gate);
			}
			_stopSignal.Set();
			Thread.Sleep(1500);
			Environment.Exit(0);
		}

		#endregion
	}
}
