using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.ResolutionReview;

public sealed class TicketResolutionReviewUseCase(
    ITicketAssignmentPersistence persistence,
    TimeProvider timeProvider)
{
    public Task<TicketResolutionReviewResult> CloseAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(ticketId, actor, ReviewOperation.Close, cancellationToken);

    public Task<TicketResolutionReviewResult> RejectResolutionAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(ticketId, actor, ReviewOperation.RejectResolution, cancellationToken);

    private async Task<TicketResolutionReviewResult> ExecuteAsync(
        Guid ticketId,
        RequestActor actor,
        ReviewOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new TicketResolutionReviewValidationException(
                "Ticket ID cannot be empty.",
                nameof(ticketId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (actor.Role != UserRole.Administrator)
        {
            return new(TicketResolutionReviewOutcome.Forbidden);
        }

        var ticket = await persistence.FindTrackedAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return new(TicketResolutionReviewOutcome.TicketNotFound);
        }

        var oldStatus = ticket.Status;
        var rejectedSummary = ticket.ResolutionSummary;
        var occurredAt = timeProvider.GetUtcNow();

        try
        {
            if (operation == ReviewOperation.Close)
            {
                ticket.Close(occurredAt);
            }
            else
            {
                ticket.RejectResolution(occurredAt);
            }
        }
        catch (InvalidOperationException)
        {
            return new(TicketResolutionReviewOutcome.InvalidState);
        }

        IReadOnlyCollection<TicketHistory> histories = operation == ReviewOperation.Close
            ?
            [
                History(ticket, actor.UserId, TicketHistoryEventType.Closed, null, null, occurredAt),
                History(ticket, actor.UserId, TicketHistoryEventType.StatusChanged,
                    oldStatus.ToString(), ticket.Status.ToString(), occurredAt)
            ]
            :
            [
                History(ticket, actor.UserId, TicketHistoryEventType.ResolutionRejected,
                    rejectedSummary, null, occurredAt),
                History(ticket, actor.UserId, TicketHistoryEventType.StatusChanged,
                    oldStatus.ToString(), ticket.Status.ToString(), occurredAt)
            ];

        var persistenceOutcome = await persistence.PersistAsync(ticket, histories, cancellationToken);
        if (persistenceOutcome == TicketAssignmentPersistenceOutcome.ConcurrencyConflict)
        {
            return new(TicketResolutionReviewOutcome.ConcurrencyConflict);
        }

        if (persistenceOutcome != TicketAssignmentPersistenceOutcome.Persisted)
        {
            throw new InvalidOperationException("Ticket mutation persistence returned an unknown outcome.");
        }

        return new(
            TicketResolutionReviewOutcome.Success,
            new TicketResolutionReviewDetails(
                ticket.Id,
                ticket.Status,
                ticket.AssignedTechnicianId,
                ticket.ResolutionSummary,
                ticket.UpdatedAt,
                ticket.ResolvedAt,
                ticket.ClosedAt));
    }

    private static TicketHistory History(
        Ticket ticket,
        Guid actorId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt) =>
        TicketHistory.Create(ticket.Id, actorId, eventType, oldValue, newValue, occurredAt);

    private enum ReviewOperation
    {
        Close,
        RejectResolution
    }
}
