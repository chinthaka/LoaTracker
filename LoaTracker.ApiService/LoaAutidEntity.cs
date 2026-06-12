using Azure;
using Azure.Data.Tables;

public class LoaAutidEntity : ITableEntity
{
	public string PartitionKey { get; set; } = default!;
	public string RowKey { get; set; } = default!;
	public DateTimeOffset? Timestamp { get; set; }
	public ETag ETag { get ; set; }


	public required string Status { get; init; }
	public required string AdviserName { get; init; }
	public required string MememberEmail { get; init; }
	public required string SchemeName { get; init; }
}