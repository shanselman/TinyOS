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
using System.IO;
using System.Text.RegularExpressions;
using System.Configuration;

namespace Hanselman.CST352
{
	/// <summary>
	/// "Bootstraps" the system by creating an <see cref="OS"/>, setting the size of the <see cref="CPU"/>'s memory, 
	/// and loading each <see cref="Program"/> into memory.  Then, for each <see cref="Program"/> we create a 
	/// <see cref="Process"/>.  Then we start everything by calling <see cref="OS.execute()"/>
	/// </summary>
	class EntryPoint
	{
		/// <summary>
		/// The entry point for the virtual OS
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			OS theOS = null;
			uint bytesOfVirtualMemory = 0;
			uint bytesOfPhysicalMemory = 0;
			
			PrintHeader();

			if (args.Length < 2)
				PrintInstructions();
			else
			{
				//try
				{
					// Total addressable (virtual) memory taken from the command line
					bytesOfVirtualMemory = uint.Parse(args[0]);
					
					bytesOfPhysicalMemory = uint.Parse(ConfigurationManager.AppSettings["PhysicalMemory"]);

					// Setup static physical memory
					CPU.initPhysicalMemory(bytesOfPhysicalMemory); 

					// Create the OS and Memory Manager with Virtual Memory
					theOS = new OS(bytesOfVirtualMemory);

					// Let the CPU know about the OS
					CPU.theOS = theOS;

					Console.WriteLine("CPU has {0} bytes of physical memory",CPU.physicalMemory.Length);
					Console.WriteLine("OS  has {0} bytes of virtual (addressable) memory",theOS.memoryMgr.virtualMemSize);

					// For each file on the command line, load the program and create a process
					for (int i = 1; i < args.Length; i++)
					{
						if (File.Exists(args[i]))
						{
							Program p = Program.LoadProgram(args[i]);
							Process rp = theOS.createProcess(p, uint.Parse(ConfigurationManager.AppSettings["ProcessMemory"]));
							Console.WriteLine("Process id {0} has {1} bytes of process memory and {2} bytes of heap",rp.PCB.pid,ConfigurationManager.AppSettings["ProcessMemory"],rp.PCB.heapAddrEnd-rp.PCB.heapAddrStart);
							p.DumpProgram();
						}
					}

					// Start executing!
					theOS.execute();
				}
				//catch (Exception e)
				{
					//PrintInstructions();
					//Console.WriteLine(e.ToString());
				}

				// Pause
				Console.WriteLine("OS execution complete.  Press Enter to continue...");
				Console.ReadLine();
			}
		}
		
		/// <summary>
		/// Prints the static instructions on how to invoke from the command line
		/// </summary>
		private static void PrintInstructions()
		{
			Console.WriteLine("");
			Console.WriteLine("usage: OS membytes [files]");
		}

		/// <summary>
		/// Prints the static informatonal header
		/// </summary>
		private static void PrintHeader()
		{
			Console.WriteLine("Scott's CST352 Virtual Operating System");
			Console.WriteLine(System.Reflection.Assembly.GetExecutingAssembly().FullName);
			Console.WriteLine("Copyright (C) Scott Hanselman 2002. All rights reserved." + System.Environment.NewLine);	
			
		}
	}
}
