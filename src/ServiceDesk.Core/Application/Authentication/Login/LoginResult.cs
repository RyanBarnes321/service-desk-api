namespace ServiceDesk.Core.Application.Authentication.Login;

public sealed record LoginResult(
    bool IsSuccess,
    string? AccessToken,
    DateTimeOffset? ExpiresAt)
{
    public static LoginResult InvalidCredentials { get; } = new(false, null, null);

    public static LoginResult Success(string accessToken, DateTimeOffset expiresAt) =>
        new(true, accessToken, expiresAt);
}
