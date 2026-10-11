namespace ServiceDesk.Core.Application.UserManagement;

public sealed class AdminUserValidationException(string message, string paramName)
    : ArgumentException(message, paramName);
