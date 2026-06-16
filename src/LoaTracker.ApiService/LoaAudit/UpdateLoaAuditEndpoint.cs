using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LoaTracker.ApiService.LoaAudit;

internal static class UpdateLoaAuditEndpoint
{
	public static RouteGroupBuilder MapUpdateLoaAudit(this RouteGroupBuilder group)
	{
		group.MapPatch("/{trackingId}", HandleAsync).WithName("UpdateLoaAudit");
		return group;
	}

	private static async Task<Results<Ok<LoaStatusUpdateResponse>, NotFound, ValidationProblem>> HandleAsync(
		string trackingId,
		LoaStatusUpdate update,
		ILoaAuditService auditService,
		CancellationToken cancellationToken)
	{
		var result = await auditService.UpdateStatusAsync(trackingId, update, cancellationToken);

		return result.Status switch
		{
			LoaAuditUpdateStatus.Success => TypedResults.Ok(result.Response!),
			LoaAuditUpdateStatus.NotFound => TypedResults.NotFound(),
			LoaAuditUpdateStatus.InvalidStatus => TypedResults.ValidationProblem(
				new Dictionary<string, string[]>
				{
					["status"] = ["Status is required"]
				}),
			_ => throw new UnreachableException()
		};
	}
}