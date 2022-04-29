# Memory Snapshot Analysis

Use [ClrMd](https://github.com/microsoft/clrmd) to analyse crash dumps through .NET code, rather than having to rely on WinDbg and expertise in its commands.

Analysis includes:

1. Run time summary (CLR version, whether GC is server mode, what the target platform is, etc)
1. Heap analysis (what types occupy the most space in memory and how much space they require, the most common strings, the largest strings)
1. Thread analysis (all threads with information about whether the thread is still alive, the number of managed locks that the thread has currently entered, the stack trace and the current exception - if there is one)