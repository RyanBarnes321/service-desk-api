using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.CreateTicket;

public sealed class CreateTicketUseCase(
    ICreateTicketPersistence persistence,
    TimeProvider timeProvider)
{
    public async Task<CreateTicketResult> ExecuteAsync(
        CreateTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var createdAt = timeProvider.GetUtcNow();
        var ticket = Ticket.Create(
            command.Title,
            command.Description,
            command.Category,
            command.Priority,
            command.CreatedByUserId,
            createdAt);
        var history = TicketHistory.Create(
            ticket.Id,
            command.CreatedByUserId,
            TicketHistoryEventType.Created,
            null,
            null,
            createdAt);

        await persistence.PersistAsync(ticket, history, cancellationToken);

        return CreateTicketResult.FromTicket(ticket);
    }
}
