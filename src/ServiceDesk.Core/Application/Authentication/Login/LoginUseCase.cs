using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Application.Authentication.Login;

public sealed class LoginUseCase(
    ILoginUserQuery userQuery,
    IPasswordService passwordService,
    IAccessTokenIssuer tokenIssuer,
    TimeProvider timeProvider)
{
    public async Task<LoginResult> ExecuteAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email, "email");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password, "password");

        var normalizedEmail = EmailAddress.Normalize(request.Email);
        var user = await userQuery.FindByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        var passwordHash = user is { IsActive: true }
            ? user.PasswordHash
            : passwordService.DummyHash;
        var verification = passwordService.Verify(passwordHash, request.Password);

        if (user is null || !user.IsActive || verification == PasswordVerificationOutcome.Failed)
        {
            return LoginResult.InvalidCredentials;
        }

        // Rehashing is deliberately deferred until a password-hash update port is introduced.
        var currentTime = timeProvider.GetUtcNow().ToUniversalTime();
        var issuedAt = new DateTimeOffset(
            currentTime.Ticks - (currentTime.Ticks % TimeSpan.TicksPerSecond),
            TimeSpan.Zero);
        var expiresAt = issuedAt.AddMinutes(60);
        var token = tokenIssuer.Issue(
            user.Id,
            normalizedEmail,
            user.Role,
            issuedAt,
            expiresAt);

        return LoginResult.Success(token, expiresAt);
    }
}
