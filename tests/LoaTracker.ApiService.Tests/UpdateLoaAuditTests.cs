using Azure.Data.Tables.Models;
using LoaTracker.ApiService.LoaAudit;

namespace LoaTracker.ApiService.Tests;

public class UpdateLoaAuditTests
{
    private static AsyncPageable<LoaAutidEntity> CreatePageable(params LoaAutidEntity[] entities)
    {
        var page = Page<LoaAutidEntity>.FromValues(entities, continuationToken: null, response: Mock.Of<Response>());
        return AsyncPageable<LoaAutidEntity>.FromPages(new[] { page });
    }

    private static AsyncPageable<LoaAutidEntity> CreateEmptyPageable()
    {
        var page = Page<LoaAutidEntity>.FromValues(Array.Empty<LoaAutidEntity>(), continuationToken: null, response: Mock.Of<Response>());
        return AsyncPageable<LoaAutidEntity>.FromPages(new[] { page });
    }

    private static (LoaAuditService Service, Mock<TableClient> TableClient) CreateService(
        AsyncPageable<LoaAutidEntity> pageableToReturn)
    {
        var tableClientMock = new Mock<TableClient>();
        tableClientMock
            .Setup(c => c.QueryAsync<LoaAutidEntity>(
                It.IsAny<Expression<Func<LoaAutidEntity, bool>>>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(pageableToReturn);
        tableClientMock
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Response.FromValue(default(TableItem)!, Mock.Of<Response>())));
        tableClientMock
            .Setup(c => c.AddEntityAsync(It.IsAny<LoaAutidEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var tableServiceMock = new Mock<TableServiceClient>();
        tableServiceMock
            .Setup(s => s.GetTableClient("LoaAudit"))
            .Returns(tableClientMock.Object);

        var service = new LoaAuditService(
            tableServiceMock.Object,
            NullLogger<LoaAuditService>.Instance);

        return (service, tableClientMock);
    }

    [Fact]
    public async Task UpdateStatusAsync_NoMatchingEntity_ReturnsNotFound()
    {
        var (service, _) = CreateService(CreateEmptyPageable());

        var result = await service.UpdateStatusAsync(
            "missing-id",
            new LoaStatusUpdate("UnderReview"),
            CancellationToken.None);

        Assert.Equal(LoaAuditUpdateStatus.NotFound, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task UpdateStatusAsync_EmptyStatus_ReturnsInvalidStatus()
    {
        var entity = new LoaAutidEntity
        {
            PartitionKey = "2025-01-15",
            RowKey = "abc123",
            Status = "Received",
            AdviserName = "Jane",
            MememberEmail = "jane@example.com",
            SchemeName = "SchemeA"
        };
        var (service, _) = CreateService(CreatePageable(entity));

        var result = await service.UpdateStatusAsync(
            "abc123",
            new LoaStatusUpdate("  "),
            CancellationToken.None);

        Assert.Equal(LoaAuditUpdateStatus.InvalidStatus, result.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ValidRequest_ReturnsSuccessAndAppendsAuditEvent()
    {
        var trackingId = "abc123def456";
        var entity = new LoaAutidEntity
        {
            PartitionKey = "2025-01-15",
            RowKey = trackingId,
            Status = "Received",
            AdviserName = "John Doe",
            MememberEmail = "member@example.com",
            SchemeName = "DefaultScheme"
        };
        entity.Timestamp = DateTimeOffset.UtcNow;

        var (service, tableClientMock) = CreateService(CreatePageable(entity));

        var result = await service.UpdateStatusAsync(
            trackingId,
            new LoaStatusUpdate("UnderReview"),
            CancellationToken.None);

        Assert.Equal(LoaAuditUpdateStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(trackingId, result.Response.TrackingId);
        Assert.Equal("UnderReview", result.Response.Status);
        Assert.Equal("Received", result.Response.PreviousStatus);

        tableClientMock.Verify(
            c => c.AddEntityAsync(
                It.Is<LoaAutidEntity>(e =>
                    e.RowKey == trackingId &&
                    e.Status == "UnderReview" &&
                    e.AdviserName == entity.AdviserName &&
                    e.MememberEmail == entity.MememberEmail &&
                    e.SchemeName == entity.SchemeName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}