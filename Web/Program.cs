using MemorySnapshotAnalysis;

const string htmlTemplate = @"<!DOCTYPE html>
<html>
<head>
<title>Memory Snapshot Analysis</title>
<link rel=""stylesheet"" href=""/site.css"">
</head>
<body>
<h1>Memory Snapshot Analysis</h1>
{0}
</body>
<script src=""/site.js""></script>
</html>";

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", async context =>
{
	var path = context.Request.Query["path"].FirstOrDefault();
	if (string.IsNullOrWhiteSpace(path))
    {
		await context.Response.WriteAsync("No 'path' specified in Query String");
		return;
    }

	context.Response.ContentType = "text/html";

	await context.Response.WriteAsync(
		string.Format(
			htmlTemplate,
			Analysis.GenerateSummaryHtml(path)));
});

app.Run();