namespace LoaTracker.ApiService.LoaAudit;

internal interface ILoaAuditService
{
	Task<LoaAuditUpdateResult> UpdateStatusAsync(
		string trackingId,
		LoaStatusUpdate update,
		CancellationToken cancellationToken);
}