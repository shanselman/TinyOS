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
    using System;

    /// <summary>
    /// Represents a running Process in the <see cref="OS.runningProcesses"/> table.  Implements <see cref="IComparable"/> 
    /// so two Processes can be compared with &gt; and &lt;.  This will allow easy sorting of the runningProcesses table 
    /// based on <see cref="ProcessControlBlock.priority"/>.
    /// </summary>
    public sealed class Process : IComparable, IComparable<Process>
    {
        /// <summary>
        /// Process Constructor
        /// </summary>
        /// <param name="processId">the readonly unique id for this Process</param>
        /// <param name="memorySize">the ammount of memory this Process and address</param>
        public Process(uint processId, uint memorySize)
        {
            this.PCB = new ProcessControlBlock(processId, memorySize);
        }

        /// <summary>
        /// 
        /// </summary>
        public ProcessControlBlock PCB { get; }

        /// <summary>
        /// Needed to implement <see cref="IComparable"/>.  Compares Processes based on <see cref="ProcessControlBlock.priority"/>.
        /// <pre>
        /// Value                  Meaning 
        /// --------------------------------------------------------
        /// Less than zero         This instance is less than obj
        /// Zero                   This instance is equal to obj 
        /// Greater than an zero   This instance is greater than obj
        /// </pre>
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public int CompareTo(object obj)
        {
            if (!(obj is Process process))
            {
                throw new ArgumentException();
            }

            return this.CompareTo(process);
        }

        /// <summary>
        /// Needed to implement <see cref="IComparable"/>.  Compares Processes based on <see cref="ProcessControlBlock.priority"/>.
        /// <pre>
        /// Value                  Meaning 
        /// --------------------------------------------------------
        /// Less than zero         This instance is less than obj
        /// Zero                   This instance is equal to obj 
        /// Greater than an zero   This instance is greater than obj
        /// </pre>
        /// </summary>
        /// <param name="process"></param>
        /// <returns></returns>
        public int CompareTo(Process process)
        {
            // We want to sort HIGHEST priority first (reverse of typical)
            // Meaning 9,8,7,6,5,4,3,2,1 
            if (this.PCB.priority < process.PCB.priority)
            {
                return 1;
            }

            if (this.PCB.priority > process.PCB.priority)
            {
                return -1;
            }

            if (this.PCB.priority != process.PCB.priority)
            {
                return 0;
            }

            // Make sure potentially starved processes get a chance
            if (this.PCB.clockCycles < process.PCB.clockCycles)
            {
                return 1;
            }

            if (this.PCB.clockCycles > process.PCB.clockCycles)
            {
                return -1;
            }

            return 0;
        }
    }
}
