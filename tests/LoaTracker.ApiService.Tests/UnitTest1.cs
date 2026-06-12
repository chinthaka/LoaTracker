using LoaTracker.ApiService;

namespace LoaTracker.ApiService.Tests;

public class GetLoaStatusTests
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

    private static TableServiceClient CreateTableServiceMock(AsyncPageable<LoaAutidEntity> pageableToReturn)
    {
        var tableClientMock = new Mock<TableClient>();
        tableClientMock
            .Setup(c => c.QueryAsync<LoaAutidEntity>(
                It.IsAny<Expression<Func<LoaAutidEntity, bool>>>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(pageableToReturn);

        var tableServiceMock = new Mock<TableServiceClient>();
        tableServiceMock
            .Setup(s => s.GetTableClient("LoaAudit"))
            .Returns(tableClientMock.Object);

        return tableServiceMock.Object;
    }

    [Fact]
    public async Task GetLoaStatus_NoMatchingEntity_ReturnsNotFound()
    {
        // Arrange
        var tableClient = CreateTableServiceMock(CreateEmptyPageable());
        var ct = CancellationToken.None;

        // Act
        var result = await LoaApi.GetLoaStatus("nonexistent123", tableClient, ct);

        // Assert
        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetLoaStatus_SingleEvent_ReturnsOkWithCurrentStatusAndHistory()
    {
        // Arrange
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

        var tableClient = CreateTableServiceMock(CreatePageable(entity));
        var ct = CancellationToken.None;

        // Act
        var result = await LoaApi.GetLoaStatus(trackingId, tableClient, ct);

        // Assert
        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, status.StatusCode);

        dynamic value = ok.Value!;
        Assert.Equal(trackingId, (string)value.trackingId);
        Assert.Equal("Received", (string)value.currentStatus);

        var history = ((IEnumerable<object>)value.history).ToList();
        Assert.Single(history);
    }

    [Fact]
    public async Task GetLoaStatus_MultipleEvents_ReturnsLatestStatusAsCurrent()
    {
        // Arrange
        var trackingId = "track987654";
        var now = DateTimeOffset.UtcNow;

        var e1 = new LoaAutidEntity
        {
            PartitionKey = "2025-01-10",
            RowKey = trackingId,
            Status = "Received",
            AdviserName = "Alice",
            MememberEmail = "a@example.com",
            SchemeName = "SchemeX"
        };
        e1.Timestamp = now.AddMinutes(-30);

        var e2 = new LoaAutidEntity
        {
            PartitionKey = "2025-01-10",
            RowKey = trackingId,
            Status = "UnderReview",
            AdviserName = "Alice",
            MememberEmail = "a@example.com",
            SchemeName = "SchemeX"
        };
        e2.Timestamp = now.AddMinutes(-10);

        var e3 = new LoaAutidEntity
        {
            PartitionKey = "2025-01-11",
            RowKey = trackingId,
            Status = "Approved",
            AdviserName = "Alice",
            MememberEmail = "a@example.com",
            SchemeName = "SchemeX"
        };
        e3.Timestamp = now;

        var tableClient = CreateTableServiceMock(CreatePageable(e2, e1, e3));
        var ct = CancellationToken.None;

        // Act
        var result = await LoaApi.GetLoaStatus(trackingId, tableClient, ct);

        // Assert
        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, status.StatusCode);

        dynamic value = ok.Value!;

        Assert.Equal(trackingId, (string)value.trackingId);
        Assert.Equal("Approved", (string)value.currentStatus);

        var history = ((IEnumerable<dynamic>)value.history).ToList();
        Assert.Equal(3, history.Count);
        // After OrderBy the last should be the most recent
        Assert.Equal("Approved", (string)history[2].Status);
    }

    [Fact]
    public async Task GetLoaStatus_FoundEntity_IncludesFullHistoryObjects()
    {
        // Arrange
        var trackingId = "fullhist001";
        var entity = new LoaAutidEntity
        {
            PartitionKey = "2025-02-01",
            RowKey = trackingId,
            Status = "Processed",
            AdviserName = "Bob",
            MememberEmail = "bob@corp.test",
            SchemeName = "CorpScheme"
        };
        entity.Timestamp = DateTimeOffset.UtcNow;

        var tableClient = CreateTableServiceMock(CreatePageable(entity));
        var ct = CancellationToken.None;

        // Act
        var result = await LoaApi.GetLoaStatus(trackingId, tableClient, ct);

        // Assert
        var ok = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, ok.StatusCode);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(valueResult.Value);
    }
}

