namespace LoaTracker.ApiService.LoaAudit;

public record LoaStatusUpdate(string Status);

public record LoaStatusUpdateResponse(
	string TrackingId,
	string Status,
	string PreviousStatus,
	DateTimeOffset UpdatedAt);

internal enum LoaAuditUpdateStatus
{
	Success,
	NotFound,
	InvalidStatus
}

internal sealed record LoaAuditUpdateResult(
	LoaAuditUpdateStatus Status,
	LoaStatusUpdateResponse? Response = null);