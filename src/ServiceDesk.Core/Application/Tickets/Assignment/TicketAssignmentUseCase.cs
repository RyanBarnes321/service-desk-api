using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.Assignment;

public sealed class TicketAssignmentUseCase(
    ITicketAssignmentPersistence persistence,
    ICurrentUserStateQuery userStateQuery,
    TimeProvider timeProvider)
{
    public Task<TicketAssignmentResult> ClaimAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            targetTechnicianId: actor.UserId,
            AssignmentOperation.Claim,
            cancellationToken);
    }

    public Task<TicketAssignmentResult> AssignAsync(
        Guid ticketId,
        Guid technicianId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            technicianId,
            AssignmentOperation.Assign,
            cancellationToken);
    }

    public Task<TicketAssignmentResult> UnassignAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            targetTechnicianId: null,
            AssignmentOperation.Unassign,
            cancellationToken);
    }

    private async Task<TicketAssignmentResult> ExecuteAsync(
        Guid ticketId,
        RequestActor actor,
        Guid? targetTechnicianId,
        AssignmentOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket ID cannot be empty.", nameof(ticketId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAuthorized(actor.Role, operation))
        {
            return new TicketAssignmentResult(TicketAssignmentOutcome.Forbidden);
        }

        if (operation == AssignmentOperation.Assign)
        {
            if (targetTechnicianId is null || targetTechnicianId == Guid.Empty)
            {
                return new TicketAssignmentResult(TicketAssignmentOutcome.TargetUnavailable);
            }

            var target = await userStateQuery.FindAsync(targetTechnicianId.Value, cancellationToken);
            if (target is null ||
                target.UserId != targetTechnicianId.Value ||
                !target.IsActive ||
                target.Role != UserRole.Technician)
            {
                return new TicketAssignmentResult(TicketAssignmentOutcome.TargetUnavailable);
            }
        }

        var ticket = await persistence.FindTrackedAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return new TicketAssignmentResult(TicketAssignmentOutcome.TicketNotFound);
        }

        var occurredAt = timeProvider.GetUtcNow();
        IReadOnlyCollection<TicketHistory> histories;

        try
        {
            histories = operation switch
            {
                AssignmentOperation.Claim => Claim(ticket, actor.UserId, occurredAt),
                AssignmentOperation.Assign => Assign(
                    ticket,
                    targetTechnicianId!.Value,
                    actor.UserId,
                    occurredAt),
                AssignmentOperation.Unassign => Unassign(ticket, actor.UserId, occurredAt),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
        catch (InvalidOperationException)
        {
            return new TicketAssignmentResult(TicketAssignmentOutcome.InvalidState);
        }

        var persistenceOutcome = await persistence.PersistAsync(ticket, histories, cancellationToken);
        if (persistenceOutcome == TicketAssignmentPersistenceOutcome.ConcurrencyConflict)
        {
            return new TicketAssignmentResult(TicketAssignmentOutcome.ConcurrencyConflict);
        }

        if (persistenceOutcome != TicketAssignmentPersistenceOutcome.Persisted)
        {
            throw new InvalidOperationException("Ticket assignment persistence returned an unknown outcome.");
        }

        return new TicketAssignmentResult(
            TicketAssignmentOutcome.Success,
            new TicketAssignmentDetails(
                ticket.Id,
                ticket.Status,
                ticket.AssignedTechnicianId,
                ticket.UpdatedAt));
    }

    private static IReadOnlyCollection<TicketHistory> Assign(
        Ticket ticket,
        Guid technicianId,
        Guid performedByUserId,
        DateTimeOffset occurredAt)
    {
        var oldStatus = ticket.Status;
        var oldTechnicianId = ticket.AssignedTechnicianId;

        if (oldTechnicianId is null)
        {
            ticket.Assign(technicianId, occurredAt);
            return
            [
                History(
                    ticket.Id,
                    performedByUserId,
                    TicketHistoryEventType.Assigned,
                    null,
                    technicianId.ToString("D"),
                    occurredAt),
                StatusHistory(ticket.Id, performedByUserId, oldStatus, ticket.Status, occurredAt)
            ];
        }

        ticket.Reassign(technicianId, occurredAt);
        var histories = new List<TicketHistory>
        {
            History(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.Reassigned,
                oldTechnicianId.Value.ToString("D"),
                technicianId.ToString("D"),
                occurredAt)
        };

        if (oldStatus != ticket.Status)
        {
            histories.Add(StatusHistory(
                ticket.Id,
                performedByUserId,
                oldStatus,
                ticket.Status,
                occurredAt));
        }

        return histories;
    }

    private static IReadOnlyCollection<TicketHistory> Claim(
        Ticket ticket,
        Guid performedByUserId,
        DateTimeOffset occurredAt)
    {
        var oldStatus = ticket.Status;
        ticket.Assign(performedByUserId, occurredAt);

        return
        [
            History(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.Assigned,
                null,
                performedByUserId.ToString("D"),
                occurredAt),
            StatusHistory(ticket.Id, performedByUserId, oldStatus, ticket.Status, occurredAt)
        ];
    }

    private static IReadOnlyCollection<TicketHistory> Unassign(
        Ticket ticket,
        Guid performedByUserId,
        DateTimeOffset occurredAt)
    {
        var oldStatus = ticket.Status;
        var oldTechnicianId = ticket.AssignedTechnicianId;
        ticket.Unassign(occurredAt);

        return
        [
            History(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.Unassigned,
                oldTechnicianId!.Value.ToString("D"),
                null,
                occurredAt),
            StatusHistory(ticket.Id, performedByUserId, oldStatus, ticket.Status, occurredAt)
        ];
    }

    private static TicketHistory StatusHistory(
        Guid ticketId,
        Guid performedByUserId,
        TicketStatus oldStatus,
        TicketStatus newStatus,
        DateTimeOffset occurredAt)
    {
        return History(
            ticketId,
            performedByUserId,
            TicketHistoryEventType.StatusChanged,
            oldStatus.ToString(),
            newStatus.ToString(),
            occurredAt);
    }

    private static TicketHistory History(
        Guid ticketId,
        Guid performedByUserId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        return TicketHistory.Create(
            ticketId,
            performedByUserId,
            eventType,
            oldValue,
            newValue,
            occurredAt);
    }

    private static bool IsAuthorized(UserRole role, AssignmentOperation operation)
    {
        return operation switch
        {
            AssignmentOperation.Claim => role is UserRole.Technician or UserRole.Administrator,
            AssignmentOperation.Assign or AssignmentOperation.Unassign =>
                role == UserRole.Administrator,
            _ => false
        };
    }

    private enum AssignmentOperation
    {
        Claim,
        Assign,
        Unassign
    }
}
