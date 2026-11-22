using FluentAssertions;
using InventoryAggregator.Contracts;
using InventoryAggregator.Models;
using InventoryAggregator.Services;
using Moq;

namespace InventoryAggregator.Tests;

public sealed class InventoryAggregationServiceTests
{
    private readonly Mock<IInventoryApiClient> _clientMock = new();

    private IInventoryAggregationService BuildService() =>
        new InventoryAggregationService(_clientMock.Object);

    // ------------------------------------------------------------------
    // Zero-quantity exclusion
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aggregate_ExcludesRecordsWithZeroQuantity()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 10m, quantity: 5),
                MakeRecord(2, "Electronics", price: 20m, quantity: 0), // should be excluded
            });

        var result = await BuildService().AggregateAsync();

        var electronics = result.Categories.Single(c => c.Category == "Electronics");
        electronics.AvailableRecordCount.Should().Be(1);
        electronics.Items.Should().ContainSingle(r => r.Id == 1);
        electronics.Items.Should().NotContain(r => r.Id == 2);
    }

    [Fact]
    public async Task Aggregate_ReturnsEmptyCategories_WhenAllRecordsAreZeroQuantity()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 10m, quantity: 0),
                MakeRecord(2, "Furniture",  price: 50m, quantity: 0),
            });

        var result = await BuildService().AggregateAsync();

        result.Categories.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Grouping and value calculation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aggregate_GroupsRecordsByCategory()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 100m, quantity: 2),
                MakeRecord(2, "Furniture",   price: 200m, quantity: 3),
                MakeRecord(3, "Electronics", price: 50m,  quantity: 4),
            });

        var result = await BuildService().AggregateAsync();

        result.Categories.Should().HaveCount(2);
        result.Categories.Should().Contain(c => c.Category == "Electronics");
        result.Categories.Should().Contain(c => c.Category == "Furniture");
    }

    [Fact]
    public async Task Aggregate_CalculatesTotalInventoryValue_AsSumOfPriceTimesQuantity()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 100m, quantity: 2),  // 200
                MakeRecord(2, "Electronics", price: 50m,  quantity: 4),  // 200
            });

        var result = await BuildService().AggregateAsync();

        var electronics = result.Categories.Single();
        electronics.TotalInventoryValue.Should().Be(400m);
    }

    [Fact]
    public async Task Aggregate_TotalInventoryValue_IsRoundedToTwoDecimalPlaces()
    {
        // 1.005 * 3 = 3.015 — rounds to 3.02 with AwayFromZero
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 1.005m, quantity: 3),
            });

        var result = await BuildService().AggregateAsync();

        result.Categories.Single().TotalInventoryValue.Should().Be(3.02m);
    }

    [Fact]
    public async Task Aggregate_TotalInventoryValue_NeverExceedsTwoDecimalPlaces()
    {
        // Repeating decimals: 1.0m / 3 would be 0.333... but price * quantity here
        // produces a value whose sum has more than 2 dp before rounding.
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", price: 10.999m, quantity: 2),  // 21.998
                MakeRecord(2, "Electronics", price:  0.003m, quantity: 1),  //  0.003 → total 22.001
            });

        var result = await BuildService().AggregateAsync();

        var value = result.Categories.Single().TotalInventoryValue;
        // Value should be rounded to exactly 2 dp
        value.Should().Be(Math.Round(value, 2));
        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits(value)[3])[2];
        decimalPlaces.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task Aggregate_SetsAvailableRecordCount_ToCountOfNonZeroRecords()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", quantity: 1),
                MakeRecord(2, "Electronics", quantity: 2),
                MakeRecord(3, "Electronics", quantity: 0), // excluded
            });

        var result = await BuildService().AggregateAsync();

        result.Categories.Single().AvailableRecordCount.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // Most-recently-updated record
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aggregate_SelectsMostRecentlyUpdatedRecord_ByLastUpdated()
    {
        var older  = DateTimeOffset.UtcNow.AddDays(-2);
        var newest = DateTimeOffset.UtcNow.AddDays(-1);
        var oldest = DateTimeOffset.UtcNow.AddDays(-3);

        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", quantity: 1, lastUpdated: oldest),
                MakeRecord(2, "Electronics", quantity: 1, lastUpdated: newest),
                MakeRecord(3, "Electronics", quantity: 1, lastUpdated: older),
            });

        var result = await BuildService().AggregateAsync();

        result.Categories.Single().MostRecentlyUpdated.Id.Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_MostRecentlyUpdated_HasCorrectFields()
    {
        var updated = new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero);

        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(42, "Electronics", name: "Widget Pro", quantity: 5, lastUpdated: updated),
            });

        var result = await BuildService().AggregateAsync();

        var mostRecent = result.Categories.Single().MostRecentlyUpdated;
        mostRecent.Id.Should().Be(42);
        mostRecent.Name.Should().Be("Widget Pro");
        mostRecent.LastUpdated.Should().Be(updated);
    }

    // ------------------------------------------------------------------
    // Items collection
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aggregate_Items_ContainsAllNonZeroRecordsForCategory()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>
            {
                MakeRecord(1, "Electronics", quantity: 1),
                MakeRecord(2, "Electronics", quantity: 2),
                MakeRecord(3, "Electronics", quantity: 0), // excluded
                MakeRecord(4, "Furniture",   quantity: 3),
            });

        var result = await BuildService().AggregateAsync();

        var electronics = result.Categories.Single(c => c.Category == "Electronics");
        electronics.Items.Should().HaveCount(2);
        electronics.Items.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    // ------------------------------------------------------------------
    // Edge cases
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aggregate_ReturnsEmpty_WhenNoRecords()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ReturnsAsync(new List<InventoryRecord>());

        var result = await BuildService().AggregateAsync();

        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregate_PropagatesException_WhenClientThrows()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ThrowsAsync(new InventoryApiException("upstream down"));

        var sut = BuildService();
        await sut.Invoking(s => s.AggregateAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("upstream down");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static InventoryRecord MakeRecord(
        int id,
        string category,
        decimal price = 10m,
        int quantity = 1,
        string name = "Test Item",
        DateTimeOffset? lastUpdated = null) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Price = price,
            Quantity = quantity,
            LastUpdated = lastUpdated ?? DateTimeOffset.UtcNow,
        };
}
