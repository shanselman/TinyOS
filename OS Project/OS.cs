// ------------------------------------------------------------------------------
// <copyright from='2002' to='2002' company='Scott Hanselman'>
//    Copyright (c) Scott Hanselman. All Rights Reserved.   
// </copyright> 
// ------------------------------------------------------------------------------
//
// Scott Hanselman's Tiny Academic Virtual CPU and OS
// Copyright (c) 2002, Scott Hanselman (scott@hanselman.com)
// All rights reserved.
// 
// A BSD License
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
// 
// Redistributions of source code must retain the above copyright notice, 
// this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation 
// and/or other materials provided with the distribution. 
// Neither the name of Scott Hanselman nor the names of its contributors
// may be used to endorse or promote products derived from this software without
// specific prior written permission. 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR 
// PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS 
// BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; 
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE 
// OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.
//
using System;
using System.Diagnostics;
using System.Collections;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Text;
using System.Configuration;

namespace Hanselman.CST352
{
	/// <summary>
	/// The delegate (object-oriented function pointer) definition for an OS System Call. 
	/// ALl opCodes will be mapped to a function that matches this signature
	/// </summary>
	public delegate void SystemCall();

	/// <summary>
	/// The definition of an Operarting System, including a <see cref="MemoryManager"/> and a <see cref="ProcessCollection"/>
	/// </summary>
	public class OS
	{
		/// <summary>
		/// Contains the <see cref="Process"/> and the <see cref="Process.ProcessControlBlock"/> for all runningProcesses
		/// </summary>
		private ProcessCollection runningProcesses = new ProcessCollection();
		/// <summary>
		/// Holds a reference to the current running <see cref="Process"/>
		/// </summary>
		public Process currentProcess = null;

		/// <summary>
		/// A reference to the <see cref="MemoryManager"/> Class.  A <see cref="Process"/> memory accesses go 
		/// through this class.
		/// </summary>
		/// <example>
		/// theOS.memoryMgr[processId, 5]; //accesses memory at address 5
		/// </example>
		public MemoryManager memoryMgr; 

		/// <summary>
		/// There are 10 locks, numbered 1 to 10.  Lock 0 is not used.  
		/// We will store 0 when the lock is free, or the ProcessID when the lock is acquired
		/// </summary>
		public uint[] locks = new uint[11] {0,0,0,0,0,0,0,0,0,0,0};

		/// <summary>
		/// There are 10 events, numbered 1 to 10.  Event 0 is not used
		/// </summary>
		public EventState[] events = new EventState[11];

		/// <summary>
		/// An event is either Signaled or NonSignaled
		/// </summary>
		public enum EventState { 
			/// <summary>
			/// Events are by default NonSignaled
			/// </summary>
			NonSignaled = 0, 

			/// <summary>
			/// Events become Signaled, and Processes that are waiting on them wake up when Signaled
			/// </summary>
			Signaled = 1 };

		/// <summary>
		/// This counter is incremented as new processes are created.  
		/// It provides a unique id for a process. Process Id 0 is assumed to be the OS.
		/// </summary>
		public static uint processIdPool = 0;

		/// <summary>
		/// Do we output debug for Instructions?
		/// </summary>
		private bool bDumpInstructions = false; 

		/// <summary>
		/// Public constructor for the OS
		/// </summary>
		/// <param name="virtualMemoryBytes">The number of "addressable" bytes of memory for the whole OS.</param>
		public OS(uint virtualMemoryBytes)
		{
			memoryMgr = new MemoryManager(virtualMemoryBytes);
			bDumpInstructions = bool.Parse(ConfigurationManager.AppSettings["DumpInstruction"]);
		}

		/// <summary>
		/// Checks if the <see cref="currentProcess"/> is eligible to run
		/// </summary>
		/// <returns>true if the <see cref="currentProcess"/> is eligible to run</returns>
		public bool currentProcessIsEligible()
		{
			if (currentProcess == null) return false;

			if (currentProcess.PCB.state == ProcessState.Terminated 
				|| currentProcess.PCB.state == ProcessState.WaitingOnLock 
				|| currentProcess.PCB.state == ProcessState.WaitingAsleep 
				|| currentProcess.PCB.state == ProcessState.WaitingOnEvent)
				return false;
			return true;
		}

		/// <summary>
		/// Dumps collected statistics of a process when it's been removed from the <see cref="runningProcesses"/> table
		/// </summary>
		/// <param name="processIndex">The Index (not the ProcessID!) in the <see cref="runningProcesses"/> table of a Process</param>
		public void DumpProcessStatistics(int processIndex)
		{
			Process p = runningProcesses[processIndex];

			Console.WriteLine("Removed Exited Process # {0}",p.PCB.pid);
			Console.WriteLine("  # of Page Faults:      {0}",memoryMgr.PageFaultsForProcess(p));
			Console.WriteLine("  # of Clock Cycles:     {0}",p.PCB.clockCycles);
			Console.WriteLine("  # of Context Switches: {0}",p.PCB.contextSwitches);
		}

		/// <summary>
		/// The primary control loop for the whole OS.  
		/// Spins through eligible processes and executes their opCodes
		/// Provides scheduling and removes old processes.
		/// </summary>
		public void execute()
		{
			while(true)
			{
				//
				// Yank terminated processes
				//
				for (int i = runningProcesses.Count-1; i >= 0; i--)
				{
					if (runningProcesses[i].PCB.state == ProcessState.Terminated)
					{
						DumpProcessStatistics(i);
						memoryMgr.ReleaseMemoryOfProcess(runningProcesses[i].PCB.pid);	
						runningProcesses[i].PCB.heapPageTable.Clear();
						ReleaseLocksOfProccess(runningProcesses[i].PCB.pid);
						runningProcesses.RemoveAt(i);
						CPU.DumpPhysicalMemory();						
					}
				}

				// Sort high priority first + least used clock cycles first to avoid starvation
				// see Process.Compare
				// 
				runningProcesses.Sort(); 

				if (runningProcesses.Count == 0) 
				{
					Console.WriteLine("No Processes");
					if (bool.Parse(ConfigurationManager.AppSettings["PauseOnExit"]) == true)System.Console.ReadLine();
					System.Environment.Exit(0);
				}
				else
				{
					foreach (Process p in runningProcesses)
					{
						switch (p.PCB.state)
						{
							case ProcessState.Terminated:
								//yank old processes outside the foreach
								break;
							case ProcessState.WaitingAsleep:
								//is this process waiting for an event?
								break;
							case ProcessState.WaitingOnLock:
								//is this process waiting for an event?
								break;
							case ProcessState.WaitingOnEvent:
								//is this process waiting for an event?
								break;
							case ProcessState.NewProcess:
							case ProcessState.Ready:
								currentProcess = p;

								//copy state from PCB to CPU
								LoadCPUState();

								DumpContextSwitchIn();

								// Reset this flag. If we need to interrupt execution 
								// because a lock has been made available
								// or an Event has signaled, we can preempt the current process
								bool bPreemptCurrentProcess = false;

								while (currentProcessIsEligible())
								{
									currentProcess.PCB.state = ProcessState.Running;
									
									//CPU.DumpPhysicalMemory();
									//CPU.DumpRegisters();

									try
									{
										CPU.executeNextOpCode();
										currentProcess.PCB.clockCycles++;
									}
									catch(MemoryException e)
									{
										Console.WriteLine(e.ToString());
										CPU.DumpRegisters();
										currentProcess.PCB.state = ProcessState.Terminated;
									}
									catch(StackException e)
									{
										Console.WriteLine(e.ToString());
										CPU.DumpRegisters();
										currentProcess.PCB.state = ProcessState.Terminated;
									}
									catch(HeapException e)
									{
										Console.WriteLine(e.ToString());
										CPU.DumpRegisters();
										currentProcess.PCB.state = ProcessState.Terminated;
									}
								
									CPU.DumpPhysicalMemory();
									CPU.DumpRegisters();

									//
									// Update any sleeping processes
									//
									foreach (Process sleepingProcess in runningProcesses)
									{
										switch(sleepingProcess.PCB.state)
										{
											case ProcessState.WaitingAsleep:
												// a sleepCounter of 0 sleeps forever if we are waiting
												if (sleepingProcess.PCB.sleepCounter != 0)
													//If we JUST reached 0, wake up!
													if (--sleepingProcess.PCB.sleepCounter == 0)
													{
														sleepingProcess.PCB.state = ProcessState.Ready;
														bPreemptCurrentProcess = true;
													}
												break;
											case ProcessState.WaitingOnEvent:
												// Are we waiting for an event?  We'd better be!
												Debug.Assert(sleepingProcess.PCB.waitingEvent != 0);
												
												// Had the event been signalled recently?
												if (this.events[sleepingProcess.PCB.waitingEvent] == EventState.Signaled)
												{
													this.events[sleepingProcess.PCB.waitingEvent] = EventState.NonSignaled;
													sleepingProcess.PCB.state = ProcessState.Ready;
													sleepingProcess.PCB.waitingEvent = 0;
													bPreemptCurrentProcess = true;
												}
												break;
											case ProcessState.WaitingOnLock:
												// We are are in the WaitingOnLock state, we can't wait on the "0" lock
												Debug.Assert(sleepingProcess.PCB.waitingLock != 0);

												// Has the lock be released recently?
												if (this.locks[sleepingProcess.PCB.waitingLock] == 0)
												{
													// Acquire the Lock and wake up!
													this.locks[sleepingProcess.PCB.waitingLock] = sleepingProcess.PCB.waitingLock;
													sleepingProcess.PCB.state = ProcessState.Ready;													bPreemptCurrentProcess = true;
													sleepingProcess.PCB.waitingLock = 0;
													bPreemptCurrentProcess = true;
												}
												break;
										}
									}

									// Have we used up our slice of time?
									bool bEligible = currentProcess.PCB.clockCycles == 0 || (currentProcess.PCB.clockCycles % currentProcess.PCB.timeQuantum != 0);
									if (!bEligible)	
										break;
									if (bPreemptCurrentProcess)		
										break;
								}
								if (currentProcess.PCB.state != ProcessState.Terminated)
								{
									//copy state from CPU to PCB
									if (currentProcess.PCB.state != ProcessState.WaitingAsleep 
										&& currentProcess.PCB.state != ProcessState.WaitingOnLock
										&& currentProcess.PCB.state != ProcessState.WaitingOnEvent)
										currentProcess.PCB.state = ProcessState.Ready;
									currentProcess.PCB.contextSwitches++;

									DumpContextSwitchOut();

									SaveCPUState();
									
									//Clear registers for testing
									CPU.registers = new uint[12];
								}
								currentProcess = null;
								break;
						}
					}
				}
			}
		}

		/// <summary>
		/// If the DumpContextSwitch Configuration option is set to True, reports the Context Switch.  
		/// Used for debugging
		/// </summary>
		public void DumpContextSwitchIn()
		{
			if (bool.Parse(ConfigurationManager.AppSettings["DumpContextSwitch"]) == false)
				return;
			Console.WriteLine("Switching in Process {0} with ip at {1}",currentProcess.PCB.pid,currentProcess.PCB.ip);
		}

		/// <summary>
		/// If the DumpContextSwitch Configuration option is set to True, reports the Context Switch.  
		/// Used for debugging
		/// </summary>
		public void DumpContextSwitchOut()
		{
			if (bool.Parse(ConfigurationManager.AppSettings["DumpContextSwitch"]) == false)
				return;
			Console.WriteLine("Switching out Process {0} with ip at {1}",currentProcess.PCB.pid,CPU.ip);
		}

		/// <summary>
		/// Outputs a view of memory from the Process's point of view
		/// </summary>
		/// <param name="p">The Process to Dump</param>
		public void DumpProcessMemory(Process p)
		{
			int address = 0; byte b;
			for (uint i = 0; i < p.PCB.processMemorySize; i++)
			{
				b = this.memoryMgr[p.PCB.pid,i];
				if (address == 0 || address%16==0)
					Console.Write(System.Environment.NewLine + "{0,-4:000} ", address);
				address++;
				if (b == 0)
					Console.Write("{0,3}","-");
				else
					Console.Write("{0,3}",(int)b);
				if (address%4==0 && address%16!=0) Console.Write("  :");
			}
			Console.WriteLine();
		}

		/// <summary>
		/// Called on a context switch. Copy the CPU's <see cref="CPU.registers"/> to the <see cref="currentProcess"/>'s <see cref="CPU.registers"/>
		/// </summary>
		private void SaveCPUState()
		{
			CPU.registers.CopyTo(currentProcess.PCB.registers,0);
			currentProcess.PCB.zf = CPU.zf;
			currentProcess.PCB.sf = CPU.sf;
			currentProcess.PCB.ip = CPU.ip;
		}

		/// <summary>
		/// Called on a context switch. Copy the <see cref="currentProcess"/>'s <see cref="Process.ProcessControlBlock.registers"/> to the CPU's <see cref="CPU.registers"/> 
		/// </summary>
		private void LoadCPUState()
		{
			currentProcess.PCB.registers.CopyTo(CPU.registers,0);
			CPU.zf = currentProcess.PCB.zf;
			CPU.sf = currentProcess.PCB.sf;
			CPU.ip = currentProcess.PCB.ip;
		}

		/// <summary>
		/// Take as a <see cref="Program"/> and creates a Process object, adding it to the <see cref="runningProcesses"/>
		/// </summary>
		/// <param name="prog">Program to load</param>
		/// <param name="memorySize">Size of memory in bytes to assign to this Process</param>
		/// <returns>The newly created Process</returns>
		public Process createProcess(Program prog, uint memorySize)
		{
			// Get an array represting the code block
			byte[] processCode = prog.GetMemoryImage();

			// Create a process with a unique id and fixed memory size
			Process p = new Process(++processIdPool, memorySize);
			
			// Map memory to the Process (if available, otherwise freak out)
			this.memoryMgr.MapMemoryToProcess(p.PCB.processMemorySize,p.PCB.pid);
			
			// Set the initial IP to 0 (that's where exectution will begin)
			p.PCB.ip = 0; 

			//
			// SETUP CODE SECTION
			//
			// Copy the code in one byte at a time
			uint index = 0;
			foreach (byte b in processCode)
				memoryMgr[p.PCB.pid, index++] = b;

			//
			// SETUP STACK SECTION
			//
			// Set stack pointer at the end of memory
			//
			p.PCB.sp = memorySize-1;
			p.PCB.stackSize = uint.Parse(ConfigurationManager.AppSettings["StackSize"]);

			//
			// SETUP CODE SECTION
			//
			// Set the length of the Code section
			//
			uint roundedCodeLength = CPU.UtilRoundToBoundary((uint)processCode.Length, CPU.pageSize);
			//uint roundedCodeLength = (uint)(CPU.pageSize * ((processCode.Length / CPU.pageSize) + ((processCode.Length % CPU.pageSize > 0) ? 1: 0)));
			p.PCB.codeSize = roundedCodeLength;

			//
			// SETUP DATA SECTION
			//
			// Point Global Data just after the Code for now...
			//
			p.PCB.registers[9] = (uint)roundedCodeLength; 
			p.PCB.dataSize = uint.Parse(ConfigurationManager.AppSettings["DataSize"]);

			//
			// SETUP HEAP SECTION
			//
			p.PCB.heapAddrStart = p.PCB.codeSize + p.PCB.dataSize;
			p.PCB.heapAddrEnd = p.PCB.processMemorySize - p.PCB.stackSize;
		

			this.memoryMgr.CreateHeapTableForProcess(p);

			// Add ourselves to the runningProcesses table
			runningProcesses.Add(p);
			return p;
		}
		
		/// <summary>
		/// Releases any locks held by this process.  
		/// This function is called when the process exits.
		/// </summary>
		/// <param name="pid">Process ID</param>
		public void ReleaseLocksOfProccess(uint pid)
		{	
			for (int i = 0; i < this.locks.Length; i++)
				if (this.locks[i] == pid) 
					this.locks[i] = 0;
		}


		/// <summary>
		/// Utility function to fetch a 4 byte unsigned int from Process Memory based on the current <see cref="CPU.ip"/>
		/// </summary>
		/// <returns>a new uint</returns>
		public unsafe uint FetchUIntAndMove()
		{
			uint retVal = memoryMgr.getUIntFrom(currentProcess.PCB.pid,CPU.ip);
			CPU.ip += sizeof(uint);
			return retVal;
		}

		/// <summary>
		/// Increments register
		/// <pre>1 r1</pre>
		/// </summary>
		public void Incr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];	
			Debug.Assert(InstructionType.Incr == instruction);
			
			//move to the param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			//increment the register pointed to by this memory
			CPU.registers[register]++;
		}

		/// <summary>
		///  Adds constant 1 to register 1
		/// <pre>
		/// 2 r1, $1
		/// </pre>
		/// </summary>
		public void Addi()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Addi == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();
			uint param1 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} {3}",currentProcess.PCB.pid, instruction, register, param1);

			//increment the register pointed to by this memory by the const next to it
			CPU.registers[register]+= param1;
		}

		/// <summary>
		/// Adds r2 to r1 and stores the value in r1
		/// <pre>
		/// 3 r1, r2
		/// </pre>
		/// </summary>
		public void Addr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Addr == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
			uint param1 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} {3}",currentProcess.PCB.pid, instruction, register, param1);

			//add 1st register and 2nd register and put the result in 1st register
			CPU.registers[register] = CPU.registers[register] + CPU.registers[param1];
		}

		/// <summary>
		/// Compare contents of r1 with 1.  If r1 &lt; 9 set sign flag.  If r1 &gt; 9 clear sign flag.
		/// If r1 == 9 set zero flag.
		/// <pre>
		/// 14 r1, $9
		/// </pre>
		/// </summary>
		public void Cmpi()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Cmpi == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
			uint param1 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} {3}",currentProcess.PCB.pid, instruction, register, param1);

			//compare register and const
			CPU.zf = false;
			if (CPU.registers[register] < param1) 
				CPU.sf = true;
			if (CPU.registers[register] > param1) 
				CPU.sf = false;
			if (CPU.registers[register] == param1)
				CPU.zf = true;
		}

		/// <summary>
		/// Compare contents of r1 with r2.  If r1 &lt; r2 set sign flag.  If r1 &gt; r2 clear sign flag.
		/// If r1 == r2 set zero flag.
		/// <pre>
		/// 15 r1, r2
		/// </pre>
		/// </summary>
		public void Cmpr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Cmpr == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//compare register and const
			CPU.zf = false;
			if (CPU.registers[register1] < CPU.registers[register2]) 
				CPU.sf = true;
			if (CPU.registers[register1] > CPU.registers[register2]) 
				CPU.sf = false;
			if (CPU.registers[register1] == CPU.registers[register2])
				CPU.zf = true;
		}

		/// <summary>
		/// Call the procedure at offset r1 bytes from the current instrucion.  
		/// The address of the next instruction to excetute after a return is pushed on the stack
		/// <pre>
		/// 19 r1
		/// </pre>		
		/// </summary>
		public void Call()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Call == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			StackPush(currentProcess.PCB.pid, CPU.ip);

			CPU.ip+= CPU.registers[register];
		}

		/// <summary>
		/// Call the procedure at offset of the bytes in memory pointed by r1 from the current instrucion.  
		/// The address of the next instruction to excetute after a return is pushed on the stack
		/// <pre>
		/// 20 r1
		/// </pre>		
		/// </summary>
		public void Callm()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Callm == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			StackPush(currentProcess.PCB.pid, CPU.ip);

			CPU.ip+= this.memoryMgr[currentProcess.PCB.pid,CPU.registers[register]];
		}

		/// <summary>
		/// Pop the return address from the stack and transfer control to this instruction
		/// <pre>
		/// 21
		/// </pre>		
		/// </summary>
		public void Ret()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Ret == instruction);
			
			CPU.ip++;
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1}",currentProcess.PCB.pid, instruction);

			CPU.ip = StackPop(currentProcess.PCB.pid);
		}



		/// <summary>
		/// Control transfers to the instruction whose address is r1 bytes relative to the current instruction. 
		/// r1 may be negative.
		/// <pre>
		/// 13 r1
		/// </pre>
		/// </summary>
		public void Jmp()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Jmp == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			int instructionsToSkip = (int)CPU.registers[register];
			// Do some sillyness to substract if we are a negative number
			if (Math.Sign(instructionsToSkip) == -1)
				CPU.ip-= (uint)Math.Abs(instructionsToSkip);
			else
				CPU.ip+= (uint)instructionsToSkip;
		}

		/// <summary>
		/// If the sign flag is set, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 16 r1
		/// </pre>		
		/// </summary>
		public void Jlt()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Jlt == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			if (CPU.sf == true)
			{
				CPU.ip+= CPU.registers[register];
			}
		}

		/// <summary>
		/// If the sign flag is clear, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 17 r1
		/// </pre>		
		/// </summary>
		public void Jgt()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Jgt == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			if (CPU.sf == false)
			{
				CPU.ip+= CPU.registers[register];
			}
		}

		/// <summary>
		/// If the zero flag is set, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 18 r1
		/// </pre>		
		/// </summary>
		public void Je()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Je == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register = FetchUIntAndMove();
		
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			if (CPU.zf == true)
			{
				CPU.ip+= CPU.registers[register];
			}
		}


		/// <summary>
		/// Just that, does nothing
		/// </summary>
		public void Noop()
		{
			;
		}

		/// <summary>
		/// This opcode causes an exit and the process's memory to be unloaded.  
		/// Another process or the idle process must now be scheduled
		/// <pre>
		/// 27
		/// </pre>		
		/// </summary>
		public void Exit()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Exit == instruction);
				
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1}",currentProcess.PCB.pid, instruction);

			currentProcess.PCB.state = ProcessState.Terminated;
		}

		/// <summary>
		/// Moves constant 1 into register 1
		/// <pre>
		/// 6 r1, $1
		/// </pre>
		/// </summary>
		public void Movi()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Movi == instruction);
			
			CPU.ip++;
			uint register = FetchUIntAndMove();
			uint param2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} {3}",currentProcess.PCB.pid, instruction, register, param2);

			//move VALUE of param into 1st register 
			CPU.registers[register] = param2;
		}

		/// <summary>
		/// Moves contents of register2 into register 1
		/// <pre>
		/// 7 r1, r2
		/// </pre>
		/// </summary>
		public void Movr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Movr == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//move VALUE of 2nd register into 1st register 
			CPU.registers[register1] = CPU.registers[register2];
		}

		/// <summary>
		/// Moves contents of memory pointed to register 2 into register 1
		/// <pre>
		/// 8 r1, r2
		/// </pre>
		/// </summary>
		public void Movmr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Movmr == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();
			
			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//move VALUE of memory pointed to by 2nd register into 1st register 
			CPU.registers[register1] = memoryMgr.getUIntFrom(currentProcess.PCB.pid,CPU.registers[register2]);
		}

		/// <summary>
		/// Moves contents of register 2 into memory pointed to by register 1
		/// <pre>
		/// 9 r1, r2
		/// </pre>
		/// </summary>
		public void Movrm()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Movrm == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//set memory pointed to by register 1 to contents of register2
			memoryMgr.setUIntAt(currentProcess.PCB.pid,CPU.registers[register1],CPU.registers[register2]);
		}

		/// <summary>
		/// Moves contents of memory pointed to by register 2 into memory pointed to by register 1
		/// <pre>
		/// 10 r1, r2
		/// </pre>
		/// </summary>
		public void Movmm()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Movmm == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//set memory point to by register 1 to contents of memory pointed to by register 2
			memoryMgr.setUIntAt(currentProcess.PCB.pid,CPU.registers[register1],memoryMgr.getUIntFrom(currentProcess.PCB.pid,CPU.registers[register2]));
		}

		/// <summary>
		/// Prints out contents of register 1
		/// <pre>
		/// 11 r1
		/// </pre>
		/// </summary>
		public void Printr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Printr == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			Console.WriteLine(CPU.registers[register]);
		}

		/// <summary>
		/// Prints out contents of memory pointed to by register 1
		/// <pre>
		/// 12 r1
		/// </pre>
		/// </summary>
		public void Printm()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Printm == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			Console.WriteLine(memoryMgr[currentProcess.PCB.pid, CPU.registers[register]]);
		}

		/// <summary>
		/// Read the next 32-bit value into register r1
		/// <pre>
		/// 32 r1
		/// </pre>		
		/// </summary>
		public void Input()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Input == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			CPU.registers[register] = uint.Parse(Console.ReadLine());
		}

		/// <summary>
		/// Sleep the # of clock cycles as indicated in r1.  
		/// Another process or the idle process 
		/// must be scheduled at this point.  
		/// If the time to sleep is 0, the process sleeps infinitely
		/// <pre>
		/// 25 r1
		/// </pre>		
		/// </summary>
		public void Sleep()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Sleep == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			//Set the number of clockCycles to sleep
			currentProcess.PCB.sleepCounter = CPU.registers[register];
			currentProcess.PCB.state = ProcessState.WaitingAsleep;
		}

		/// <summary>
		/// Set the priority of the current process to the value
		/// in register r1
		/// <pre>
		/// 26 r1
		/// </pre>		
		/// </summary>
		public void SetPriority()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.SetPriority== instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			currentProcess.PCB.priority = (int)Math.Min(CPU.registers[register],(int)ProcessPriority.MaxPriority);
		}

		/// <summary>
		/// Pushes contents of register 1 onto stack
		/// <pre>
		/// 4 r1
		/// </pre>
		/// </summary>
		public void Pushr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Pushr == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			StackPush(currentProcess.PCB.pid, CPU.registers[register]);
		}

		/// <summary>
		/// Pushes constant 1 onto stack
		/// <pre>
		/// 5 $1
		/// </pre>
		/// </summary>
		public void Pushi()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Pushi == instruction);
			
			//move to the param containing the 1st param
			CPU.ip++;
			uint param = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} {2}",currentProcess.PCB.pid, instruction, param);

			StackPush(currentProcess.PCB.pid, param);
		}

		/// <summary>
		/// Terminate the process whose id is in the register r1
		/// <pre>
		/// 34 r1
		/// </pre>		
		/// </summary>
		public void TerminateProcess()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.TerminateProcess == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			foreach (Process p in this.runningProcesses)
			{
				if (p.PCB.pid == CPU.registers[register])
				{
					p.PCB.state = ProcessState.Terminated;
					Console.WriteLine("Process {0} has forceably terminated Process {1}",currentProcess.PCB.pid, p.PCB.pid);
					break;
				}
			}
		}

		/// <summary>
		/// Pop the contents at the top of the stack into register r1 
		/// <pre>
		/// 35 r1
		/// </pre>		
		/// </summary>
		public void Popr()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Popr == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			CPU.registers[register] = StackPop(currentProcess.PCB.pid);
		}

		/// <summary>
		/// set the bytes starting at address r1 of length r2 to zero
		/// <pre>
		/// 33 r1, r2
		/// </pre>		
		/// </summary>
		public void MemoryClear()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.MemoryClear == instruction);
			
			//move to the param containing the 1st register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			//move VALUE of memory pointed to by 2nd register into 1st register 
			this.memoryMgr.SetMemoryOfProcess(currentProcess.PCB.pid,CPU.registers[register1],CPU.registers[register2],0);
		}

		/// <summary>
		/// Pop the contents at the top of the stack into the memory pointed to by register r1 
		/// <pre>
		/// 36 r1
		/// </pre>		
		/// </summary>
		public void Popm()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Popm == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			memoryMgr.setUIntAt(currentProcess.PCB.pid,CPU.registers[register],StackPop(currentProcess.PCB.pid));
		}

		/// <summary>
		/// Acquire the OS lock whose # is provided in register r1.  
		/// Icf the lock is not held by the current process
		/// the operation is a no-op
		/// <pre>
		/// 23 r1
		/// </pre>		
		/// </summary>
		public void AcquireLock()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.AcquireLock == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			//Are we the first ones here? with a valid lock?
			if (CPU.registers[register] > 0 && CPU.registers[register] <= 10)
			{
				if (this.locks[CPU.registers[register]] == 0)
				{
					//Set the lock specified in the register as locked...
					this.locks[CPU.registers[register]] = currentProcess.PCB.pid;
				}
				else if (this.locks[CPU.registers[register]] == currentProcess.PCB.pid)
				{
					//No-Op, we already have this lock
					; 
				}
				else
				{
					//Get in line for this lock
					currentProcess.PCB.waitingLock = CPU.registers[register];
					currentProcess.PCB.state = ProcessState.WaitingOnLock;
				}
			}
		}

		/// <summary>
		/// Release the OS lock whose # is provided in register r1.  
		/// Another process or the idle process 
		/// must be scheduled at this point.  
		/// if the lock is not held by the current process, 
		/// the instruction is a no-op
		/// <pre>
		/// 24 r1
		/// </pre>		
		/// </summary>
		public void ReleaseLock()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.ReleaseLock == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);

			//Release only if we already have this lock, and it's a valid lock
			if (CPU.registers[register] > 0 && CPU.registers[register] <= 10)
			{
				if (this.locks[CPU.registers[register]] == currentProcess.PCB.pid)
				{
					//set the lock back to 0 (the OS)
					this.locks[CPU.registers[register]] = 0;
				}
			}
		}

		/// <summary>
		/// Signal the event indicated by the value in register r1
		/// <pre>
		/// 30 r1
		/// </pre>		
		/// </summary>
		public void SignalEvent()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.SignalEvent == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);
			
			if (CPU.registers[register] > 0 && CPU.registers[register] <= 10)
			{
				this.events[CPU.registers[register]] = EventState.Signaled;
			}
		}

		/// <summary>
		/// Wait for the event in register r1 to be triggered resulting in a context-switch
		/// <pre>
		/// 31 r1
		/// </pre>		
		/// </summary>
		public void WaitEvent()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.WaitEvent == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register);
			
			if (CPU.registers[register] > 0 && CPU.registers[register] <= 10)
			{
				currentProcess.PCB.waitingEvent = CPU.registers[register];
				currentProcess.PCB.state = ProcessState.WaitingOnEvent;
			}
		}

		/// <summary>
		/// Map the shared memory region identified by r1 and return the start address in r2
		/// <pre>
		/// 29 r1, r2
		/// </pre>		
		/// </summary>
		public void MapSharedMem()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.MapSharedMem == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register1 = FetchUIntAndMove();
			uint register2 = FetchUIntAndMove();

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);
			
			CPU.registers[register2] = this.memoryMgr.MapSharedMemoryToProcess(CPU.registers[register1], currentProcess.PCB.pid);
		}

		
		/// <summary>
		/// Allocate memory of the size equal to r1 bytes and return the address of the new memory in r2.  
		/// If failed, r2 is cleared to 0.
		/// <pre>
		/// 22 r1, r2
		/// </pre>		
		/// </summary>
		public void Alloc()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.Alloc == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register1 = FetchUIntAndMove(); //bytes requested
			uint register2 = FetchUIntAndMove(); //address returned

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2} r{3}",currentProcess.PCB.pid, instruction, register1, register2);

			uint addr = this.memoryMgr.ProcessHeapAlloc(currentProcess, CPU.registers[register1]);

			CPU.registers[register2] = addr;
		}

		/// <summary>
		/// Free the memory allocated whose address is in r1
		/// <pre>
		/// 28 r1
		/// </pre>		
		/// </summary>
		public void FreeMemory()
		{
			//get the instruction and make sure we should be here
			InstructionType instruction = (InstructionType)memoryMgr[currentProcess.PCB.pid,CPU.ip];
			Debug.Assert(InstructionType.FreeMemory == instruction);
			
			//move to the param containing the register
			CPU.ip++;
			uint register1 = FetchUIntAndMove(); //address of memory

			if (bDumpInstructions) Console.WriteLine(" Pid:{0} {1} r{2}",currentProcess.PCB.pid, instruction, register1);

			this.memoryMgr.ProcessHeapFree(currentProcess, CPU.registers[register1]);
		}


		/// <summary>
		/// Push a uint on the stack for this Process
		/// </summary>
		/// <param name="processid">The Process Id</param>
		/// <param name="avalue">The uint for the stack</param>
		public void StackPush(uint processid, uint avalue)
		{

			CPU.sp -= 4;

			//Are we blowing the stack?
			if (CPU.sp < (currentProcess.PCB.processMemorySize - 1 - currentProcess.PCB.stackSize))
				throw (new StackException(currentProcess.PCB.pid, currentProcess.PCB.processMemorySize - 1 - currentProcess.PCB.stackSize - CPU.sp));
	
			this.memoryMgr.setUIntAt(processid,CPU.sp,avalue);
		}

		/// <summary>
		/// Pop a uint off the stack for this Process
		/// </summary>
		/// <param name="processid">The Process ID</param>
		/// <returns>the uint from the stack</returns>
		public uint StackPop(uint processid)
		{
			uint retVal = this.memoryMgr.getUIntFrom(processid,CPU.sp);
			this.memoryMgr.SetMemoryOfProcess(processid,CPU.sp,4,0);
			CPU.sp += 4;
			return retVal;
		}
	}
}

