# Memory Snapshot Analysis

Use [ClrMd](https://github.com/microsoft/clrmd) to analyse crash dumps through .NET code, rather than having to rely on WinDbg and expertise in its commands.

Analysis includes:

1. Run time summary (CLR version, whether GC is server mode, what the target platform is, etc)
1. Heap analysis (what types occupy the most space in memory and how much space they require, the most common strings, the largest strings)
1. Paused managed methods (shows where threads are paused, such as in a `ManualResetEventSlim.Wait` call - see the second screen shot below for an example that highlights where `SqlClient` requests are being completed synchronously within an async call stack, which is wasteful; `System.Data.SqlClient.TdsParserStateObjectNative.ReadSyncOverAsync`)
1. Thread analysis (all threads with information about whether the thread is still alive, the number of managed locks that the thread has currently entered, the stack trace and the current exception - if there is one)

## Running the analysis

You can produce reports that are either plain text (written directly to the console) or that are html. The html reports are rendered in a page that shrinks the tables initially, to make it easier to see what is available at a glance (the see the table content in full, click the "Expand" link above it).

There are separate projects for these two output methods; the `Console` and `Web` projects. They both need to be provided a path to the dump file - with the `Console` project, it reads it from the command line and the `Web` project looks for a `path` query string value.

## Example output

![The initial web view](/Docs/InitialView.jpg)

![Paused managed methods (ordered by thread occurrence count)](/Docs/PausedManagedMethods.jpg)

![Managed threads with exceptions](/Docs/ManagedThreadsWithExceptions.jpg)

![The largest items on the Large Object Heap](/Docs/LargestLOHEntries.jpg)