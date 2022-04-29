using System.Diagnostics;
using ClrMdTest;
using Microsoft.Diagnostics.Runtime;

var path = args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(path))
{
	Console.WriteLine("No file specified in the command line arguments");
	Environment.Exit(1);
}
if (!File.Exists(path))
{
	Console.WriteLine("Specified file path does not exist: " + path);
	Environment.Exit(1);
}

var timer = Stopwatch.StartNew();

Console.WriteLine($"[{timer.Elapsed.TotalSeconds:0.00}s] Loading: {path}");
using (var target = DataTarget.LoadDump(path))
{
	Console.WriteLine($"[{timer.Elapsed.TotalSeconds:0.00}s] Loaded");
	Console.WriteLine();

	var runtime = target.ClrVersions.First().CreateRuntime();

	var retrievals = new Action[]
    {
		() => PrintRuntimeInfo(runtime),
		() => PrintMemoryRegionInfo(runtime),
		() => PrintHeapAnalysis(runtime),
		() => PrintThreadAnalysis(runtime, onlyIfGotException: false),
		() => PrintThreadAnalysis(runtime, onlyIfGotException: true)
	};

	foreach (var retrieval in retrievals)
    {
		retrieval();
		Console.WriteLine($"^ Done after {timer.Elapsed.TotalSeconds:0.00}s");
		Console.WriteLine();
	}
}

Console.WriteLine($"[{timer.Elapsed.TotalSeconds:0.00}s] Done! Press [Enter] to terminate..");
Console.ReadLine();

static void PrintRuntimeInfo(ClrRuntime runtime) =>
	new
	{
		Version = runtime.ClrInfo.Version.ToString(),
		ServerGC = runtime.Heap.IsServer ? "Yes" : "No",
		runtime.DataTarget!.DataReader.Architecture,
		TargetPlatform = runtime.DataTarget!.DataReader.TargetPlatform.ToString(),
		Bitness = runtime.DataTarget!.DataReader.PointerSize == 8 ? "x64" : "x86",
		AppDomainCount = runtime.AppDomains.Length,
		runtime.AppDomains,
		Modules = runtime.EnumerateModules(),
		Threads = runtime.Threads.Length,
		Heaps = runtime.Heap.LogicalHeapCount
	}
	.Dump("Runtime Info");

static void PrintMemoryRegionInfo(ClrRuntime runtime)
{
	var segmentGroups = runtime.Heap.Segments
		.GroupBy(segment => segment.LogicalHeap)
		.OrderBy(group => group.Key)
		.Select(group => new { Heap = group.Key, Size = group.Sum(segment => (uint)segment.Length) })
		.ToArray();

	const string title = "Memory Region Information";
	if (segmentGroups.Length == 0)
	{
		Console.WriteLine($"{title}: None available");
	}
	else
    {
		segmentGroups.Dump(title);
    }
}

static void PrintHeapAnalysis(ClrRuntime runtime)
{
	var heap = runtime.Heap;

	ulong totalStringObjectSize = 0, stringObjectCounter = 0;
	var stringCounts = new Dictionary<string, ulong>();
	var typeCounts = new Dictionary<string, (ulong Bytes, ulong Count)>();
	foreach (var obj in heap.EnumerateObjects())
	{
		var type = obj.Type;
		if (type is null)
		{
			continue;
		}

		var size = obj.Size;
		var name = type.Name!;
		if (typeCounts.TryGetValue(name, out var t))
		{
			typeCounts[name] = (t.Bytes + size, t.Count + 1);
		}
		else
		{
			typeCounts[name] = (size, 1);
		}

		if (type.IsString)
		{
			stringObjectCounter++;
			var text = obj.AsString(maxLength: int.MaxValue);
			if (text is not null)
			{
				totalStringObjectSize += size;
				stringCounts[text] = stringCounts.TryGetValue(text, out var count) ? count + 1 : 1;
            }
		}
	}

	typeCounts
		.OrderByDescending(w => w.Value.Bytes)
		.Select(w => new { w.Value.Bytes, w.Value.Count, Type = w.Key })
		.Take(100)
		.Dump("Top 100 Types (By Size)");

	Console.WriteLine();
	typeCounts
		.OrderByDescending(w => w.Value.Count)
		.Select(w => new { w.Value.Count, w.Value.Bytes, Type = w.Key })
		.Take(100)
		.Dump("Top 100 Types (By Count)");

	Console.WriteLine();
	stringCounts
		.OrderByDescending(w => w.Value)
		.Select(w => new { Count = w.Value, Value = w.Key })
		.Take(100)
		.Dump("Top 100 Most Common Strings");

	Console.WriteLine();
	stringCounts
		.OrderByDescending(w => w.Key.Length)
		.Select(w => new { Count = w.Value, Size = w.Key.Length.ToString("n0"), Value = w.Key.Length > 10000 ? w.Key[..10000] + "..." : w.Key })
		.Take(100)
		.Dump("1000 Largest Strings");

	Console.WriteLine($"Overall {stringObjectCounter:N0} \"System.String\" objects take up {totalStringObjectSize:N0} bytes ({(totalStringObjectSize / 1024.0 / 1024.0):N2} MB)");
}

static void PrintThreadAnalysis(ClrRuntime runtime, bool onlyIfGotException) =>
	runtime.Threads
		.Where(t => t.EnumerateStackTrace().Any())
		.Where(t => (t.CurrentException is not null) || !onlyIfGotException)
		.Select(t => new
		{
			t.IsAlive,
			t.OSThreadId,
			t.ManagedThreadId,
			t.GCMode,
			t.IsFinalizer,
			t.LockCount,
			Address = t.Address.ToString("x"),
			Details = new
			{
				t.IsAbortRequested,
				t.IsAborted,
				t.IsGCSuspendPending,
				t.IsUserSuspended,
				t.IsDebugSuspended,
				t.IsBackground,
				t.IsUnstarted,
				t.IsCoInitialized,
				t.IsSTA,
				t.IsMTA
			},
			t.CurrentException,
			StackTrace = t.EnumerateStackTrace()
		})
		.Dump($"{(onlyIfGotException ? "" : "All ")}Managed Threads{(onlyIfGotException ? " with Exceptions" : "")}");