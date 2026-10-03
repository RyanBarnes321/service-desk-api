using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets;

public sealed class TicketVisibilityScope
{
    private TicketVisibilityScope(Guid? createdByUserId)
    {
        CreatedByUserId = createdByUserId;
    }

    public Guid? CreatedByUserId { get; }

    public static TicketVisibilityScope For(RequestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return actor.Role switch
        {
            UserRole.Employee => new TicketVisibilityScope(actor.UserId),
            UserRole.Technician => new TicketVisibilityScope((Guid?)null),
            UserRole.Administrator => new TicketVisibilityScope((Guid?)null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(actor),
                actor.Role,
                "Actor role is not supported.")
        };
    }
}
