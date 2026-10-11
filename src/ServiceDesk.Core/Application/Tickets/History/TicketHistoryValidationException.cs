namespace ServiceDesk.Core.Application.Tickets.History;

public sealed class TicketHistoryValidationException(string message, string paramName)
    : ArgumentException(message, paramName);
