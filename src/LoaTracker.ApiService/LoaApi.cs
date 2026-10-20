using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using LoaTracker.ApiService.LoaAudit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LoaTracker.ApiService;

public static class LoaApi
{
	public sealed class LoaApiLog;
	const long MaxDocumentBytes = 10 * 1024 * 1024;
	const string DocumentContainer = "loa-documents";
	static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".pdf", ".png", ".jpg", ".jpeg"
	};
	public static IEndpointRouteBuilder MapLoaEndpoints(this IEndpointRouteBuilder app)
	{
		var loa = app.MapGroup("/loa").WithTags("Loa");

		loa.MapPost("/", AddLoaRequest).WithName("SubmitLoaRequest");
		loa.MapPost("/upload", AddLoaDocument).DisableAntiforgery().WithName("SubmitLoaDocument");
		loa.MapGet("/{trackingId}/document", DownloadLoaDocument).WithName("DownloadLoaDocument");
		loa.MapGet("/{trackingId}", GetLoaStatus).WithName("GetLoaStatus");
		loa.MapUpdateLoaAudit();

		return app;
	}


	internal static async Task<IResult> AddLoaRequest(
		LoaRequest request,
		QueueServiceClient queueClient,
		TableServiceClient tableClient,
		ILogger<LoaApiLog> logger,
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

			result = await PersistAndQueue(
				request.AdviserName,
				request.MemeberEmail,
				request.SchemeName,
				documentFileName: null,
				documentBlobName: null,
				queueClient,
				tableClient,
				logger,
				cancellationToken);

		}
		catch (Exception e)
		{
			result = Results.BadRequest("Please try again later");
			logger.LogError(e, "Error submitting LoA request");
		}

		return result;
	}

	internal static async Task<IResult> AddLoaDocument(
		HttpRequest request,
		BlobServiceClient blobClient,
		QueueServiceClient queueClient,
		TableServiceClient tableClient,
		ILogger<LoaApiLog> logger,
		CancellationToken cancellationToken)
	{
		if (!request.HasFormContentType)
		{
			return Results.ValidationProblem(new Dictionary<string, string[]>
			{
				["request"] = ["Submit the letter of authority as multipart form data."]
			});
		}

		var form = await request.ReadFormAsync(cancellationToken);
		var adviserName = form["AdviserName"].ToString();
		var memberEmail = form["MemeberEmail"].ToString();
		var schemeName = form["SchemeName"].ToString();
		var file = form.Files.GetFile("file");

		var errors = new Dictionary<string, string[]>();
		if (string.IsNullOrWhiteSpace(adviserName) || string.IsNullOrWhiteSpace(memberEmail))
		{
			errors["request"] = ["MemberEmail and AdviserName are required"];
		}

		if (file is null || file.Length == 0)
		{
			errors["file"] = ["A letter of authority file is required"];
		}
		else if (file.Length > MaxDocumentBytes)
		{
			errors["file"] = ["The file must be 10 MB or smaller"];
		}
		else if (!AllowedDocumentExtensions.Contains(Path.GetExtension(file.FileName)))
		{
			errors["file"] = ["Upload a PDF, PNG, or JPEG file"];
		}

		if (errors.Count > 0)
		{
			logger.LogWarning("Invalid LoA upload for adviser {Adviser}", adviserName);
			return Results.ValidationProblem(errors);
		}

		var trackingId = Guid.CreateVersion7().ToString("N");
		var safeName = SafeFileName(file!.FileName);
		var blobName = $"{trackingId}/{safeName}";

		try
		{
			var container = blobClient.GetBlobContainerClient(DocumentContainer);
			await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
			await using (var stream = file.OpenReadStream())
			{
				await container.GetBlobClient(blobName).UploadAsync(
					stream,
					new BlobUploadOptions
					{
						HttpHeaders = new BlobHttpHeaders { ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType }
					},
					cancellationToken);
			}

			return await PersistAndQueue(
				adviserName,
				memberEmail,
				string.IsNullOrWhiteSpace(schemeName) ? null : schemeName,
				safeName,
				blobName,
				queueClient,
				tableClient,
				logger,
				cancellationToken,
				trackingId);
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error uploading LoA document");
			return Results.BadRequest("Please try again later");
		}
	}

	internal static async Task<IResult> DownloadLoaDocument(
		string trackingId,
		BlobServiceClient blobClient,
		TableServiceClient tableClient,
		CancellationToken cancellationToken)
	{
		var events = await ReadEvents(tableClient, trackingId, cancellationToken);
		var document = events.LastOrDefault(e => !string.IsNullOrWhiteSpace(e.DocumentBlobName));
		if (document?.DocumentBlobName is null)
		{
			return Results.NotFound();
		}

		var blob = blobClient.GetBlobContainerClient(DocumentContainer).GetBlobClient(document.DocumentBlobName);
		if (!await blob.ExistsAsync(cancellationToken))
		{
			return Results.NotFound();
		}

		var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
		return Results.File(
			download.Value.Content,
			download.Value.Details.ContentType,
			document.DocumentFileName ?? "loa-document");
	}

	static async Task<IResult> PersistAndQueue(
		string adviserName,
		string memberEmail,
		string? schemeName,
		string? documentFileName,
		string? documentBlobName,
		QueueServiceClient queueClient,
		TableServiceClient tableClient,
		ILogger logger,
		CancellationToken cancellationToken,
		string? trackingId = null)
	{
		trackingId ??= Guid.CreateVersion7().ToString("N");

		logger.LogInformation("LoA request received for adviser {Adviser}", adviserName);

		var table = tableClient.GetTableClient("LoaAudit");
		await table.CreateIfNotExistsAsync(cancellationToken);
		await table.AddEntityAsync(new LoaAutidEntity
		{
			PartitionKey = DateTime.UtcNow.ToString("yyyy-MM-dd"),
			RowKey = trackingId,
			Status = "Received",
			AdviserName = adviserName,
			MememberEmail = memberEmail,
			SchemeName = string.IsNullOrWhiteSpace(schemeName) ? "(unspecified)" : schemeName,
			DocumentFileName = documentFileName,
			DocumentBlobName = documentBlobName
		}, cancellationToken: cancellationToken);

		var queue = queueClient.GetQueueClient("loa-request");
		await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

		var payload = JsonSerializer.Serialize(new
		{
			TrackingId = trackingId,
			MemberEmail = memberEmail,
			AdviserName = adviserName,
			SchemeName = schemeName
		});
		await queue.SendMessageAsync(payload, cancellationToken);

		logger.LogInformation("LoA request {TrackingId} queued successfully", trackingId);
		return Results.Accepted($"/loa/{trackingId}", new { trackingId, status = "Received", documentFileName });
	}

	internal static async Task<IResult> GetLoaStatus(
		string trackingId,
		TableServiceClient tableClient,
		CancellationToken cancellationToken)
	{
		var events = await ReadEvents(tableClient, trackingId, cancellationToken);

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

	static async Task<List<LoaAutidEntity>> ReadEvents(
		TableServiceClient tableClient,
		string trackingId,
		CancellationToken cancellationToken)
	{
		var table = tableClient.GetTableClient("LoaAudit");
		var results = table.QueryAsync<LoaAutidEntity>(e => e.RowKey == trackingId, cancellationToken: cancellationToken);
		var events = new List<LoaAutidEntity>();

		await foreach (var entity in results)
		{
			events.Add(entity);
		}

		events.Sort((left, right) => Nullable.Compare(left.Timestamp, right.Timestamp));
		return events;
	}

	static string SafeFileName(string name)
	{
		var fileName = Path.GetFileName(name);
		var cleaned = new string(fileName.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray());
		return string.IsNullOrWhiteSpace(cleaned) ? "loa-document" : cleaned;
	}

	public record LoaRequest(string AdviserName, string MemeberEmail, string? SchemeName);
}
