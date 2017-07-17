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
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hanselman.CST352
{
    /// <summary>
    /// Represents a single line in a program, consisting of an <see cref="OpCode"/> 
    /// and one or two optional parameters.  An instruction can parse a raw instruction from a test file.
    /// Tge instruction is then loaded into an <see cref="List{Instruction}"/> which is a member of
    /// <see cref="Program"/>.  The <see cref="List{Instruction}"/> is translated into bytes that are 
    /// loaded into the processes memory space.  It's never used again, but it's a neat overly object oriented
    /// construct that simplified the coding of the creation of a <see cref="Program"/> and complicated the 
    /// running of the whole system.  It was worth it though.
    /// </summary>
    public class Instruction
	{

		/// <summary>
		/// Overridden method for pretty printing of Instructions
		/// </summary>
		/// <returns>A formatted string representing an Instruction</returns>
		public override string ToString()
		{
			return String.Format("OpCode: {0,-2:G} {1,-12:G}   Param1: {2,4:G}   Param2: {3,4:G}", (byte)this.OpCode, this.OpCode, this.Param1 == uint.MaxValue? "" :this.Param1.ToString(),this.Param2 == uint.MaxValue ? "" :this.Param2.ToString());
		}

		/// <summary>
		/// The OpCode for this Instruction
		/// </summary>
		public InstructionType OpCode;

		/// <summary>
		/// The first parameter to the opCode.  May be a Constant or a Register value, or not used at all
		/// </summary>
		public uint Param1 = uint.MaxValue;

		/// <summary>
		/// The second parameter to the opCode.  May be a Constant or a Register value, or not used at all
		/// </summary>
		public uint Param2 = uint.MaxValue;
	
		/// <summary>
		/// Public constructor for an Instruction
		/// </summary>
		/// <param name="rawInstruction">A raw string from a Program File.</param>
		/// <example>Any one of the following lines is a valid rawInstruction
		/// <pre>
		///  1   r1          ; incr r1
		///  2   r6, $16     ; add 16 to r6
		///  26  r6          ; setPriority to r6
		///  2   r2, $5      ; increment r2 by 5
		///  3   r1, r2      ; add 1 and 2 and the result goes in 1
		///  2   r2, $5      ; increment r2 by 5
		///  6   r3, $99     ; move 99 into r3
		///  7   r4, r3      ; move r3 into r4
		///  11  r4          ; print r4
		///  27              ; this is exit.
		/// </pre>
		/// </example>
		public Instruction(string rawInstruction)
		{
			Regex r = new Regex("(?:;.+)|\\A(?<opcode>\\d+){1}|\\sr(?<param>[-]*\\d)|\\$(?<const>[-]*\\d+)");
			
			MatchCollection matchcol = r.Matches(rawInstruction);
			foreach(Match m in matchcol)
			{
				GroupCollection g = m.Groups;

				for (int i = 1; i < g.Count ; i++) 
				{
					if (g[i].Value.Length != 0 ) 
					{
						if (r.GroupNameFromNumber(i) == "opcode")
						{
							this.OpCode = (InstructionType)byte.Parse(g[i].Value);	
						}
								
						if (r.GroupNameFromNumber(i) == "param" || r.GroupNameFromNumber(i) == "const")
						{
							//Yank them as ints (to preserve signed-ness)
							// Treat them as uints for storage
							// This will only affect negative numbers, and 
							// VERY large unsigned numbers
							if (uint.MaxValue == this.Param1) 
								this.Param1 = uint.Parse(g[i].Value);	
							else if (uint.MaxValue == this.Param2) 
							{
								if (g[i].Value[0] == '-')
									this.Param2 = (uint)int.Parse(g[i].Value);	
								else
									this.Param2 = uint.Parse(g[i].Value);	

							}
						}

					}
				}
			}
		}
	}

	/// <summary>
	/// This enum provides an easy conversion between numerical opCodes like "2" and text 
	/// and easy to remember consts like "Addi"
	/// </summary>
	public enum InstructionType
	{
		/// <summary>
		/// No op
		/// </summary>
		Noop = 0,

		/// <summary>
		/// Increments register
		/// <pre>
		/// 1 r1
		/// </pre>
		/// </summary>
		Incr,

		/// <summary>
		///  Adds constant 1 to register 1
		/// <pre>
		/// 2 r1, $1
		/// </pre>
		/// </summary>
		Addi,

		/// <summary>
		/// Adds r2 to r1 and stores the value in r1
		/// <pre>
		/// 3 r1, r2
		/// </pre>
		/// </summary>
		Addr,

		/// <summary>
		/// Pushes contents of register 1 onto stack
		/// <pre>
		/// 4 r1
		/// </pre>
		/// </summary>
		Pushr,

		/// <summary>
		/// Pushes constant 1 onto stack
		/// <pre>
		/// 5 $1
		/// </pre>
		/// </summary>
		Pushi,

		/// <summary>
		/// Moves constant 1 into register 1
		/// <pre>
		/// 6 r1, $1
		/// </pre>
		/// </summary>
		Movi,

		/// <summary>
		/// Moves contents of register2 into register 1
		/// <pre>
		/// 7 r1, r2
		/// </pre>
		/// </summary>
		Movr,

		/// <summary>
		/// Moves contents of memory pointed to register 2 into register 1
		/// <pre>
		/// 8 r1, r2
		/// </pre>
		/// </summary>
		Movmr,

		/// <summary>
		/// Moves contents of register 2 into memory pointed to by register 1
		/// <pre>
		/// 9 r1, r2
		/// </pre>
		/// </summary>
		Movrm,

		/// <summary>
		/// Moves contents of memory pointed to by register 2 into memory pointed to by register 1
		/// <pre>
		/// 10 r1, r2
		/// </pre>
		/// </summary>
		Movmm,

		/// <summary>
		/// Prints out contents of register 1
		/// <pre>
		/// 11 r1
		/// </pre>
		/// </summary>
		Printr,

		/// <summary>
		/// Prints out contents of memory pointed to by register 1
		/// <pre>
		/// 12 r1
		/// </pre>
		/// </summary>
		Printm,

		/// <summary>
		/// Control transfers to the instruction whose address is r1 bytes relative to the current instruction. 
		/// r1 may be negative.
		/// <pre>
		/// 13 r1
		/// </pre>
		/// </summary>
		Jmp,

		/// <summary>
		/// Compare contents of r1 with 1.  If r1 &lt; 9 set sign flag.  If r1 &gt; 9 clear sign flag.
		/// If r1 == 9 set zero flag.
		/// <pre>
		/// 14 r1, $9
		/// </pre>
		/// </summary>
		Cmpi,


		/// <summary>
		/// Compare contents of r1 with r2.  If r1 &lt; r2 set sign flag.  If r1 &gt; r2 clear sign flag.
		/// If r1 == r2 set zero flag.
		/// <pre>
		/// 15 r1, r2
		/// </pre>
		/// </summary>
		Cmpr,


		/// <summary>
		/// If the sign flag is set, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 16 r1
		/// </pre>		
		/// </summary>
		Jlt,

		/// <summary>
		/// If the sign flag is clear, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 17 r1
		/// </pre>		
		/// </summary>
		Jgt,

		/// <summary>
		/// If the zero flag is set, jump to the instruction that is offset r1 bytes from the current instruction
		/// <pre>
		/// 18 r1
		/// </pre>		
		/// </summary>
		Je,

		/// <summary>
		/// Call the procedure at offset r1 bytes from the current instrucion.  
		/// The address of the next instruction to excetute after a return is pushed on the stack
		/// <pre>
		/// 19 r1
		/// </pre>		
		/// </summary>
		Call,

		/// <summary>
		/// Call the procedure at offset of the bytes in memory pointed by r1 from the current instrucion.  
		/// The address of the next instruction to excetute after a return is pushed on the stack
		/// <pre>
		/// 20 r1
		/// </pre>		
		/// </summary>
		Callm,

		/// <summary>
		/// Pop the return address from the stack and transfer control to this instruction
		/// <pre>
		/// 21
		/// </pre>		
		/// </summary>
		Ret,

		/// <summary>
		/// Allocate memory of the size equal to r1 bytes and return the address of the new memory in r2.  
		/// If failed, r2 is cleared to 0.
		/// <pre>
		/// 22 r1, r2
		/// </pre>		
		/// </summary>
		Alloc,

		/// <summary>
		/// Acquire the OS lock whose # is provided in register r1.  
		/// Icf the lock is not held by the current process
		/// the operation is a no-op
		/// <pre>
		/// 23 r1
		/// </pre>		
		/// </summary>
		AcquireLock,

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
		ReleaseLock,

		/// <summary>
		/// Sleep the # of clock cycles as indicated in r1.  
		/// Another process or the idle process 
		/// must be scheduled at this point.  
		/// If the time to sleep is 0, the process sleeps infinitely
		/// <pre>
		/// 25 r1
		/// </pre>		
		/// </summary>
		Sleep,

		/// <summary>
		/// Set the priority of the current process to the value
		/// in register r1
		/// <pre>
		/// 26 r1
		/// </pre>		
		/// </summary>
		SetPriority,

		/// <summary>
		/// This opcode causes an exit and the process's memory to be unloaded.  
		/// Another process or the idle process must now be scheduled
		/// <pre>
		/// 27
		/// </pre>		
		/// </summary>
		Exit,

		/// <summary>
		/// Free the memory allocated whose address is in r1
		/// <pre>
		/// 28 r1
		/// </pre>		
		/// </summary>
		FreeMemory,

		/// <summary>
		/// Map the shared memory region identified by r1 and return the start address in r2
		/// <pre>
		/// 29 r1, r2
		/// </pre>		
		/// </summary>
		MapSharedMem,

		/// <summary>
		/// Signal the event indicated by the value in register r1
		/// <pre>
		/// 30 r1
		/// </pre>		
		/// </summary>
		SignalEvent,

		/// <summary>
		/// Wait for the event in register r1 to be triggered resulting in a context-switch
		/// <pre>
		/// 31 r1
		/// </pre>		
		/// </summary>
		WaitEvent,

		/// <summary>
		/// Read the next 32-bit value into register r1
		/// <pre>
		/// 32 r1
		/// </pre>		
		/// </summary>
		Input,

		/// <summary>
		/// set the bytes starting at address r1 of length r2 to zero
		/// <pre>
		/// 33 r1, r2
		/// </pre>		
		/// </summary>
		MemoryClear,

		/// <summary>
		/// Terminate the process whose id is in the register r1
		/// <pre>
		/// 34 r1
		/// </pre>		
		/// </summary>
		TerminateProcess,

		/// <summary>
		/// Pop the contents at the top of the stack into register r1 
		/// <pre>
		/// 35 r1
		/// </pre>		
		/// </summary>
		Popr,

		/// <summary>
		/// Pop the contents at the top of the stack into the memory pointed to by register r1 
		/// <pre>
		/// 36 r1
		/// </pre>		
		/// </summary>
		Popm
	};

}
