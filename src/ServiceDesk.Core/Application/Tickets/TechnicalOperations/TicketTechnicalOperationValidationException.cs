namespace ServiceDesk.Core.Application.Tickets.TechnicalOperations;

public sealed class TicketTechnicalOperationValidationException(
    string message,
    string parameterName) : ArgumentException(message, parameterName);
