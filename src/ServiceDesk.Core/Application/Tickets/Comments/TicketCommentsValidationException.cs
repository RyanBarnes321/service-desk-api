namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed class TicketCommentsValidationException(string message, string paramName)
    : ArgumentException(message, paramName);
