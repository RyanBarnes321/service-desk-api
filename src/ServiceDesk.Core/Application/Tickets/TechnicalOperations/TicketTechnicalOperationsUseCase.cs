using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.TechnicalOperations;

public sealed class TicketTechnicalOperationsUseCase(
    ITicketAssignmentPersistence persistence,
    TimeProvider timeProvider)
{
    public Task<TicketTechnicalOperationResult> StartWorkAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            TechnicalOperation.StartWork,
            resolutionSummary: null,
            priority: null,
            cancellationToken);
    }

    public Task<TicketTechnicalOperationResult> WaitAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            TechnicalOperation.Wait,
            resolutionSummary: null,
            priority: null,
            cancellationToken);
    }

    public Task<TicketTechnicalOperationResult> ResumeAsync(
        Guid ticketId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            TechnicalOperation.Resume,
            resolutionSummary: null,
            priority: null,
            cancellationToken);
    }

    public Task<TicketTechnicalOperationResult> ResolveAsync(
        Guid ticketId,
        string resolutionSummary,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            TechnicalOperation.Resolve,
            resolutionSummary,
            priority: null,
            cancellationToken);
    }

    public Task<TicketTechnicalOperationResult> ChangePriorityAsync(
        Guid ticketId,
        TicketPriority? priority,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            ticketId,
            actor,
            TechnicalOperation.ChangePriority,
            resolutionSummary: null,
            priority,
            cancellationToken);
    }

    private async Task<TicketTechnicalOperationResult> ExecuteAsync(
        Guid ticketId,
        RequestActor actor,
        TechnicalOperation operation,
        string? resolutionSummary,
        TicketPriority? priority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new TicketTechnicalOperationValidationException(
                "Ticket ID cannot be empty.",
                nameof(ticketId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (actor.Role != UserRole.Technician)
        {
            return new TicketTechnicalOperationResult(TicketTechnicalOperationOutcome.Forbidden);
        }

        var ticket = await persistence.FindTrackedAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return new TicketTechnicalOperationResult(TicketTechnicalOperationOutcome.TicketNotFound);
        }

        if (ticket.AssignedTechnicianId != actor.UserId)
        {
            return new TicketTechnicalOperationResult(TicketTechnicalOperationOutcome.Forbidden);
        }

        ValidateInput(operation, resolutionSummary, priority);

        var oldStatus = ticket.Status;
        var oldPriority = ticket.Priority;
        var occurredAt = timeProvider.GetUtcNow();

        try
        {
            ApplyMutation(ticket, operation, resolutionSummary, priority, occurredAt);
        }
        catch (InvalidOperationException)
        {
            return new TicketTechnicalOperationResult(TicketTechnicalOperationOutcome.InvalidState);
        }

        var histories = CreateHistories(
            ticket,
            actor.UserId,
            operation,
            oldStatus,
            oldPriority,
            resolutionSummary,
            occurredAt);
        var persistenceOutcome = await persistence.PersistAsync(ticket, histories, cancellationToken);

        if (persistenceOutcome == TicketAssignmentPersistenceOutcome.ConcurrencyConflict)
        {
            return new TicketTechnicalOperationResult(
                TicketTechnicalOperationOutcome.ConcurrencyConflict);
        }

        if (persistenceOutcome != TicketAssignmentPersistenceOutcome.Persisted)
        {
            throw new InvalidOperationException(
                "Ticket mutation persistence returned an unknown outcome.");
        }

        return new TicketTechnicalOperationResult(
            TicketTechnicalOperationOutcome.Success,
            new TicketTechnicalOperationDetails(
                ticket.Id,
                ticket.Status,
                ticket.Priority,
                ticket.AssignedTechnicianId,
                ticket.ResolutionSummary,
                ticket.UpdatedAt,
                ticket.ResolvedAt));
    }

    private static void ValidateInput(
        TechnicalOperation operation,
        string? resolutionSummary,
        TicketPriority? priority)
    {
        if (operation == TechnicalOperation.Resolve)
        {
            if (string.IsNullOrWhiteSpace(resolutionSummary))
            {
                throw new TicketTechnicalOperationValidationException(
                    "Resolution summary is required.",
                    nameof(resolutionSummary));
            }

            if (resolutionSummary.Length > Ticket.MaximumResolutionSummaryLength)
            {
                throw new TicketTechnicalOperationValidationException(
                    $"Resolution summary cannot exceed {Ticket.MaximumResolutionSummaryLength} characters.",
                    nameof(resolutionSummary));
            }
        }

        if (operation == TechnicalOperation.ChangePriority &&
            (priority is null || !Enum.IsDefined(priority.Value)))
        {
            throw new TicketTechnicalOperationValidationException(
                "Priority must be a defined value.",
                nameof(priority));
        }
    }

    private static void ApplyMutation(
        Ticket ticket,
        TechnicalOperation operation,
        string? resolutionSummary,
        TicketPriority? priority,
        DateTimeOffset occurredAt)
    {
        switch (operation)
        {
            case TechnicalOperation.StartWork:
                ticket.StartWork(occurredAt);
                break;
            case TechnicalOperation.Wait:
                ticket.Wait(occurredAt);
                break;
            case TechnicalOperation.Resume:
                ticket.Resume(occurredAt);
                break;
            case TechnicalOperation.Resolve:
                ticket.Resolve(resolutionSummary!, occurredAt);
                break;
            case TechnicalOperation.ChangePriority:
                ticket.ChangePriority(priority!.Value, occurredAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static IReadOnlyCollection<TicketHistory> CreateHistories(
        Ticket ticket,
        Guid performedByUserId,
        TechnicalOperation operation,
        TicketStatus oldStatus,
        TicketPriority oldPriority,
        string? resolutionSummary,
        DateTimeOffset occurredAt)
    {
        if (operation == TechnicalOperation.Resolve)
        {
            return
            [
                History(
                    ticket.Id,
                    performedByUserId,
                    TicketHistoryEventType.Resolved,
                    null,
                    resolutionSummary,
                    occurredAt),
                History(
                    ticket.Id,
                    performedByUserId,
                    TicketHistoryEventType.StatusChanged,
                    oldStatus.ToString(),
                    ticket.Status.ToString(),
                    occurredAt)
            ];
        }

        if (operation == TechnicalOperation.ChangePriority)
        {
            return
            [
                History(
                    ticket.Id,
                    performedByUserId,
                    TicketHistoryEventType.PriorityChanged,
                    oldPriority.ToString(),
                    ticket.Priority.ToString(),
                    occurredAt)
            ];
        }

        return
        [
            History(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.StatusChanged,
                oldStatus.ToString(),
                ticket.Status.ToString(),
                occurredAt)
        ];
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

    private enum TechnicalOperation
    {
        StartWork,
        Wait,
        Resume,
        Resolve,
        ChangePriority
    }
}
