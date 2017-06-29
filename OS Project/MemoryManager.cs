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
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Configuration;
using System.Runtime.Serialization.Formatters.Binary;

namespace Hanselman.CST352
{
	/// <summary>
	/// The MemoryManager for the <see cref="OS"/>.   All memory accesses by a <see cref="Process"/> 
	/// go through this class.
	/// </summary>
	/// <example>
	/// theOS.memoryMgr[processId, 5]; //accesses memory at address 5
	/// </example>
	public class MemoryManager
	{
		private ArrayList _pageTable;
		
		//BitArray freePhysicalPages = new BitArray((int)(CPU.physicalMemory.Length/CPU.pageSize), true);
		private  bool[] freePhysicalPages = new bool[(int)(CPU.physicalMemory.Length/CPU.pageSize)];

		/// <summary>
		/// Total ammount of addressable memory.  This is set in the Constructor. 
		/// Once set, it is readonly
		/// </summary>
		public readonly uint virtualMemSize = 0;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="virtualMemSizeIn"></param>
		public MemoryManager(uint virtualMemSizeIn)
		{
			//
			// Find a size for addressableMemory that is on a page boundary
			//
			virtualMemSize = CPU.UtilRoundToBoundary(virtualMemSizeIn, CPU.pageSize);

			//
			// Size of memory must be a factor of CPU.pageSize
			// This was asserted when the CPU initialized memory
			//
			uint physicalpages = (uint)(CPU.physicalMemory.Length/CPU.pageSize);
			uint addressablepages = (uint)(virtualMemSize/CPU.pageSize);
			
			_pageTable = new ArrayList((int)addressablepages);

			// Delete all our Swap Files
			foreach (string f in Directory.GetFiles(".","*.xml"))
				File.Delete(f);

			// For all off addressable memory...
			// Make the pages in physical and the pages that aren't in physical
			for (uint i = 0;i < virtualMemSize; i+=CPU.pageSize)
			{
				// Mark the Pages that are in physical memory as "false" or "not free"
				MemoryPage p;
				if (i < CPU.physicalMemory.Length) 
				{
					p = new MemoryPage(i, true);
					freePhysicalPages[(int)(i/CPU.pageSize)] = false;
				}
				else p = new MemoryPage(i, false);

				_pageTable.Add(p);
			}

			//
			// Cordon off some shared memory regions...these are setting the AppSettings
			//
			uint SharedRegionsSize = uint.Parse(ConfigurationSettings.AppSettings["SharedMemoryRegionSize"]);
			uint SharedRegions = uint.Parse(ConfigurationSettings.AppSettings["NumOfSharedMemoryRegions"]);
			if (SharedRegions > 0 && SharedRegionsSize > 0)
			{
				uint TotalPagesNeeded = (uint)(SharedRegions*SharedRegionsSize/CPU.pageSize);
				uint pagesPerRegion = TotalPagesNeeded/SharedRegions;

				// ForExample: 
				// I need 2 regions
				//	64 bytes needed for each
				//  4 pages each
				//  8 total pages needed

				// Because I pre-allocate shared memory I'll have the luxury of contigous pages of memory.
				// I'll exploit this hack in MapSharedMemoryToProcess
				foreach (MemoryPage page in _pageTable)
				{
					// Do we still need pages?
					if (TotalPagesNeeded > 0)
					{
						// If this page is assigned to the OS, take it
						if (page.SharedMemoryRegion == 0) 
						{
							// Now assign it to us
							page.SharedMemoryRegion = SharedRegions;
							TotalPagesNeeded--;
							if (TotalPagesNeeded % pagesPerRegion == 0) 
								SharedRegions--;
						}
					}
					else //We have all we need
						break;
				}
			}
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="p">The Process</param>
		/// <param name="bytesRequested">The number of bytes requested.  Will be rounded up to the nearest page</param>
		/// <returns>The Start Address of the Alloc'ed memory</returns>
		public uint ProcessHeapAlloc(Process p, uint bytesRequested)
		{
			// Round up to the nearest page boundary
			uint pagesRequested = MemoryManager.BytesToPages(bytesRequested);
			uint addrStart = 0;

			//
			// Finds n *Contiguous* Pages
			//

			// Start with a list of potentialPages...
			ArrayList potentialPages =  new ArrayList();
			
			// Look through all the pages in our heap
			for (int i = 0; i < p.PCB.heapPageTable.Count; i++)
			{
				// The pages must be contiguous
				bool bContiguous = true;
				
				//From this start page, check for contiguous free pages nearby
				MemoryPage startPage = (MemoryPage)p.PCB.heapPageTable[i];

				//Is this page, and x ahead of it free?
				if (startPage.heapAllocationAddr == 0)
				{
					potentialPages.Clear();
					potentialPages.Add(startPage);

					//Is this page, and x ahead of it free?
					for (int j = 1; j < pagesRequested;j++)
					{
						// Have we walked past the end of the heap?
						if ((i+j) >= p.PCB.heapPageTable.Count)
							throw new HeapException(p.PCB.pid, pagesRequested*CPU.pageSize);

						MemoryPage nextPage = (MemoryPage)p.PCB.heapPageTable[i+j];
						if (nextPage.heapAllocationAddr == 0) 
							potentialPages.Add(nextPage);
						else
							bContiguous = false;
					}
					// If we make it here, we've found enough contiguous pages, break and continue
					if (bContiguous == true) 
						break;
				}
			}

			// Did we not find enough pages?
			if (potentialPages.Count != pagesRequested)
				throw new HeapException(p.PCB.pid, pagesRequested*CPU.pageSize);

			// Mark each page with the address of the original alloc 
			// so we can Free them later
			addrStart = ((MemoryPage)potentialPages[0]).addrProcessIndex;
			foreach (MemoryPage page in potentialPages)
				page.heapAllocationAddr = addrStart;

			return addrStart;
		}


		/// <summary>
		/// For debugging only.  The value used to "zero out" memory when doing a FreeMemory. 
		/// </summary>
		private static int memoryClearInt;

		/// <summary>
		/// Releases pages that were Alloc'ed from the Process's Heap
		/// </summary>
		/// <param name="p">The Processes</param>
		/// <param name="startAddr">The Process address that the allocation began at</param>
		/// <returns></returns>
		public uint ProcessHeapFree(Process p, uint startAddr)
		{
			uint pageCount = 0;
			foreach (MemoryPage page in p.PCB.heapPageTable)
			{
				if (page.heapAllocationAddr == startAddr)
				{
					page.heapAllocationAddr = 0;
					pageCount++;
				}
			}
			
			//
			// For Heap Debugging, uncomment this line, 
			// this incrementing value will be used to 
			// clear memory out when releasing heap blocks
			//
			//memoryClearInt++;
			
			memoryClearInt = 0;
			SetMemoryOfProcess(p.PCB.pid, startAddr, pageCount*CPU.pageSize, (byte)memoryClearInt);
			return 0;

		}

		/// <summary>
		/// Adds all the pages allocated to a Process's heap to a PCB specific table of memory pages
		/// </summary>
		/// <param name="p">The Process</param>
		public void CreateHeapTableForProcess(Process p)
		{
			foreach (MemoryPage page in _pageTable)
			{
				if (page.pidOwner == p.PCB.pid)
				{
					if (page.addrProcessIndex >= p.PCB.heapAddrStart && page.addrProcessIndex < p.PCB.heapAddrEnd)
					{
						p.PCB.heapPageTable.Add(page);
					}
				}
			}
		}


		/// <summary>
		/// Gets a 4 byte unsigned integer (typically an opCode param) from memory
		/// </summary>
		/// <param name="processid">The calling processid</param>
		/// <param name="processIndex">The address in memory from the Process's point of view</param>
		/// <returns></returns>
		public uint getUIntFrom(uint processid, uint processIndex)
		{
			return CPU.BytesToUInt(getBytesFrom(processid, processIndex, 4));
		}

		/// <summary>
		/// Sets a 4 byte unsigned integer (typically an opCode param) to memory
		/// </summary>
		/// <param name="processid">The calling processid</param>
		/// <param name="processIndex">The address in memory from the Process's point of view</param>
		/// <param name="avalue">The new value</param>
		public void setUIntAt(uint processid, uint processIndex, uint avalue)
		{
			setBytesAt(processid, processIndex, CPU.UIntToBytes(avalue));
		}

		/// <summary>
		/// Gets an array of "length" bytes from a specific process's memory address
		/// </summary>
		/// <param name="processid">The calling process's id</param>
		/// <param name="processIndex">The address in memory from the Process's point of view</param>
		/// <param name="length">how many bytes</param>
		/// <returns>an initialized byte array containing the contents of memory</returns>
		public byte[] getBytesFrom(uint processid, uint processIndex, uint length)
		{
			byte[] bytes = new byte[length];
			for (uint i = 0; i < length; i++)
			{
				bytes[i] = this[processid, processIndex + i];
			}
			return bytes;
		}

		/// <summary>
		/// Sets an array of bytes to a specific process's memory address
		/// </summary>
		/// <param name="processid">The calling processid</param>
		/// <param name="processIndex">The address in memory from the Process's point of view</param>
		/// <param name="pageValue">The source array of bytes</param>
		public void setBytesAt(uint processid, uint processIndex, byte[] pageValue)
		{
			for (uint i = 0; i < pageValue.Length; i++)
			{
				this[processid, processIndex+i] = pageValue[i];
			}
		}


	
		/// <summary>
		/// Translates a Process's address space into physical address space
		/// </summary>
		/// <param name="processid">The calling process's id</param>
		/// <param name="processMemoryIndex">The address in memory from the Process's point of view</param>
		/// <param name="dirtyFlag">Whether we mark this <see cref="MemoryPage"/> as dirty or not</param>
		/// <returns>The physical address of the memory we requested</returns>
		/// <exception cref='MemoryException'>This process has accessed memory outside it's Process address space</exception>
		public uint ProcessAddrToPhysicalAddr(uint processid, uint processMemoryIndex, bool dirtyFlag)
		{
			foreach(MemoryPage page in _pageTable)
			{
				// If this process owns this page
				if (page.pidOwner == processid)
				{	
					// If this page is responsible for the memory addresses we are interested in
					if((processMemoryIndex >= page.addrProcessIndex) && (processMemoryIndex < page.addrProcessIndex+CPU.pageSize))
					{
						// Get the page offset
						uint pageOffset = processMemoryIndex - page.addrProcessIndex;
						return ProcessAddrToPhysicalAddrHelper(page, dirtyFlag, pageOffset);
					}
				}

				// Maybe this is a shared region?
				if (page.SharedMemoryRegion != 0)
				{
					// Go through the list of owners and see if we are one...
					for (int i = 0; i <= page.pidSharedOwnerList.Count-1; i++)
					{
						// Do we own this page?
						if ((uint)page.pidSharedOwnerList[i] == processid)
						{
							// Does this page handle this address?
							if (processMemoryIndex >= (uint)page.pidSharedProcessIndex[i] && processMemoryIndex < (uint)page.pidSharedProcessIndex[i]+CPU.pageSize)
							{
								uint pageOffset = processMemoryIndex - (uint)page.pidSharedProcessIndex[i];
								return ProcessAddrToPhysicalAddrHelper(page, dirtyFlag, pageOffset);
							}
						}
					}
				}
			}
			
			// If we make it here, this process has accessed memory that doesn't exist in the page table
			// We'll catch this exception and terminate the process for accessing memory that it doesn't own
			throw(new MemoryException(processid, processMemoryIndex));
		}

		private uint ProcessAddrToPhysicalAddrHelper(MemoryPage page, bool dirtyFlag, uint pageOffset)
		{
			// Get the page offset
			uint virtualIndex = page.addrVirtual+pageOffset;
						
			// Update Flags for this process
			page.isDirty = dirtyFlag || page.isDirty;
			page.accessCount++;
			page.lastAccessed = DateTime.Now;
						
			// Take this new "virtual" address (relative to all addressable memory)
			// and translate it to physical ram.  Page Faults may occur inside this next call.
			uint physicalIndex = VirtualAddrToPhysical(page,virtualIndex);
			return physicalIndex;
		}


		/// <summary>
		/// Resets a memory page to defaults, deletes that page's swap file and 
		/// may mark the page as free in physical memory
		/// </summary>
		/// <param name="page">The <see cref="MemoryPage"/> to reset</param>
		public void ResetPage(MemoryPage page)
		{
			if (page.isValid == true)
			{
				// Make this page as availble in physical memory
				uint i = page.addrPhysical / CPU.pageSize;			
				Debug.Assert(i < freePhysicalPages.Length); //has to be
				freePhysicalPages[(int)i] = true;
			}

			//Reset to reasonable defaults
			page.isDirty = false;
			page.addrPhysical = 0;
			page.pidOwner = 0;
			page.pageFaults = 0;
			page.accessCount = 0;
			page.lastAccessed = DateTime.Now;
			page.addrProcessIndex = 0;
			page.heapAllocationAddr = 0;

			// Delete this page's swap file
			string filename = System.Environment.CurrentDirectory + "/page" + page.pageNumber + "." + page.addrVirtual + ".xml";
			File.Delete(filename);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="page"></param>
		/// <param name="virtualIndex"></param>
		/// <returns></returns>
		public uint VirtualAddrToPhysical(MemoryPage page, uint virtualIndex)
		{
			if (page.isValid == false)
			{
				int i = 0;
				for (; i < freePhysicalPages.Length; i++)
				{
					if (freePhysicalPages[i] == true)
					{
						// Found a free physical page!
						freePhysicalPages[i] = false;
						break; 
					}
				}

				// If we have reach the end of the freePhysicalPages 
				// without finding a free page - we are out of physical memory, therefore
				// we PageFault and start looking for victim pages to swap out
				if (i == freePhysicalPages.Length)
				{
					MemoryPage currentVictim = null;
					foreach (MemoryPage possibleVictim in _pageTable)
					{
						if (!page.Equals(possibleVictim) && possibleVictim.isValid == true)
						{
							if (currentVictim == null) currentVictim = possibleVictim;

							// If this is the least accessed Memory Page we've found so far
							if (possibleVictim.lastAccessed < currentVictim.lastAccessed)
								currentVictim = possibleVictim;
						}
					}
					// Did we find no victims?  That's a HUGE problem, and shouldn't ever happen
					Debug.Assert(currentVictim != null);
					
					SwapOut(currentVictim);
					
					// Take the physical address of this page
					page.addrPhysical = currentVictim.addrPhysical;
					
					SwapIn(page);
				}
				else // no page fault
				{
					// Map this page to free physical page "i"
					page.addrPhysical = (uint)(i*CPU.pageSize);
					SwapIn(page);
				}

			}

			// Adjust the physical address with pageOffset from a page boundary
			uint pageOffset = virtualIndex % CPU.pageSize;
			uint physicalIndex = page.addrPhysical+pageOffset;
			return physicalIndex;
		}
		
		/// <summary>
		/// Public accessor method to make Virtual Memory look like an array
		/// </summary>
		/// <example>
		/// theOS.memoryMgr[processId, 5]; //accesses memory at address 5
		/// </example>
		public byte this[uint processid, uint processMemoryIndex] 
		{
			get
			{
				uint physicalIndex = ProcessAddrToPhysicalAddr(processid, processMemoryIndex, false);
				return CPU.physicalMemory[physicalIndex];
			}
			set 
			{
				uint physicalIndex = ProcessAddrToPhysicalAddr(processid, processMemoryIndex, true);
				CPU.physicalMemory[physicalIndex] = value;
			}
		}

		/// <summary>
		/// Helper method to translate # of bytes to # of Memory Pages
		/// </summary>
		/// <param name="bytes">bytes to translate</param>
		/// <returns>number of pages</returns>
		public static uint BytesToPages(uint bytes)
		{
			return (CPU.UtilRoundToBoundary(bytes, CPU.pageSize)/CPU.pageSize);
			//return ((uint)(bytes / CPU.pageSize) + (uint)(bytes % CPU.pageSize));
		}

		/// <summary>
		/// Takes a Process's ID and releases all MemoryPages assigned to it, zeroing and reseting them
		/// </summary>
		/// <param name="pid">Process ID</param>
		public void ReleaseMemoryOfProcess(uint pid)
		{
			foreach (MemoryPage page in _pageTable)
			{
				if (page.pidOwner == pid)
				{
					if (page.isValid == true)
					{
						SetMemoryOfProcess(pid,page.addrProcessIndex,CPU.pageSize,0);
					}
					ResetPage(page);
				}

				if (page.SharedMemoryRegion != 0)
				{
					for (int i = 0; i <= page.pidSharedOwnerList.Count-1; i++)
					{
						// Do we own this page?
						if ((uint)page.pidSharedOwnerList[i] == pid)
						{
                            page.pidSharedOwnerList.RemoveAt(i);
							page.pidSharedProcessIndex.RemoveAt(i);
							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Zeros out memory belonging to a Process from start until length
		/// </summary>
		/// <param name="pid">Process ID</param>
		/// <param name="start">start memory address</param>
		/// <param name="length">length in bytes</param>
		/// <param name="newvalue">the new value of the byte</param>
		public void SetMemoryOfProcess(uint pid, uint start, uint length, byte newvalue)
		{
			for (uint i = start; i < (start+length);i++)
				this[pid,i] = newvalue;
		}

		/// <summary>
		/// Maps the shared memory region to the process passed in
		/// </summary>
		/// <param name="memoryRegion">the number of the shared region to map</param>
		/// <param name="pid">Process ID</param>
		/// <returns>the index in process memory of the shared region</returns>
		public uint MapSharedMemoryToProcess(uint memoryRegion, uint pid)
		{
			uint SharedRegionsSize = uint.Parse(ConfigurationSettings.AppSettings["SharedMemoryRegionSize"]);
			uint PagesNeeded = (uint)(SharedRegionsSize/CPU.pageSize);

			uint startAddrProcessIndex;
			uint addrProcessIndex = 0;

			//Find the max address used by this process (a free place to map this memory to)
			foreach (MemoryPage page in _pageTable)
			{
				if (page.pidOwner == pid)
					addrProcessIndex = Math.Max(page.addrProcessIndex,addrProcessIndex);
			}
			//Add one more page, to get the address of where to map the Shared Memory Region 
			addrProcessIndex += CPU.pageSize;
			startAddrProcessIndex = addrProcessIndex;

			// Very inefficient: 
			// Now, find the Shared Memory pages and and map them to this process
			foreach (MemoryPage page in _pageTable)
			{
				if (PagesNeeded > 0)
				{
					if (page.SharedMemoryRegion == memoryRegion)
					{
						page.pidSharedOwnerList.Add(pid);
						page.pidSharedProcessIndex.Add(addrProcessIndex);
						addrProcessIndex += CPU.pageSize;
						PagesNeeded--;
					}
				}
				else
					// We've got enough pages...
					break; 
			}
			return startAddrProcessIndex;
		}


		/// <summary>
		/// Takes a number of bytes and a process id and assigns MemoryPages in the pageTable to the Process
		/// </summary>
		/// <param name="bytes"># of bytes to assign</param>
		/// <param name="pid">Process ID</param>
		public void MapMemoryToProcess(uint bytes, uint pid)
		{
			uint pagesNeeded = BytesToPages(bytes);
			uint addrProcessIndex = 0;
						
			foreach (MemoryPage page in _pageTable)
			{
				if (pagesNeeded > 0)
				{
					// If this page is assigned to the OS, 
					// and not a SharedMemoryRegion and take it
					if (page.pidOwner == 0 && page.SharedMemoryRegion == 0) 
					{
						// Now assign it to us
						page.pidOwner = pid; 
						page.addrProcessIndex = addrProcessIndex;
						addrProcessIndex += CPU.pageSize;
						pagesNeeded--;
					}
				}
				else
					// We've got enough pages...
					break; 
			}
			
			// Did we go through the whole pageTable and not have enough memory?
			if (pagesNeeded > 0)
			{ 
				Console.WriteLine("OUT OF MEMORY: Process {0} requested {1} more bytes than were available!",pid,pagesNeeded*CPU.pageSize); 
				System.Environment.Exit(1);
			}
		}

		/// <summary>
		/// Represents an entry in the Page Table.  MemoryPages (or "Page Table Entries") 
		/// are created once and never destroyed, their values are just reassigned
		/// </summary>
		public class MemoryPage 
		{
			/// <summary>
			/// The number of the shared memory region this MemoryPage is mapped to
			/// </summary>
			public uint SharedMemoryRegion = 0;

			/// <summary>
			/// One of two parallel arrays, one of shared owners of this page, one of shared process indexes of this page
			/// </summary>
			public ArrayList pidSharedOwnerList = new ArrayList();

			/// <summary>
			/// One of two parallel arrayz, one of shared owners of this page, one of shared process indexes of this page
			/// </summary>
			public ArrayList pidSharedProcessIndex = new ArrayList();

			/// <summary>
			/// The number this page is in addressable Memory.  Set once and immutable
			/// </summary>
			public readonly uint pageNumber = 0;

			/// <summary>
			/// The address in addressable space this page is responsbile for
			/// </summary>
			public readonly uint addrVirtual = 0;

			/// <summary>
			/// The address in Process space this page is responsible for
			/// </summary>
			public uint addrProcessIndex = 0;

			/// <summary>
			/// The process address that originally allocated this page.  Kept so we can free that page(s) later.
			/// </summary>
			public uint heapAllocationAddr = 0;

			/// <summary>
			/// The process that is currently using this apge
			/// </summary>
			public uint pidOwner = 0;

			/// <summary>
			/// This is only valid when 
			/// pidOwner != 0 and isValid == true
			/// meaning the page is actually mapped and present
			/// </summary>
			public uint addrPhysical = 0; 
 
			/// <summary>
			/// Is the page in memory now?
			/// </summary>
			public bool isValid;		

			/// <summary>
			/// Has the page been changes since it was last swapped in from Disk?
			/// </summary>
			public bool isDirty = false;			
			
			/// <summary>
			/// For statistics: How many times has this page been involved in a pageFault?
			/// </summary>
			public uint pageFaults = 0;				

			/// <summary>
			/// For aging and swapping: How many times has this page's address range been accessed?
			/// </summary>
			public uint accessCount = 0;			

			/// <summary>
			/// For aging and swapping: When was this page last accessed?
			/// </summary>
			public DateTime lastAccessed = DateTime.Now; 

			/// <summary>
			/// Only public constructor for a Memory Page and is only called once 
			/// in the <see cref="MemoryManager"/> constructor
			/// </summary>
			/// <param name="initAddrVirtual">The address in addressable memory this page is responsible for</param>
			/// <param name="isValidFlag">Is this page in memory right now?</param>
			public MemoryPage(uint initAddrVirtual, bool isValidFlag)
			{
				isValid = isValidFlag;
				if (isValid)
					addrPhysical = initAddrVirtual;
				addrVirtual = initAddrVirtual;
				pageNumber = (addrVirtual)/CPU.pageSize;
			}
		}

		/// <summary>
		/// Represents the actual values in memory that a MemoryPage points to.  
		/// MemoryPageValue is serialized to disk, currently as XML, in <see cref="SwapOut"/>.
		/// </summary>
		[Serializable] public class MemoryPageValue
		{
			/// <summary>
			/// The array of bytes holding the value of memory for this page
			/// </summary>
			[XmlArray(ElementName = "byte", Namespace = "http://www.hanselman.com")]
			public byte[] memory = new byte[CPU.pageSize];
			
			/// <summary>
			/// For aging and swapping: How many times has this page's address range been accessed?
			/// </summary>
			public uint accessCount = 0;

			/// <summary>
			/// For aging and swapping: When was this page last accessed?
			/// </summary>
			public DateTime lastAccessed = DateTime.Now;
		}

		/// <summary>
		/// Swaps the specified <see cref="MemoryPage"/> to disk.  Currently implemented as XML for fun.
		/// </summary>
		/// <param name="victim">The <see cref="MemoryPage"/> to be swapped</param>
		public void SwapOut(MemoryPage victim)
		{
			if (victim.isDirty)
			{

				// Generate a filename based on address and page number
				string filename = System.Environment.CurrentDirectory + "/page" + victim.pageNumber + "-" + victim.addrVirtual + ".xml";

//				IFormatter ser = new BinaryFormatter();
//				Stream writer = new FileStream(filename, FileMode.Create);

				XmlSerializer ser = new XmlSerializer(typeof(MemoryPageValue));
				Stream fs = new FileStream(filename, FileMode.Create);
				XmlWriter writer = new XmlTextWriter(fs, new UTF8Encoding());

				MemoryPageValue pageValue = new MemoryPageValue();

				// Copy the bytes from Physical Memory so we don't pageFault in a Fault Hander
				byte[] bytes = new byte[CPU.pageSize];
				for (int i = 0; i < CPU.pageSize; i++)
					bytes[i] = CPU.physicalMemory[victim.addrPhysical+i];

				// Copy details from the MemoryPage to the MemoryPageValue
				pageValue.memory = bytes;
				pageValue.accessCount = victim.accessCount;
				pageValue.lastAccessed = victim.lastAccessed;

				//Console.WriteLine("Swapping out page {0} at physical memory {1}",victim.pageNumber, victim.addrPhysical);
			
				// Write the MemoryPageValue to disk!
				ser.Serialize(writer,pageValue);
						
				//writer.Flush();
				//writer.Close();
				fs.Close();
			}
			victim.isValid = false;
		}

		/// <summary>
		/// Swaps in the specified <see cref="MemoryPage"/> from disk.  Currently implemented as XML for fun.
		/// </summary>
		/// <param name="winner">The <see cref="MemoryPage"/> that is being swapped in</param>
		public void SwapIn(MemoryPage winner)
		{
			// Generate a filename based on address and page number
			string filename = System.Environment.CurrentDirectory + "/page" + winner.pageNumber + "-" + winner.addrVirtual + ".xml";
			if (File.Exists(filename) && winner.isValid == false)
			{
				//BinaryFormatter ser = new BinaryFormatter();
				//Stream reader = new FileStream(filename, FileMode.Open);

				XmlSerializer ser = new XmlSerializer(typeof(MemoryPageValue));
				Stream fs = new FileStream(filename, FileMode.Open);
				XmlReader reader = new XmlTextReader(fs);

				// Load the MemoryPageValue in from Disk!
				MemoryPageValue pageValue = (MemoryPageValue)ser.Deserialize(reader);
				
				// Copy the bytes from Physical Memory so we don't pageFault in a Fault Hander
				for (int i = 0; i < CPU.pageSize; i++)
					CPU.physicalMemory[winner.addrPhysical+i] = pageValue.memory[i];

				//Console.WriteLine("Swapping in page {0} at physical memory {1}",winner.pageNumber, winner.addrPhysical);
				
				winner.accessCount = pageValue.accessCount;
				winner.lastAccessed = pageValue.lastAccessed;

				pageValue = null;

				reader.Close();
				fs.Close();
				File.Delete(filename);
			}
			else //no swap file, do nothing
			{
				//Console.WriteLine(filename + " doesn't exist");
			}
			
			// We are now in memory and we were involved in Page Fault
			winner.isValid = true;
			winner.pageFaults++;
		}

		/// <summary>
		/// For statistical purposes only.  
		/// Total up how many times this Process has been involved in a Page Fault
		/// </summary>
		/// <param name="p">The Process to total</param>
		/// <returns>number of Page Faults</returns>
		public uint PageFaultsForProcess(Process p)
		{
			uint totalPageFaults = 0;
			foreach (MemoryPage page in _pageTable)
			{
				if (page.pidOwner == p.PCB.pid)
				{
					totalPageFaults += page.pageFaults;
				}
			}
			return totalPageFaults;
		}
	}

	/// <summary>
	/// Memory Protection: MemoryExceptions are constructed and thrown 
	/// when a <see cref="Process"/> accessed memory that doesn't belong to it.
	/// </summary>
	public class MemoryException : Exception
	{
		/// <summary>
		/// Process ID
		/// </summary>
		public uint pid = 0;
		/// <summary>
		/// Process address in question
		/// </summary>
		public uint processAddress = 0;

		/// <summary>
		/// Public Constructor for a Memory Exception
		/// </summary>
		/// <param name="pidIn">Process ID</param>
		/// <param name="addrIn">Process address</param>
		public MemoryException(uint pidIn, uint addrIn)
		{
			pid = pidIn;
			processAddress = addrIn;			
		}

		/// <summary>
		/// Pretty printing for MemoryExceptions
		/// </summary>
		/// <returns>Formatted string about the MemoryException</returns>
		public override string ToString()
		{
			return String.Format("Process {0} tried to access memory at address {1} and will be terminated! ",pid, processAddress);
		}
	}

	/// <summary>
	/// Memory Protection: MemoryExceptions are constructed and thrown 
	/// when a <see cref="Process"/> accessed memory that doesn't belong to it.
	/// </summary>
	public class StackException : Exception
	{
		/// <summary>
		/// Process ID
		/// </summary>
		public uint pid = 0;
		/// <summary>
		/// Num of Bytes more than the stack could handle
		/// </summary>
		public uint tooManyBytes = 0;

		/// <summary>
		/// Public Constructor for a Memory Exception
		/// </summary>
		/// <param name="pidIn">Process ID</param>
		/// <param name="tooManyBytesIn">Process address</param>
		public StackException(uint pidIn, uint tooManyBytesIn)
		{
			pid = pidIn;
			tooManyBytes = tooManyBytesIn;			
		}

		/// <summary>
		/// Pretty printing for MemoryExceptions
		/// </summary>
		/// <returns>Formatted string about the MemoryException</returns>
		public override string ToString()
		{
			return String.Format("Process {0} tried to push {1} too many bytes on to the stack and will be terminated! ",pid, tooManyBytes);
		}
	}

	/// <summary>
	/// Memory Protection: MemoryExceptions are constructed and thrown 
	/// when a <see cref="Process"/> accessed memory that doesn't belong to it.
	/// </summary>
	public class HeapException : Exception
	{
		/// <summary>
		/// Process ID
		/// </summary>
		public uint pid = 0;
		/// <summary>
		/// Num of Bytes more than the stack could handle
		/// </summary>
		public uint tooManyBytes = 0;

		/// <summary>
		/// Public Constructor for a Memory Exception
		/// </summary>
		/// <param name="pidIn">Process ID</param>
		/// <param name="tooManyBytesIn">Process address</param>
		public HeapException(uint pidIn, uint tooManyBytesIn)
		{
			pid = pidIn;
			tooManyBytes = tooManyBytesIn;			
		}

		/// <summary>
		/// Pretty printing for MemoryExceptions
		/// </summary>
		/// <returns>Formatted string about the MemoryException</returns>
		public override string ToString()
		{
			return String.Format("Process {0} tried to alloc {1} bytes more from the heap than were free and will be terminated! ",pid, tooManyBytes);
		}
	}


}
