using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LoaTracker.ApiService;

public static class LoaApi
{
	public static IEndpointRouteBuilder MapLoaEndpoints(this IEndpointRouteBuilder app)
	{
		var loa = app.MapGroup("/loa").WithTags("Loa");

		loa.MapPost("/", AddLoaRequest).WithName("SubmitLoaRequest");
		loa.MapGet("/{trackingId}", GetLoaStatus).WithName("GetLoaStatus");

		return app;
	}


	internal static async Task<IResult> AddLoaRequest(
		LoaRequest request,
		QueueServiceClient queueClient,
		TableServiceClient tableClient,
		ILogger logger,
		CancellationToken cancellationToken)
	{

		if (string.IsNullOrWhiteSpace(request.MemeberEmail) ||
			string.IsNullOrWhiteSpace(request.AdviserName))
		{
			logger.LogWarning("Invalid request received: {Request}", request);

			return Results.ValidationProblem(new Dictionary<string, string[]>
			{
				["request"] = ["MemberEmail and AdviserName are required"]
			});
		}

		using var scope = logger.BeginScope(new Dictionary<string, object>
		{
			["AdviserName"] = request.AdviserName,
			["RequestType"] = "LoaSubmission"
		});

		IResult result;

		try
		{

			var trackingId = Guid.CreateVersion7().ToString("N");

			logger.LogInformation("LoA request received for adviser {Adviser}", request.AdviserName);

			var table = tableClient.GetTableClient("LoaAudit");
			await table.CreateIfNotExistsAsync(cancellationToken);
			await table.AddEntityAsync(new LoaAutidEntity
			{
				PartitionKey = DateTime.UtcNow.ToString("yyyy-MM-dd"),
				RowKey = trackingId,
				Status = "Received",
				AdviserName = request.AdviserName,
				MememberEmail = request.MemeberEmail,
				SchemeName = request.SchemeName ?? "(unspecified)"
			}, cancellationToken: cancellationToken);

			var queue = queueClient.GetQueueClient("loa-request");
			await queue.CreateIfNotExistsAsync();

			var payload = JsonSerializer.Serialize(new
			{
				TrackingId = trackingId,
				MemberEmail = request.MemeberEmail,
				AdviserName = request.AdviserName,
				SchemeName = request.SchemeName
			});
			await queue.SendMessageAsync(payload, cancellationToken);

			result = Results.Accepted($"/loa/{trackingId}", new { trackingId, status = "Received" });

			logger.LogInformation("LoA request {TrackingId} queued successfully", trackingId);

		}
		catch (Exception e)
		{
			result = Results.BadRequest("Please try again later");
			logger.LogError(e, "Error submitting LoA request");
		}

		return result;
	}

	internal static async Task<IResult> GetLoaStatus(
		string trackingId,
		TableServiceClient tableClient,
		CancellationToken cancellationToken)
	{
		var table = tableClient.GetTableClient("LoaAudit");
		var results = table.QueryAsync<LoaAutidEntity>(e => e.RowKey == trackingId, null, null, cancellationToken).OrderBy(e => e.Timestamp);

		var events = new List<LoaAutidEntity>();

		await foreach (var entity in results)
		{
			events.Add(entity);
		}

		if (events.Count == 0)
		{
			return Results.NotFound();
		}

		return Results.Ok(new
		{
			trackingId,
			currentStatus = events[^1].Status,
			history = events
		});
	}

	public record LoaRequest(string AdviserName, string MemeberEmail, string? SchemeName);
}
