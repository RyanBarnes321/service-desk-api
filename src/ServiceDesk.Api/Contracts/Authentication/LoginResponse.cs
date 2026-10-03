namespace ServiceDesk.Api.Contracts.Authentication;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
