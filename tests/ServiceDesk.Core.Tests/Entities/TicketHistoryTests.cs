using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Entities;

public class TicketHistoryTests
{
    private static readonly Guid TicketId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PerformerUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void Create_WithUserPerformer_CreatesHistoryWithoutNormalizingValues()
    {
        const string oldValue = " Medium ";
        const string newValue = " High ";

        var history = TicketHistory.Create(
            TicketId,
            PerformerUserId,
            TicketHistoryEventType.PriorityChanged,
            oldValue,
            newValue,
            CreatedAt);

        Assert.NotEqual(Guid.Empty, history.Id);
        Assert.Equal(TicketId, history.TicketId);
        Assert.Equal<Guid?>(PerformerUserId, history.PerformedByUserId);
        Assert.Equal(TicketHistoryEventType.PriorityChanged, history.EventType);
        Assert.Equal(oldValue, history.OldValue);
        Assert.Equal(newValue, history.NewValue);
        Assert.Equal(CreatedAt, history.CreatedAt);
    }

    [Fact]
    public void Create_WithSystemPerformerAndNullValues_CreatesHistory()
    {
        var history = TicketHistory.Create(
            TicketId,
            null,
            TicketHistoryEventType.Created,
            null,
            null,
            CreatedAt);

        Assert.Null(history.PerformedByUserId);
        Assert.Null(history.OldValue);
        Assert.Null(history.NewValue);
    }

    [Fact]
    public void Create_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketHistory.Create(
            Guid.Empty,
            PerformerUserId,
            TicketHistoryEventType.Created,
            null,
            null,
            CreatedAt));
    }

    [Fact]
    public void Create_WithEmptyPerformerUserId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketHistory.Create(
            TicketId,
            Guid.Empty,
            TicketHistoryEventType.Created,
            null,
            null,
            CreatedAt));
    }

    [Fact]
    public void Create_WithUndefinedEventType_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketHistory.Create(
            TicketId,
            PerformerUserId,
            (TicketHistoryEventType)999,
            null,
            null,
            CreatedAt));
    }

    [Theory]
    [InlineData(TicketHistoryEventType.Assigned, null, null)]
    [InlineData(TicketHistoryEventType.Closed, "unexpected old value", "unexpected new value")]
    [InlineData(TicketHistoryEventType.PriorityChanged, null, "  High  ")]
    public void Create_WithArbitraryValueCombination_RetainsValuesWithoutSemanticValidation(
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue)
    {
        var history = TicketHistory.Create(
            TicketId,
            PerformerUserId,
            eventType,
            oldValue,
            newValue,
            CreatedAt);

        Assert.Equal(eventType, history.EventType);
        Assert.Equal(oldValue, history.OldValue);
        Assert.Equal(newValue, history.NewValue);
    }
}
