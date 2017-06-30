# CST 352 Operating systems final project spec

The goal of this project is to write a virtual operating system for an abstract machine to provide the following services. The details of the abstract machine will be provided below.

- Load and unload programs.
- Allocate and deallocate virtual memory.
- Provide I/O services.
  - These will just be standard I/o input.
- Provide services for scheduling processes.
  - Based on priority.
  - Based on time cycles expiring.
  - Based on resources.
- Provide services for mutual exclusion and synchronization.
  - Semaphores.
  - Locks( mutexes ).
  - Events.

## Changes made on 4/8/2002

The following features are mandatory. Mandatory implies these features must be implemented and fully functional. If some or all of the features are not working as specified, the grade is left to the instructor&#39;s subjective decisions. This feature set is called the BASE FEATURE SET in this document.

- Process scheduling based on priority.
  - Process scheduling based on time quantum expiration is optional.
  - This implies that the current running process continues to run until it exits or tries to acquire a resource that is currently held by another process.
  - The functionality for Sleep is mandatory.
- Provide output services. (Print. This is used to verify the functionality of the program ).
- Provide mutual exclusion facilities. Minimally locks must be implemented.
- An idle process is required. This process continues to run until time elapses for another process to wake up.
- On a process exit.
  - All the memory that still belongs to the process MUST be clean deallocated and MUST be made available for other processes.
  - All internal OS structures used for the process(PCB..) must be released as well.
  - If the process holds locks when it exits, the lock MUST be marked as not acquired.
    - This implies any process waiting for this lock MUST now be made eligible to run.
- These  features, if implemented completely, account for 65% of the total grade for the class.

## Changes made on 05/06/2002

The following features may be implemented, in addition to the above, for an additional 35% grade. This implies that if the above mandatory features and these features are implemented completely and verified to be functionally complete, the student SHALL receive the full grade of 100% for the class. The mandatory features are the minimum set of features needed. Students are strongly advised to get their mandatory features working before even venturing into these features. Students MUST clearly indicate at the time of submission what features they implemented. This feature set is KNOWN AS THE EXTENDED FEATURE SET in this document.

- Implement dynamic memory allocation features.
  - Alloc and FreeMemory MUST be implemented.
- Implement virtual memory using dynamic paging.
  - Each process&#39;s memory is now classified as virtual memory.
  - Each process has its own set of page tables that map its virtual memory to physical memory.
  - A process may incur page faults when it tries to access memory it currently does not own or does not have in memory.
  - Page size for the page tables can be set to 4 bytes or taken as an input to the OS command line arguments.
  - Normal data files may be used to store the unused state of a process.
  - Each process may have its own file for paging or may share a file.
  - OS code must service page faults and handle them properly.
  - Page tables can either grow in size or be restricted to a fixed size.
    - The former is preferable although both will be considered as good.

The following are the list of opcodes that are minimally needed for the above features. The instructor SHALL use only these opcodes in his sample programs in order to test students&#39; projects. It is left up to the students to choose additional opcodes to implement if they find them interesting.

## Opcodes

| Opcode | BASE FEATURE SET | EXTENDED FEATURE SET |
| --- | --- | --- |
| Incr | x | x |
| Addi | x | x |
| movmr | x | x |
| movmm | x | x |
| movrm | x | x |
| printr | x | x |
| printm | x | x |
| AcquireLock | x | x |
| ReleaseLock | x | x |
| Alloc |   | x |
| FreeMemory |   | x |
| Sleep | x | x |
| MemoryClear |   | x |
| Exit | x | x |
| movi | x | x |
| movr | x | x |

The project will implement a virtual operating system. The term virtual in this context implies an operating system that will run on top of a standard platform.

- Either a standard PC with Windows or Linux platform is sufficient.
- Any standard C/C++/Java compiler may be used to implement the operating system.
- Since there is no real hardware here, the operating system provides the above services + executes the programs themselves.
- In this sense, the operating system is both the system and the hardware , all in one. It interprets the programs by itself( similar to the Java virtual machine).

## Definition of the abstract machine.

The machine used for this project will be an abstract one. Abstract implies there is no real hardware (CPU+ chipset + hardwired logic) needed to implement this. Instead, it will be emulated in software. The machine has the following details. The abstract machine is a 32-bit machine which implies all addresses are 32-bits and registers are 32-bits as well.

- It has 10 general purpose registers.
  - Each register is 32-bits wide and can store any information.
  - They are addressed as 1 through 10 in the opcodes for this machine.
  - Register 10 is also the stack pointer register.
    - Always contains the top of the stack.
    - Grows towards lower addresses.
    - Pushing decrements stack pointer. Popping increments it.
    - Only 32-bit quantities may be pushed or popped.
    - Only the full-registers may be addressed.
      - This is unlike x86 where a 32-bit register may be addressed by its full 32-bit , 16-bit or 8-bit portions.
- The abstract machine has 2 bit flag registers.
  - SF is the sign flag. This is set when any comparison of 2 quantities results in a sign extension.
  - ZF is the sign flag. This is set when any comparison of 2 quantities results in a zero( both quantities are equal ).
- The instruction pointer register is special and is 32-bits wide.
  - Not accessible to the programmer.
  - Modified when the program executes.
  - Must be saved by the operating system on function calls and context switches.
  - Also known as eip in this document.
- The abstract machine has the following opcodes. Since the intent of this project is to teach Operating system design and implementation, the machine is not complete in the sense that it does not provide a full repertoire of instructions (similar to x86 or other architectures). Each opcode is exactly 1 byte long and is of the following format.
  - &lt;opcode&gt; &lt;argument 1&gt; &lt;argument 2&gt; ..
  - All instructions are multiples of bytes.
  - All instructions take exactly one clock cycle to execute, independent of the actual operation of the instruction.
  - Context-switches only happen on instruction boundaries.
  - Constants are indicated by the operator $ in front of them.
  - Registers are indicated by r1 … r10.
  - The stack pointer (register 10) has another name for it which is sp.

| Opcode | Value(decimal) | Format |
| --- | --- | --- |
| Incr | 1 | incr r1(increment value of register 1 by 1 ). |
| Addi | 2 | addi  r1,$1 is the same as incr r1 |
| addr | 3 | Addr r1, r2( r1 &lt;= r1 + r2 ). |
| Pushr | 4 | Pushr rx (pushes contents of register x onto stack. Decrements sp by 4 ). |
| Pushi | 5 | Pushi $x . pushes the constant x onto stack. Sp is decremented by 4 after push. |
| Movi | 6 | Movi rx, $y. rx &lt;= y |
| Movr | 7 | Movr rx, ry ; rx &lt;= ry |
| Movmr | 8 | Movmr rx, ry ; rx &lt;= [ry] |
| Movrm | 9 | Movrm rx,ry; [rx] &lt;= ry |
| Movmm | 10 | Movmm rx, ry [rx] &lt;= [ry] |
| Printr | 11 | Printr r1 ; displays contents of register 1 |
| Printm | 12 | Printm r1; displays contents of memory whose address is in register 1. |
| Jmp | 13 | Jmp r1; control transfers to the instruction whose address is r1 bytes relative to the current instruction. R1 may be negative. |
| Cmpi | 14 | Cmpi rx, $y;  subtract y from register rx. If rx &lt; y, set sign flag. If rx &gt; y, clear sign flag. If rx == y , set zero flag. |
| Cmpr | 15 | The same as cmpi except now both operands are registers. |
| Jlt | 16 | Jlt rx; if the sign flag is set, jump to the instruction whose offset is rx bytes from the current instruction. |
| Jgt | 17 | Jgt rx; if the sign flag is clear, jump to the instruction whose offset is rx bytes from the current instruction |
| Je | 18 | Je rx; if the zero flag is clear, jump to the instruction whose offset is rx bytes from the current instruction. |
| Call | 19 | Call r1; call the procedure at offset r1 bytes from the current instruction. The address of the next instruction to execute after a return is pushed on the stack. |
| Callm | 20 | Callm r1; call the procedure at offset [r1] bytes from the current instruction. The address of the next instruction to execute after a return is pushed on the stack. |
| Ret | 21 | Pop the return address from the stack and transfer control to this instruction. |
| Alloc | 22 | Alloc r1, r2; allocate memory of size equal to r1 bytes and return the address of the new memory in r2. If failed, r2 is cleared to 0. |
| AcquireLock | 23 | AcquireLock r1; Acquire the operating system lock whose # is provided in the register r1. If the lock is invalid, the instruction is a no-op. |
| ReleaseLock | 24 | Releaselock r1; release the operating system lock whose # is provided in the register r1; if the lock is not held by the current process, the instruction is a no-op. |
| Sleep | 25 | Sleep r1; Sleep the # of clock cycles as indicated in r1. Another process or the idle process must be scheduled at this point. If the time to sleep is 0, the process sleeps infinitely. |
| SetPriority | 26 | SetPriority r1; Set the priority of the current process to the value in register r1; See priorities discussion in Operating system design |
| Exit | 27 | Exit. This opcode is executed by a process to exit and be unloaded. Another process or the idle process must now be scheduled. |
| FreeMemory | 28 | FreeMemory r1; Free the memory allocated whose address is in r1. |
| MapSharedMem | 29 | MapSharedMem r1, r2; Map the shared memory region identified by r1 and return the start address in r2. |
| SignalEvent | 30 | SignalEvent r1; Signal the event indicated by the value in register r1. |
| WaitEvent | 31 | WaitEvent r1; Wait for the event in register r1 to be triggered. This results in context-switches happening. |
| Input | 32 | Input r1; read the next 32-bit value into register r1. |
| MemoryClear | 33 | MemoryClear r1, r2; set the bytes starting at address r1 of length r2 bytes to zero. |
| TerminateProcess | 34 | TerminateProcess r1; terminate the process whose id is in the register r1. |
| Popr | 35 | pop the contents at the top of the stack into register rx which is the operand. Stack pointer is decremented by 4. |
| popm | 36 | pop the contents at the top of the stack into the memory operand whose address is in the register which is the operand. Stack pointer is decremented by 4. |

## Operating system design goals

The operating system designed in this project must provide the following services to programs written for this operating system.

- Provide virtual memory services.
  - All processes will have page tables allocated by the operating system.
  - OS will detect invalid memory accesses and terminate the processes.
    - Terminating processes means the operating system will display an error message on the console and choose the next process to execute.
  - Processes can allocate any amount of memory subject to the resources available.
  - Processes only manipulate virtual addresses.
- Provide inter-process synchronization mechanisms.
  - OS provides mutexes for a process to lock out other processes while manipulating critical shared structures.
  - For this project, the OS comes with (or is preconfigured with) 10 locks, each identified by the numbers 1 through 10.
  - The OS does not provide any services for processes to allocate their own critical sections.
  - The instructions AcquireLock and ReleaseLock allow a process to acquire and release the OS provided locks.
  - The locks provided by the OS are meant for use by application processes to protect themselves while manipulating shared-critical structures.
- Provide scheduling services.
  - Each process is scheduled independently.
  - Processes are single-threaded.
  - Only one process is running at a time.
  - A process once scheduled(means it starts running) continues to run until the following happens.
    - It exits( by executing opcode Exit ).
    - It sleeps for some time( by executing opcode Sleep ).
    - It acquires a lock already acquired by someone else.
    - It accesses an address which requires a page fault and there is not enough memory available. Once another process frees up some memory, this process is scheduled again.
    - Waits on an event to be signaled by another process.
    - Its time quantum runs out.
      - Each process has a time quantum. A time quantum is the # of clock cycles it is allowed to run before it is scheduled out.
- Provide memory-mapping services.
  - This OS provides built-in or preconfigured shared memory regions of size 10000 bytes each for a total of 10 memory regions, each indexed by the number 1 thru 10 respectively.
  - Processes may map any of them into their address spaces by using the opcode MapSharedMem and passing the # of the shared memory region.
  - Processes may use this facility to share memory and perform inter-process communication.
- Provide I/O services.
  - Processes may use these I/O services to print out values to the console.
  - Processes may use input services to read values from the console as well.

## Project requirements

Where the word MUST is used, the features are required in order for the student to receive full grade for the project. The word OS is used to describe the implementation of this operating system. Where the word MAY is used, the feature is optional.

- Students MAY use C/C++ or Java to write the operating system.
  - If C/C++, students MUST use Visual C++ 5.0 or 6.0.
  - If Java, the code must be compilable using Sun&#39;s JDK 2.0 or greater.
- The OS must be compilable without any errors.
  - Any errors in compilation results in 0 grade.
  - The students are responsible for providing any readme or any other documentation to the instruction in order to evaluate the project.
  - The program must be compilable on windows platforms.
  - Warnings MUST be minimized.
  - If the operating system code crashes, the grade provided is based on the instructor&#39;s subjective decisions.
  - The instructor will evaluate the project using sample files that he will provide later in the class.
- The team ( of 2 students ) is graded as a whole for the project.
  - Any differences between the team members( if any ) must be resolved by the members themselves.
  - It is strongly advised that students team up for this project.

## Project features

- The OS must contain an interpreter that understands the above opcodes.
- The OS must be invoked as follows.
  - OS &lt;# of bytes&gt; &lt;program1.txt&gt; &lt;program2.txt&gt; …..
  - Each programx.txt contains the code for the programs to be loaded and executed.
  - The number of bytes for all programs that can be loaded(including code, data, heap and stack) must be provided in the first parameter.
  - This # does not include the # of bytes the OS will itself need to keep track of information about each process such as process blocks.
- The OS will provide the following services.
  - Loader to load programs of the above type.
    - The students may assume that the instructor will provide programs with correct opcodes.
      - This is the not the same as rogue programs.
  - Scheduler for scheduling programs.
    - Maintain process context(using process context blocks or otherwise ).
    - The time quantum a process is limited to running before it is scheduled out is 10 clock cycles.
    - All process context (including all registers, page tables) MUST be stored in the process context block.
    - An idle process must be provided that runs if no other process is eligible to run.
      - The idle process will be provided in a separate file called idle.txt.
      - The idle process just sits in a tight loop printing out the value 20.
      - The idle process only has a quantum of 5 clock cycles.
      - The idle process never exits.
      - The scheduler always schedules the highest-priority process eligible to run.
      - There are 32-priorities in the OS.
      - Processes with higher priorities run until they give up control, exit or are terminated.
      - The bigger the priority #, the higher the priority for the process.
      - A process may adjust its own priority by invoking the opcode SetPriority.
      - In addition to these queues, the scheduler MUST implement queues for blocking &amp; other types of processes.
- Virtual memory manager.
  - Each process MUST only access virtual addresses.
  - When a process is loaded, the memory manager MUST construct its page tables in the appropriate fashion.
    - Every memory access by each process goes through page tables and accesses the real memory.
    - This is true for all addresses(code, data, stack and heap).
    - The details of breaking down a virtual address to a physical address using page tables will be provided in the class.
    - If a process accesses an address it does not own, the OS must print an error message with the values of the registers of the process at the time the problem occurred, terminate the process and schedule another one.
  - Processes sharing memory.
    - A process may map the OS provided shared memory region into its own address space using the MapSharedMem opcode.
    - This allows 2 or more processes to share memory.
    - The exact usage of these memory regions is left to the processes themselves.
  - Process virtual memory.
    - A process&#39;s memory map is divided into 4 regions.
      - Code. This is the region containing the instructions with some combination of the above opcodes.
      - Stack. This is the region where the program may store temporary variables. When a function is called, the interpreter stores the return address here as well.
        - When a program is loaded, it is provided a stack of 4k bytes.
        - The stack grows and shrinks depending on usage.
      - Global data.
        - When a program is loaded, it is provided a memory region called global data of size 4k bytes. The contents are initialized to 0. Please see &quot;process initial state&quot; for more information.
      - Heap.
        - Heap is the portion of the address space that the process can allocate dynamically using the Alloc and FreeMemory opcodes.
  - Process initial state.
    - A program becomes a process when it is loaded by the operating system.
    - When a process is created, its registers have the following state.
      - It is the responsibility of the operating system to set it up this way.
      - R1 – r7 are undefined.
      - Register r8 contains the id of the process.
      - R9 contains the virtual address of the global data region( of length 4k bytes ).
      - R10 (sp) is set to the top of the stack.
        - Growing the stack implies it grows towards lower addresses.
      -  The instruction pointer(eip) contains the value 0 essentially translating(via page tables) to the first instruction in the program to be executed.
  - Process exiting.
    - A process may exit due to any of the following reasons.
      - It invokes the Exit opcode.
      - It performs an illegal operation(it access a memory location it does not own).
      - Another process kills this process using the TerminateProcess opcode.
        - It is left to the individual processes to find the id of the other processes.
        - Admittedly, the security is very weak(allowing any process to terminate any other process).
    - When a process exits for whatever reason, your OS must perform the following.
      - Display the # of page faults, the # of context switches it went through, the # of clock cycles it consumed.
  - Process synchronization.
    - The OS provides 10 mutexes or locks.
    - The locks are numbered through 1 to 10.
    - A process acquires the lock using AcquireLock.
      - If another process already owns the lock, the current process is put to sleep on the wait queue.
      - If a process X tries to acquire the same lock Y two or more times, it is a no-operation. In short, the process MUST not deadlock against itself.
    - A processes releases a lock it is currently holding via ReleaseLock.
      - This MUST make only one process(the highest priority process currently waiting) eligible to be scheduled.
      - This implies that other processes also blocking for this lock will not be scheduled. Only the highest priority process will be scheduled(if any).
      - The process releasing the lock might be scheduled out because the process now eligible to run may have a higher priority than the one releasing the lock.
    - The OS must take into account priority inversion.
      - It is possible that a higher-priority process is blocked for a resource that is currently owned by a lower-priority process.
      - In this case, the OS must bump up the priority of the lower priority process to that of the blocked process and keep it this way until the lower-priority process releases the resources.
    - If a process holding a lock exits or is terminated for whatever reason,
      - The lock MUST be considered not held and another process waiting(if any) should be made eligible for scheduling.
  - Events.
    - Events, unlike locks, are provided for process synchronization.
    - They allow one process to notify another process the occurrence of an event.
      - The OS provides built-in events for a total of 10. They are numbered 1 to 10.
      - Any event is in one of 2 states.
        - Signaled. This implies any process waiting on it is eligible to run now.
          - An event is set to signaled when a process invokes the SignalEvent opcode.
        - Non-signaled.
          - This is the initial state of all events when the OS boots up(so to speak).
          - When a process that is waiting on it becomes eligible to run, the event becomes non-signaled allowing another process to wait on it.
          - There is no concept of ownership of an event.
