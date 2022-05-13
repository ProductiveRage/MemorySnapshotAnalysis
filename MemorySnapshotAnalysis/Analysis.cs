using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Web;
using Microsoft.Diagnostics.Runtime;

namespace MemorySnapshotAnalysis
{
	public static class Analysis
	{
		public static string GenerateSummaryHtml(string path)
		{
			var contentBuilder = new StringBuilder();
			Generate(path, content => contentBuilder.AppendLine(content), html: true);
			return contentBuilder.ToString();
		}

		public static void WriteSummaryToConsole(string path) =>
			Generate(path, Console.WriteLine, html: false);

		private static void Generate(string path, Action<string> writeTo, bool html)
		{
			var timer = Stopwatch.StartNew();

			WriteLine(writeTo, html, $"[{timer.Elapsed.TotalSeconds:0.00}s] Loading: {path}");
			using (var target = DataTarget.LoadDump(path))
			{
				WriteLine(writeTo, html, $"[{timer.Elapsed.TotalSeconds:0.00}s] Loaded");
				WriteLine(writeTo, html);

				var runtime = target.ClrVersions.First().CreateRuntime();

				var retrievals = new Action[]
				{
					() => PrintRuntimeInfo(runtime, writeTo, html),
					() => PrintMemoryRegionInfo(runtime, writeTo, html),
					() => PrintHeapAnalysis(runtime, writeTo, html),
					() => PrintPausedMethods(runtime, writeTo, html),
					() => PrintThreadAnalysis(runtime, writeTo, html, onlyIfGotException: false),
					() => PrintThreadAnalysis(runtime, writeTo, html, onlyIfGotException: true)
				};

				foreach (var retrieval in retrievals)
				{
					retrieval();
					WriteLine(writeTo, html, $"^ Done after {timer.Elapsed.TotalSeconds:0.00}s");
					WriteLine(writeTo, html);
				}
			}

			WriteLine(writeTo, html, $"[{timer.Elapsed.TotalSeconds:0.00}s] Done!");
		}

		static void PrintRuntimeInfo(ClrRuntime runtime, Action<string> writeTo, bool html) =>
			new
			{
				Version = runtime.ClrInfo.Version.ToString(),
				ServerGC = runtime.Heap.IsServer ? "Yes" : "No",
				runtime.DataTarget!.DataReader.Architecture,
				TargetPlatform = runtime.DataTarget!.DataReader.TargetPlatform.ToString(),
				Bitness = runtime.DataTarget!.DataReader.PointerSize == 8 ? "x64" : "x86",
				AppDomainCount = runtime.AppDomains.Length,
				runtime.AppDomains,
				Threads = runtime.Threads.Length,
				Heaps = runtime.Heap.LogicalHeapCount,
				Modules = runtime.EnumerateModules()
			}
			.Dump("Runtime Info", writeTo, html);

		static void PrintMemoryRegionInfo(ClrRuntime runtime, Action<string> writeTo, bool html) =>
			runtime.Heap.Segments
				.GroupBy(segment => segment.LogicalHeap)
				.OrderBy(group => group.Key)
				.Select(group => new { Heap = group.Key, Size = group.Sum(segment => (uint)segment.Length) })
				.ToArray()
				.DumpTable("Memory Region Information", writeTo, html);

		static void PrintHeapAnalysis(ClrRuntime runtime, Action<string> writeTo, bool html)
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
				var name = type.Name;
				if (name is not null) // Note: Some types (eg. ones created at runtime by something like JIL) won't have names - skip these
				{
					if (typeCounts.TryGetValue(name, out var t))
					{
						typeCounts[name] = (t.Bytes + size, t.Count + 1);
					}
					else
					{
						typeCounts[name] = (size, 1);
					}
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
				.DumpTable("Top 100 Types (By Size)", writeTo, html);

			WriteLine(writeTo, html);
			typeCounts
				.OrderByDescending(w => w.Value.Count)
				.Select(w => new { w.Value.Count, w.Value.Bytes, Type = w.Key })
				.Take(100)
				.DumpTable("Top 100 Types (By Count)", writeTo, html);

			WriteLine(writeTo, html);
			stringCounts
				.OrderByDescending(w => w.Value)
				.Select(w => new { Count = w.Value, Value = w.Key })
				.Take(100)
				.DumpTable("Top 100 Most Common Strings", writeTo, html);

			WriteLine(writeTo, html);
			stringCounts
				.OrderByDescending(w => w.Key.Length)
				.Select(w => new { Count = w.Value, Size = w.Key.Length.ToString("n0"), Value = w.Key.Length > 10000 ? w.Key[..10000] + "..." : w.Key })
				.Take(100)
				.DumpTable("1000 Largest Strings", writeTo, html);

			WriteLine(writeTo, html);
			$"Overall {stringObjectCounter:N0} \"System.String\" objects take up {totalStringObjectSize:N0} bytes ({(totalStringObjectSize / 1024.0 / 1024.0):N2} MB)"
				.Dump("Total String Storage Space", writeTo, html);

			WriteLine(writeTo, html);
			heap.Segments
				.Where(segment => segment.IsLargeObjectSegment)
				.SelectMany(segment => segment.EnumerateObjects())
				.Select(obj => (obj.Type is null) || (obj.Type == heap.FreeType)
					? default
					: (Display: obj.Type == heap.StringType ? obj.AsString() : obj.Type.ToString(), obj.Size))
				.Where(obj => obj != default)
				.OrderByDescending(obj => obj.Size)
				.Take(100)
				.DumpTable("Top 100 Largest LOH Entries", writeTo, html);
		}

		static void PrintPausedMethods(ClrRuntime runtime, Action<string> writeTo, bool html) =>
			runtime.Threads
				.Select(thread => thread.EnumerateStackTrace()
					.Select(frame => frame.Method?.Signature)
					.FirstOrDefault(signature => signature is not null))
				.Where(signature => signature is not null)
				.GroupBy(signature => signature)
				.Select(group => new { Count = group.Count(), Signature = group.Key })
				.OrderByDescending(signature => signature.Count)
				.DumpTable("Paused Managed Methods", writeTo, html);

		static void PrintThreadAnalysis(ClrRuntime runtime, Action<string> writeTo, bool html, bool onlyIfGotException) =>
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
				.Dump($"{(onlyIfGotException ? "" : "All ")}Managed Threads{(onlyIfGotException ? " with Exceptions" : "")}", writeTo, html);

		private static void WriteLine(Action<string> writeTo, bool html, string content = "")
		{
			if (html)
			{
				content = content.Length == 0
					? "<br>"
					: $"<p>{HttpUtility.HtmlEncode(content)}</p>";
			}

			writeTo(content);
		}

		private static void Dump(this object value, string title, Action<string> writeTo, bool html)
		{
			if ((value is IEnumerable enumerable) && !enumerable.GetEnumerator().MoveNext())
			{
				if (html)
				{
					"None Available".DumpHtml(title, writeTo: writeTo);
				}
				else
				{
					"None Available".Dump(title, writeTo: writeTo);
				}
			}
			else
			{
				if (html)
				{
					value.DumpHtml(title, writeTo: writeTo);
				}
				else
				{
					value.Dump(title, writeTo: writeTo);
				}
			}
		}

		private static void DumpTable<T>(this IEnumerable<T> values, string title, Action<string> writeTo, bool html)
		{
			if (html)
			{
				values.DumpHtmlTable(title, writeTo: writeTo);
			}
			else
			{
				if (values.Any())
				{
					values.Dump(title, writeTo: writeTo);
				}
				else
				{
					"None Available".Dump(title, writeTo: writeTo);
				}
			}
		}
	}
}