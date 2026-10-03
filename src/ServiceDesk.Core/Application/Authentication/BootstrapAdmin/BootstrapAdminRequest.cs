namespace ServiceDesk.Core.Application.Authentication.BootstrapAdmin;

public sealed record BootstrapAdminRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName);
