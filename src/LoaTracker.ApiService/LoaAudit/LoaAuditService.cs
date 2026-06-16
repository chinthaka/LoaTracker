using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace LoaTracker.ApiService.LoaAudit;

internal sealed class LoaAuditService(
	TableServiceClient tableClient,
	ILogger<LoaAuditService> logger) : ILoaAuditService
{
	public async Task<LoaAuditUpdateResult> UpdateStatusAsync(
		string trackingId,
		LoaStatusUpdate update,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(update.Status))
		{
			return new LoaAuditUpdateResult(LoaAuditUpdateStatus.InvalidStatus);
		}

		var table = tableClient.GetTableClient("LoaAudit");
		await table.CreateIfNotExistsAsync(cancellationToken);

		var results = table.QueryAsync<LoaAutidEntity>(
			e => e.RowKey == trackingId,
			null,
			null,
			cancellationToken);

		LoaAutidEntity? latest = null;

		await foreach (var entity in results)
		{
			if (latest is null || entity.Timestamp > latest.Timestamp)
			{
				latest = entity;
			}
		}

		if (latest is null)
		{
			return new LoaAuditUpdateResult(LoaAuditUpdateStatus.NotFound);
		}

		var status = update.Status.Trim();

		await table.AddEntityAsync(new LoaAutidEntity
		{
			PartitionKey = DateTime.UtcNow.ToString("yyyy-MM-dd"),
			RowKey = trackingId,
			Status = status,
			AdviserName = latest.AdviserName,
			MememberEmail = latest.MememberEmail,
			SchemeName = latest.SchemeName
		}, cancellationToken: cancellationToken);

		logger.LogInformation(
			"LoA {TrackingId} status updated from {PreviousStatus} to {Status}",
			trackingId,
			latest.Status,
			status);

		return new LoaAuditUpdateResult(
			LoaAuditUpdateStatus.Success,
			new LoaStatusUpdateResponse(
				trackingId,
				status,
				latest.Status,
				DateTimeOffset.UtcNow));
	}
}