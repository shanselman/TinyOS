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
// ReSharper disable once CheckNamespace
namespace Hanselman.CST352
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Internal class to <see cref="Process"/> that represents a ProcessControlBlock.  It isn't a struct so it can have
    /// instance field initializers.  Maintains things like <see cref="registers"/> and <see cref="clockCycles"/> for this
    /// Process.
    /// 
    /// Global Data Region at R9 and SP at R10 are set in <see cref="OS.createProcess"/>
    /// </summary>
    public sealed class ProcessControlBlock
    {
        /// <summary>
        /// Constructor for a ProcessControlBlock
        /// </summary>
        /// <param name="id">the new readonly ProcessId.  Set only once, readonly afterwards.</param>
        /// <param name="memorySize"></param>
        public ProcessControlBlock(uint id, uint memorySize)
        {
            this.pid = id;
            this.registers[8] = this.pid;
            this.processMemorySize = memorySize;
        }
        #region Process Details

        /// <summary>
        /// The OS-wide unique Process ID.  This is set in the <see cref="ProcessControlBlock"/> constructor.
        /// </summary>
        public uint pid { get; }

        /// <summary>
        /// The length of the code segement for this Process relative to the 0.  It points one byte after the code segment.
        /// </summary>
        public uint codeSize { get; set; } = 0;

        /// <summary>
        /// Maximum size of the stack for this Process
        /// </summary>
        public uint stackSize { get; set; } = 0;

        /// <summary>
        /// Size of the Data Segement for this Process
        /// </summary>
        public uint dataSize { get; set; } = 0;

        /// <summary>
        /// Start address of the Heap for this Process
        /// </summary>
        public uint heapAddrStart { get; set; } = 0;

        /// <summary>
        /// End Address of the Heap for this Process 
        /// </summary>
        public uint heapAddrEnd { get; set; } = 0;

        /// <summary>
        /// ArrayList of MemoryPages that are associated with the Heap for this Process
        /// </summary>
        public IList<MemoryManager.MemoryPage> heapPageTable { get; } = new List<MemoryManager.MemoryPage>();

        /// <summary>
        /// The ammount of memory this Process is allowed to access.
        /// </summary>
        public uint processMemorySize { get; } = 0;
        #endregion

        #region Process State
        /// <summary>
        /// The states this Process can go through.  Starts at NewProcess, changes to Running.
        /// </summary>
        public ProcessState state { get; set; } = ProcessState.NewProcess;

        /// <summary>
        /// We have 10 registers.  R11 is the <see cref="ip"/>, and we don't use R0.  R10 is the <see cref="sp"/>.  So, that's 1 to 10, and 11.
        /// </summary>
        public uint[] registers { get; } = new uint[12];

        /// <summary>
        /// We have a Sign Flag and a Zero Flag in a <see cref="BitArray"/>
        /// </summary>
        private BitArray bitFlagRegisters { get; } = new BitArray(2, false);

        /// <summary>
        /// This <see cref="Process">Process's</see> current priority.  Can be changed programmatically.
        /// </summary>
        public int priority { get; set; } = 1;

        /// <summary>
        /// The number of <see cref="clockCycles"/> this <see cref="Process"/> can execute before being switched out.
        /// </summary>
        public int timeQuantum { get; } = 5;

        /// <summary>
        /// If we are waiting on a lock, we'll store it's value here
        /// </summary>
        public uint waitingLock { get; set; } = 0;

        /// <summary>
        /// If we are waiting on an event, we'll store it's value here
        /// </summary>
        public uint waitingEvent { get; set; } = 0;
        #endregion

        #region Counter Variables
        /// <summary>
        /// The number of clockCycles this <see cref="Process"/> has executed
        /// </summary>
        public int clockCycles { get; set; } = 0;

        /// <summary>
        /// The number of additional <see cref="clockCycles"/> to sleep.  
        /// If we are in a waiting state, and this is 0, we will sleep forever.  
        /// If this is 1 (we are about to wake up) our state will change to ProcessState.Running
        /// </summary>
        public uint sleepCounter { get; set; } = 0;

        /// <summary>
        /// The number of times this application has been switched out
        /// </summary>
        public int contextSwitches { get; set; } = 0;

        /// <summary>
        /// The number of pageFaults this <see cref="Process"/> has experienced.
        /// </summary>
        public int pageFaults { get; set; } = 0;
        #endregion

        #region Accessors
        /// <summary>
        /// Public get/set accessor for the Sign Flag
        /// </summary>
        public bool sf //Sign Flag
        {
            get => this.bitFlagRegisters[0];
            set => this.bitFlagRegisters[0] = value;
        }

        /// <summary>
        /// Public get/set accessor for the Zero Flag
        /// </summary>
        public bool zf //Zero Flag
        {
            get => this.bitFlagRegisters[1];
            set => this.bitFlagRegisters[1] = value;
        }

        /// <summary>
        /// Public get/set accessor for the Stack Pointer
        /// </summary>
        public uint sp
        {
            get => this.registers[10];
            set => this.registers[10] = value;
        }

        /// <summary>
        /// Public get/set accessor for the Instruction Pointer
        /// </summary>
        public uint ip
        {
            get => this.registers[11];
            set => this.registers[11] = value;
        }
        #endregion
    }
}
