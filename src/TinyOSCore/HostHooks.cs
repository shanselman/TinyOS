// ------------------------------------------------------------------------------
// TinyOS Time Machine (2026) host observation hooks.
//
// This file is NOT part of the 2002 assignment. It adds optional, read-only
// observation points so a separate worker process can watch the original
// scheduler, CPU, and memory manager without changing what they do.
//
// The console build never sets HostHooks.Observer, so every hook below is a
// null check and the historical behavior is unchanged.
// ------------------------------------------------------------------------------
namespace Hanselman.CST352
{
	/// <summary>
	/// Implemented by a host (the Time Machine worker) that wants to observe execution.
	/// Implementations must never read guest memory through <see cref="MemoryManager"/>'s
	/// indexer, because that updates paging metadata and can trigger swapping.
	/// </summary>
	public interface IHostObserver
	{
		/// <summary>Called at the top of every scheduler pass in <see cref="OS.execute"/>.</summary>
		void SchedulerPass();

		/// <summary>Called immediately before the scheduler attempts the next instruction. May block (pause/step gate).</summary>
		void BeforeInstruction();

		/// <summary>Called after an instruction attempt and the scheduler's sleeping/waiting bookkeeping, before the time-slice check.</summary>
		void AfterInstruction();

		/// <summary>Called at the end of <see cref="MemoryManager.SwapOut"/>.</summary>
		void PageSwappedOut(MemoryManager.MemoryPage victim, bool wroteSwapFile);

		/// <summary>Called at the end of <see cref="MemoryManager.SwapIn"/>.</summary>
		void PageSwappedIn(MemoryManager.MemoryPage page, bool readSwapFile);
	}

	/// <summary>
	/// Holds the optional host observer. Null (and therefore inert) in the console build.
	/// </summary>
	public static class HostHooks
	{
		/// <summary>The current observer, or null when running as the original console OS.</summary>
		public static IHostObserver Observer;
	}
}
