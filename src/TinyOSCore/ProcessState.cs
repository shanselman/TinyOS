﻿// ------------------------------------------------------------------------------
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
    /// <summary>
    /// All the states a <see cref="Process"/> can experience.
    /// </summary>
    public enum ProcessState
    {
        /// <summary>
        /// A <see cref="Process"/> initial state
        /// </summary>
        NewProcess = 0,

        /// <summary>
        /// The state of a <see cref="Process"/> ready to run
        /// </summary>
        Ready,

        /// <summary>
        /// The state of the currently running <see cref="Process"/>
        /// </summary>
        Running,

        /// <summary>
        /// The state of a <see cref="Process"/> waiting after a Sleep
        /// </summary>
        WaitingAsleep,

        /// <summary>
        /// The state of a <see cref="Process"/> waiting after an AcquireLock
        /// </summary>
        WaitingOnLock,

        /// <summary>
        /// The state of a <see cref="Process"/> waiting after a WaitEvent
        /// </summary>
        WaitingOnEvent,

        /// <summary>
        /// The state of a <see cref="Process"/> waiting to be removed from the Running <see cref="ProcessCollection"/>
        /// </summary>
        Terminated
    }
}
