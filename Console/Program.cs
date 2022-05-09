using MemorySnapshotAnalysis;

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

Analysis.WriteSummaryToConsole(path);

Console.WriteLine();
Console.WriteLine("Press [Enter] to terminate..");
Console.ReadLine();