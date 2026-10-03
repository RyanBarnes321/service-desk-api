namespace ServiceDesk.Api.Contracts.Authentication;

public sealed record LoginHttpRequest(string Email, string Password);
