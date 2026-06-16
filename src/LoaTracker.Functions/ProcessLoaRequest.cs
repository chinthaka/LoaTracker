using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Text.Json;

namespace LoaTracker.Functions;

public class ProcessLoaRequest
{
	private readonly ILogger<ProcessLoaRequest> _logger;
	private readonly ISendGridClient _sendGrid;

	public ProcessLoaRequest(ILogger<ProcessLoaRequest> logger, ISendGridClient sendGrid)
	{
		_logger = logger;
		_sendGrid = sendGrid;
	}


	[Function(nameof(ProcessLoaRequest))]
	public async Task Run(
		[QueueTrigger("loa-request", Connection = "queues")] QueueMessage message,
		[TableInput("LoaAudit", Connection = "tables")] TableClient table,
		CancellationToken ct)
	{
		var payload = JsonSerializer.Deserialize<LoaPayload>(message.MessageText)
			?? throw new InvalidOperationException("Invalid payload");

		_logger.LogInformation("Processing LoA {TrackingId} for: {messageText}", payload.TrackingId, message.MessageText);

		// Send the confirmation email
		//var msg = new SendGridMessage
		//{
		//	From = new EmailAddress("loa2@gmail.com", "LoA Tracker"),
		//	Subject = $"LoA request received - {payload.TrackingId}"
		//};
		var htmlContent = "<strong>and easy to do anywhere with C#.</strong>";
		var subject = $"LoA request received - {payload.TrackingId}";
		//msg.AddTo(payload.MemberEmail);
		var plainTextContent =
			$"Dear member,\n\n" +
			$"We've received a Letter of Authority request from adviser {payload.AdviserName}. " +
			$"Tracking ID: {payload.TrackingId}\n\n" +
			$"Punter Southall LoA Tracker (demo)";

		var mssg = MailHelper.CreateSingleEmail(new EmailAddress("kumarasiri@gmail.com", "LoA Tracker"), new EmailAddress(payload.MemberEmail), subject, plainTextContent, htmlContent);

		var response = await _sendGrid.SendEmailAsync(mssg, ct);

		if (!response.IsSuccessStatusCode)
		{
			_logger.LogWarning("SendGrid responsed {Status}", response.StatusCode);
		}

		// Update the audit row 
		await table.AddEntityAsync(new TableEntity(
			partitionKey: DateTime.UtcNow.ToString("yyyy-MM-dd"),
			rowKey: $"{payload.TrackingId}-processed")
		{
			["Status"] = "Processed",
			["TrackingId"] = payload.TrackingId,
			["EmailSentTo"] = payload.MemberEmail,
			["ProcessedAt"] = DateTime.UtcNow
		}, ct);


		_logger.LogInformation("Loa {TrackingId} processed", payload.TrackingId);

	}

	private record LoaPayload(string TrackingId, string AdviserName, string MemberEmail, string? SchemeName);
}