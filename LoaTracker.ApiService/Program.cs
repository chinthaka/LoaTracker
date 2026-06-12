
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Queues;
using LoaTracker.ApiService;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton(ResolveQueueClient);
builder.Services.AddSingleton(ResolveTableClient);

//var credential = new DefaultAzureCredential();

// Aspire appends non-standard keys like ";QueueName=queues" or ";TableName=tables" to the
// injected Azurite connection string, which causes Azure SDK connection string parsing to fail.
static string StripAspireKeys(string raw, params string[] prefixes) =>
    string.Join(';', raw.Split(';').Where(p =>
        !string.IsNullOrEmpty(p) && !prefixes.Any(k => p.StartsWith(k, StringComparison.OrdinalIgnoreCase))));

static QueueServiceClient ResolveQueueClient(IServiceProvider sp)
{
    var raw = sp.GetRequiredService<IConfiguration>().GetConnectionString("queues") ?? "";
    if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        return new QueueServiceClient(uri, new Azure.Identity.DefaultAzureCredential());
    return new QueueServiceClient(StripAspireKeys(raw, "QueueName="));
}

static TableServiceClient ResolveTableClient(IServiceProvider sp)
{
    var raw = sp.GetRequiredService<IConfiguration>().GetConnectionString("tables") ?? "";
    if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        return new TableServiceClient(uri, new Azure.Identity.DefaultAzureCredential());
    return new TableServiceClient(StripAspireKeys(raw, "TableName="));
}

builder.Services.AddLogging(logging =>
{
	logging.AddJsonConsole(opts =>
	{
		opts.JsonWriterOptions = new() { Indented = false };
		opts.IncludeScopes = true;
	});
});


var app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapLoaEndpoints();

app.Run();
