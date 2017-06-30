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
using System.Collections;
using System.Configuration;
using System.Diagnostics;

namespace Hanselman.CST352
{
	/// <summary>
	/// CPU is never instanciated, but is "always" there...like a real CPU. :)  It holds <see cref="physicalMemory"/> 
	/// and the <see cref="registers"/>.  It also provides a mapping from <see cref="Instruction"/>s to SystemCalls in 
	/// the <see cref="OS"/>.  
	/// </summary>
	public abstract class CPU
	{
		/// <summary>
		/// The size of a memory page for this system.  This should be a multiple of 4.  Small sizes (like 4) will
		/// cause the system to thrash and page often.  16 is a nice compromise for such a small system.  
		/// 64 might also work well.  This probably won't change, but it is nice to be able to.  
		/// This is loaded from Configuration on a call to <see cref="initPhysicalMemory"/>
		/// </summary>
		public static uint pageSize = 0; 

		/// <summary>
		/// The clock for the system.  This increments as we execute each <see cref="Instruction"/>.
		/// </summary>
		public static uint clock = 0;

		/// <summary>
		/// The CPU's reference to the <see cref="OS"/>.  This is set by the <see cref="EntryPoint"/>.
		/// </summary>
		public static OS theOS = null;

		/// <summary>
		/// Initialized our <see cref="physicalMemory"/> array that represents physical memory.  Should only be called once.
		/// </summary>
		/// <param name="memorySize">The size of physical memory</param>
		public static void initPhysicalMemory(uint memorySize)
		{
			pageSize = uint.Parse(EntryPoint.Configuration["MemoryPageSize"]);

			uint newMemorySize = UtilRoundToBoundary(memorySize, CPU.pageSize);

			// Initalize Physical Memory
			physicalMemory = new byte[newMemorySize];
			
			if (newMemorySize != memorySize) 
				Console.WriteLine("CPU: Memory was expanded from {0} bytes to {1} bytes to a page boundary." + System.Environment.NewLine,memorySize, newMemorySize);
		}

		/// <summary>
		/// Here is the actual array of bytes that contains the physical memory for this CPU.
		/// </summary>
		internal static byte[] physicalMemory;

		/// <summary>
		/// We have 10 registers.  R11 is the <see cref="ip"/>, and we don't use R0.  R10 is the <see cref="sp"/>.  So, that's 1 to 10, and 11.
		/// </summary>
		internal static uint[] registers = new uint[12]; //0 to 11

		/// <summary>
		/// We have a Sign Flag and a Zero Flag in a <see cref="BitArray"/>
		/// </summary>
		private static BitArray bitFlagRegisters = new BitArray(2,false);

		#region Public Accessors
		/// <summary>
		/// Public get/set accessor for the Sign Flag
		/// </summary>
		public static bool sf 
		{
			get { return bitFlagRegisters[0]; }
			set { bitFlagRegisters[0] = value; }
		}

		/// <summary>
		/// Public get/set accessor for the Zero Flag
		/// </summary>
		public static bool zf 
		{
			get { return bitFlagRegisters[1]; }
			set	{ bitFlagRegisters[1] = value; }
		}

		/// <summary>
		/// Public get/set accessor for Stack Pointer
		/// </summary>
		public static uint sp 
		{
			get	{ return CPU.registers[10]; }
			set	{ CPU.registers[10] = value; }
		}

		/// <summary>
		/// Public get/set access for the CPU's Instruction Pointer
		/// </summary>
		public static uint ip
		{
			get { return CPU.registers[11];	}
			set	{ CPU.registers[11] = value; }
		}
		#endregion


		/// <summary>
		/// Takes the process id from the <see cref="OS.currentProcess"/> and the CPU's <see cref="ip"/> and 
		/// gets the next <see cref="Instruction"/> from memory.  The <see cref="InstructionType"/> translates 
		/// via an array of <see cref="SystemCall"/>s and retrives a <see cref="Delegate"/> from <see cref="opCodeToSysCall"/>
		/// and calls it.
		/// </summary>
		public static void executeNextOpCode()
		{
			// The opCode still is pointed to by CPU.ip, but the memory access is protected
			opCodeToSysCall((InstructionType)theOS.memoryMgr[theOS.currentProcess.PCB.pid,CPU.ip]);
			CPU.clock++;
		}
		
		/// <summary>
		/// The <see cref="InstructionType"/> translates via an array of <see cref="SystemCall"/>s and 
		/// retrives a <see cref="Delegate"/> and calls it.
		/// </summary>
		/// <param name="opCode">An <see cref="InstructionType"/> enum that maps to a <see cref="SystemCall"/></param>
		public static void opCodeToSysCall(InstructionType opCode)
		{
			#region System Calls Map
			SystemCall[] sysCalls = 
			{ 
				new SystemCall(theOS.Noop), //0

				new SystemCall(theOS.Incr), //1
				new SystemCall(theOS.Addi), //2
				new SystemCall(theOS.Addr), //3
				new SystemCall(theOS.Pushr), //4
				new SystemCall(theOS.Pushi), //5

				new SystemCall(theOS.Movi), //6
				new SystemCall(theOS.Movr), //7
				new SystemCall(theOS.Movmr), //8
				new SystemCall(theOS.Movrm), //9
				new SystemCall(theOS.Movmm), //10

				new SystemCall(theOS.Printr), //11
				new SystemCall(theOS.Printm), //12
				new SystemCall(theOS.Jmp), //13
				new SystemCall(theOS.Cmpi), //14
				new SystemCall(theOS.Cmpr), //15

				new SystemCall(theOS.Jlt), //16
				new SystemCall(theOS.Jgt), //17
				new SystemCall(theOS.Je), //18
				new SystemCall(theOS.Call), //19
				new SystemCall(theOS.Callm), //20

				new SystemCall(theOS.Ret), //21
				new SystemCall(theOS.Alloc), //22
				new SystemCall(theOS.AcquireLock), //23
				new SystemCall(theOS.ReleaseLock), //24
				new SystemCall(theOS.Sleep), //25

				new SystemCall(theOS.SetPriority), //26
				new SystemCall(theOS.Exit), //27
				new SystemCall(theOS.FreeMemory), //28
				new SystemCall(theOS.MapSharedMem), //29
				new SystemCall(theOS.SignalEvent), //30

				new SystemCall(theOS.WaitEvent), //31
				new SystemCall(theOS.Input), //32
				new SystemCall(theOS.MemoryClear), //33
				new SystemCall(theOS.TerminateProcess), //34
				new SystemCall(theOS.Popr), //35

				new SystemCall(theOS.Popm)  //36
			};
		#endregion
		
			Debug.Assert(opCode >= InstructionType.Incr && opCode <= InstructionType.Popm);

			SystemCall call = sysCalls[(int)opCode];
			call();
		}


		#region Dump Functions for debugging
		/// <summary>
		/// Dumps the values of <see cref="registers"/> as the <see cref="CPU"/> currently sees it.
		/// </summary>
		public static void DumpRegisters()
		{
			if (bool.Parse(EntryPoint.Configuration["DumpRegisters"]) == false)
				return;

			Console.WriteLine("CPU Registers: r1 {0,-8:G}          r6  {1,-8:G}",registers[1],registers[6]);
			Console.WriteLine("               r2 {0,-8:G}          r7  {1,-8:G}",registers[2],registers[7]);
			Console.WriteLine("               r3 {0,-8:G}    (pid) r8  {1,-8:G}",registers[3],registers[8]);
			Console.WriteLine("               r4 {0,-8:G}   (data) r9  {1,-8:G}",registers[4],registers[9]);
			Console.WriteLine("               r5 {0,-8:G}     (sp) r10 {1}",registers[5],registers[10]);
			Console.WriteLine("               sf {0,-8:G}          ip  {1}",CPU.sf,CPU.ip);
			Console.WriteLine("               zf {0,-8:G}      ",CPU.zf);
		}

		/// <summary>
		/// Dumps the current <see cref="Instruction"/> for the current process at the current <see cref="ip"/>
		/// </summary>
		public static void DumpInstruction()
		{
			if (bool.Parse(EntryPoint.Configuration["DumpInstruction"]) == false)
				return;

			Console.WriteLine(" Pid:{0} {1} {2}",CPU.registers[8],(InstructionType)theOS.memoryMgr[theOS.currentProcess.PCB.pid,CPU.ip],(uint)theOS.memoryMgr[theOS.currentProcess.PCB.pid,CPU.ip]);
		}

		/// <summary>
		/// Dumps the content of the CPU's <see cref="physicalMemory"/> array.
		/// </summary>
		public static void DumpPhysicalMemory()
		{
			if (bool.Parse(EntryPoint.Configuration["DumpPhysicalMemory"]) == false)
				return;

			int address = 0;
			foreach (byte b in physicalMemory)
			{
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
		#endregion
	
		#region Type Conversion and Utility Functions

		/// <summary>
		/// Pins down a section of memory and converts an array of bytes into an unsigned int (<see cref="uint"/>)
		/// </summary>
		/// <param name="BytesIn">array of bytes to convert</param>
		/// <returns>value of bytes as a uint</returns>
		public unsafe static uint BytesToUInt(byte[] BytesIn)
		{
			fixed(byte* otherbytes = BytesIn)
			{
				uint newUint = 0;
				uint* ut = (uint*)&otherbytes[0];
				newUint = *ut;
				return newUint;
			}
		}

		/// <summary>
		/// Pins down a section of memory and converts an unsigned int into an array of (<see cref="byte"/>)s
		/// </summary>
		/// <param name="UIntIn">the uint to convert</param>
		/// <returns>uint containing the value of the uint</returns>
		public unsafe static byte[] UIntToBytes(uint UIntIn)
		{
			//turn a uint into 4 bytes
			byte[] fourBytes = new byte[4];
			uint* pt = &UIntIn;
			byte* bt = (byte*)&pt[0];
			fourBytes[0] = *bt++;
			fourBytes[1] = *bt++;
			fourBytes[2] = *bt++;
			fourBytes[3] = *bt++;
			return fourBytes;
		}

		/// <summary>
		/// Utility function to round any number to any arbirary boundary
		/// </summary>
		/// <param name="number">number to be rounded</param>
		/// <param name="boundary">boundary multiplier</param>
		/// <returns>new rounded number</returns>
		public static uint UtilRoundToBoundary(uint number, uint boundary)
		{
			uint newNumber = (uint)(boundary * ((number / boundary) + ((number % boundary > 0) ? 1: 0)));
			return newNumber;
		}
		#endregion
	}
}
