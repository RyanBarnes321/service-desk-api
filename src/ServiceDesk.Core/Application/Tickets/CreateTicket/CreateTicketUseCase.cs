using ServiceDesk.Core.Entities;

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

        var ticket = Ticket.Create(
            command.Title,
            command.Description,
            command.Category,
            command.Priority,
            command.CreatedByUserId,
            timeProvider.GetUtcNow());

        await persistence.PersistAsync(ticket, cancellationToken);

        return CreateTicketResult.FromTicket(ticket);
    }
}
