using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.Comments;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class TicketCommentsUseCaseTests
{
    private static readonly Guid CreatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TicketId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 4, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Resolved)]
    public async Task CreateAsync_EmployeeOwnActiveTicket_PersistsImmutableComment(TicketStatus status)
    {
        var persistence = new RecordingCreatePersistence(CreatorId, status);
        var time = new CountingTimeProvider(OccurredAt);

        var result = await new CreateTicketCommentUseCase(persistence, time).ExecuteAsync(
            TicketId,
            "The issue returned.",
            new RequestActor(CreatorId, UserRole.Employee));

        Assert.Equal(CreateTicketCommentOutcome.Success, result.Outcome);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Equal(1, time.CallCount);
        Assert.NotNull(persistence.PersistedComment);
        Assert.Equal(TicketId, result.Comment?.TicketId);
        Assert.Equal(CreatorId, result.Comment?.AuthorUserId);
        Assert.Equal("The issue returned.", result.Comment?.Content);
        Assert.Equal(OccurredAt, result.Comment?.CreatedAt);
    }

    [Theory]
    [InlineData(UserRole.Technician, false)]
    [InlineData(UserRole.Technician, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task CreateAsync_PrivilegedActor_ActiveTicketSucceedsRegardlessOfOwnershipOrAssignment(
        UserRole role,
        bool actorIsCreator)
    {
        var actorId = actorIsCreator ? CreatorId : Guid.NewGuid();
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Assigned);

        var result = await new CreateTicketCommentUseCase(
            persistence,
            new CountingTimeProvider(OccurredAt)).ExecuteAsync(
            TicketId,
            "Collaboration note.",
            new RequestActor(actorId, role));

        Assert.Equal(CreateTicketCommentOutcome.Success, result.Outcome);
        Assert.Equal(actorId, result.Comment?.AuthorUserId);
        Assert.Null(persistence.Visibility?.CreatedByUserId);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task CreateAsync_VisibleClosedTicket_ReturnsConflictWithoutPersistence(UserRole role)
    {
        var actorId = role == UserRole.Employee ? CreatorId : Guid.NewGuid();
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Closed);
        var time = new CountingTimeProvider(OccurredAt);

        var result = await new CreateTicketCommentUseCase(persistence, time).ExecuteAsync(
            TicketId,
            "Too late.",
            new RequestActor(actorId, role));

        Assert.Equal(CreateTicketCommentOutcome.TicketClosed, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    [Fact]
    public async Task CreateAsync_EmployeeOtherTicket_IsConcealedBeforeClosedStateAndValidation()
    {
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Closed);

        var result = await new CreateTicketCommentUseCase(
            persistence,
            new CountingTimeProvider(OccurredAt)).ExecuteAsync(
            TicketId,
            null,
            new RequestActor(Guid.NewGuid(), UserRole.Employee));

        Assert.Equal(CreateTicketCommentOutcome.TicketNotFound, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidContent_ThrowsAfterVisibilityWithoutPersistence(string? content)
    {
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Open);

        var exception = await Assert.ThrowsAsync<TicketCommentsValidationException>(() =>
            new CreateTicketCommentUseCase(persistence, new CountingTimeProvider(OccurredAt))
                .ExecuteAsync(TicketId, content, new RequestActor(CreatorId, UserRole.Employee)));

        Assert.Equal("content", exception.ParamName);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task CreateAsync_OverLimitContent_ThrowsWithoutPersistence()
    {
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Open);

        await Assert.ThrowsAsync<TicketCommentsValidationException>(() =>
            new CreateTicketCommentUseCase(persistence, new CountingTimeProvider(OccurredAt))
                .ExecuteAsync(
                    TicketId,
                    new string('x', TicketComment.MaximumContentLength + 1),
                    new RequestActor(CreatorId, UserRole.Employee)));

        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task CreateAsync_ExactMaximumContent_Succeeds()
    {
        var content = new string('x', TicketComment.MaximumContentLength);
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Resolved);

        var result = await new CreateTicketCommentUseCase(
            persistence,
            new CountingTimeProvider(OccurredAt)).ExecuteAsync(
            TicketId,
            content,
            new RequestActor(CreatorId, UserRole.Employee));

        Assert.Equal(CreateTicketCommentOutcome.Success, result.Outcome);
        Assert.Equal(content, persistence.PersistedComment?.Content);
    }

    [Fact]
    public async Task CreateAsync_MissingTicket_ReturnsNotFound()
    {
        var persistence = new RecordingCreatePersistence(null, null);

        var result = await new CreateTicketCommentUseCase(
            persistence,
            new CountingTimeProvider(OccurredAt)).ExecuteAsync(
            TicketId,
            "Comment",
            new RequestActor(CreatorId, UserRole.Employee));

        Assert.Equal(CreateTicketCommentOutcome.TicketNotFound, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_PreCancelled_DoesNotQueryReadTimeOrPersist()
    {
        var persistence = new RecordingCreatePersistence(CreatorId, TicketStatus.Open);
        var time = new CountingTimeProvider(OccurredAt);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CreateTicketCommentUseCase(persistence, time).ExecuteAsync(
                TicketId,
                "Comment",
                new RequestActor(CreatorId, UserRole.Employee),
                source.Token));

        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    [Fact]
    public void UndefinedRole_FailsClosedAtActorConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));
    }

    [Fact]
    public async Task ListAsync_DefaultsAndEmployeeVisibility_DelegateExactly()
    {
        var query = new RecordingListQuery(new([], 1, 20, 0, 0));
        var actor = new RequestActor(CreatorId, UserRole.Employee);

        var result = await new ListTicketCommentsUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketCommentsRequest(),
            actor);

        Assert.Same(query.Result, result);
        Assert.Equal(1, query.CallCount);
        Assert.Equal(new ListTicketCommentsRequest(1, 20), query.Request);
        Assert.Equal(CreatorId, query.Visibility?.CreatedByUserId);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task ListAsync_PrivilegedActor_UsesBroadVisibilityIncludingClosed(UserRole role)
    {
        var query = new RecordingListQuery(new([], 1, 20, 0, 0));

        await new ListTicketCommentsUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketCommentsRequest(),
            new RequestActor(Guid.NewGuid(), role));

        Assert.Null(query.Visibility?.CreatedByUserId);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task ListAsync_InvalidPagination_ThrowsWithoutQuery(
        int page,
        int pageSize,
        string parameterName)
    {
        var query = new RecordingListQuery(new([], 1, 20, 0, 0));

        var exception = await Assert.ThrowsAsync<TicketCommentsValidationException>(() =>
            new ListTicketCommentsUseCase(query).ExecuteAsync(
                TicketId,
                new ListTicketCommentsRequest(page, pageSize),
                new RequestActor(CreatorId, UserRole.Employee)));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ListAsync_CustomPaginationAndCancellationToken_DelegateExactly()
    {
        var query = new RecordingListQuery(new([], 3, 50, 0, 0));
        using var source = new CancellationTokenSource();

        await new ListTicketCommentsUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketCommentsRequest(3, 50),
            new RequestActor(Guid.NewGuid(), UserRole.Technician),
            source.Token);

        Assert.Equal(new ListTicketCommentsRequest(3, 50), query.Request);
        Assert.Equal(source.Token, query.CancellationToken);
    }

    [Fact]
    public async Task ListAsync_InaccessibleOrMissingTicket_ReturnsNull()
    {
        var query = new RecordingListQuery(null);

        var result = await new ListTicketCommentsUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketCommentsRequest(),
            new RequestActor(CreatorId, UserRole.Employee));

        Assert.Null(result);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task ListAsync_PreCancelled_DoesNotQuery()
    {
        var query = new RecordingListQuery(new([], 1, 20, 0, 0));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ListTicketCommentsUseCase(query).ExecuteAsync(
                TicketId,
                new ListTicketCommentsRequest(),
                new RequestActor(CreatorId, UserRole.Employee),
                source.Token));

        Assert.Equal(0, query.CallCount);
    }

    private sealed class RecordingCreatePersistence(Guid? creatorId, TicketStatus? status)
        : ICreateTicketCommentPersistence
    {
        public int FindCallCount { get; private set; }
        public int PersistCallCount { get; private set; }
        public TicketVisibilityScope? Visibility { get; private set; }
        public TicketComment? PersistedComment { get; private set; }

        public Task<CreateTicketCommentTarget?> FindVisibleTicketAsync(
            Guid ticketId,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            FindCallCount++;
            Visibility = visibility;
            CreateTicketCommentTarget? result = creatorId is not null && status is not null &&
                (visibility.CreatedByUserId is null || visibility.CreatedByUserId == creatorId)
                ? new(ticketId, status.Value)
                : null;
            return Task.FromResult(result);
        }

        public Task PersistAsync(TicketComment comment, CancellationToken cancellationToken)
        {
            PersistCallCount++;
            PersistedComment = comment;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingListQuery(ListTicketCommentsResult? result)
        : IListTicketCommentsQuery
    {
        public ListTicketCommentsResult? Result { get; } = result;
        public int CallCount { get; private set; }
        public ListTicketCommentsRequest? Request { get; private set; }
        public TicketVisibilityScope? Visibility { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ListTicketCommentsResult?> ListAsync(
            Guid ticketId,
            ListTicketCommentsRequest request,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            Visibility = visibility;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }

    private sealed class CountingTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public int CallCount { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return timestamp;
        }
    }
}
