using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed record CreateTicketCommentTarget(Guid Id, TicketStatus Status);
