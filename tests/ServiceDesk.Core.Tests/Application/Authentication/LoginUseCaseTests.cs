using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Tests.Application.Authentication;

public class LoginUseCaseTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 10, 2, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PasswordVerificationOutcome.Success)]
    [InlineData(PasswordVerificationOutcome.SuccessRehashNeeded)]
    public async Task ExecuteAsync_ValidCredentials_NormalizesAndIssuesSixtyMinuteToken(
        PasswordVerificationOutcome verification)
    {
        var user = ActiveUser();
        var query = new RecordingLoginQuery(user);
        var password = new StubPasswordService(verification);
        var issuer = new RecordingTokenIssuer();
        var useCase = CreateUseCase(query, password, issuer);

        var result = await useCase.ExecuteAsync(new LoginRequest("  USER@Example.COM ", "secret"));

        Assert.True(result.IsSuccess);
        Assert.Equal("issued-token", result.AccessToken);
        Assert.Equal(CurrentTime.AddMinutes(60), result.ExpiresAt);
        Assert.Equal("user@example.com", query.NormalizedEmail);
        Assert.Equal(user.Id, issuer.UserId);
        Assert.Equal("user@example.com", issuer.Email);
        Assert.Equal(user.Role, issuer.Role);
        Assert.Equal(CurrentTime, issuer.IssuedAt);
        Assert.Equal(CurrentTime.AddMinutes(60), issuer.ExpiresAt);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("inactive")]
    public async Task ExecuteAsync_InvalidCredentials_ReturnsSameGenericOutcome(string scenario)
    {
        var user = scenario == "missing"
            ? null
            : ActiveUser() with { IsActive = scenario != "inactive" };
        var verification = scenario == "wrong"
            ? PasswordVerificationOutcome.Failed
            : PasswordVerificationOutcome.Success;
        var password = new StubPasswordService(verification);
        var issuer = new RecordingTokenIssuer();
        var useCase = CreateUseCase(
            new RecordingLoginQuery(user),
            password,
            issuer);

        var result = await useCase.ExecuteAsync(new LoginRequest("user@example.com", "secret"));

        Assert.Same(LoginResult.InvalidCredentials, result);
        Assert.Equal(0, issuer.CallCount);
        Assert.Equal(1, password.VerifyCallCount);
        Assert.Equal(
            scenario == "wrong" ? user!.PasswordHash : password.DummyHash,
            password.VerifiedHash);
    }

    [Theory]
    [InlineData("", "secret", "email")]
    [InlineData("user@example.com", " ", "password")]
    public async Task ExecuteAsync_BlankInput_ThrowsWithoutLookup(
        string email,
        string passwordValue,
        string parameterName)
    {
        var query = new RecordingLoginQuery(null);
        var useCase = CreateUseCase(query, new StubPasswordService(), new RecordingTokenIssuer());

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => useCase.ExecuteAsync(new LoginRequest(email, passwordValue)));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_ThrowsWithoutLookup()
    {
        var query = new RecordingLoginQuery(null);
        var useCase = CreateUseCase(query, new StubPasswordService(), new RecordingTokenIssuer());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(
                new LoginRequest("user@example.com", "secret"),
                cancellationSource.Token));

        Assert.Equal(0, query.CallCount);
    }

    private static LoginUseCase CreateUseCase(
        ILoginUserQuery query,
        IPasswordService password,
        IAccessTokenIssuer issuer)
    {
        return new LoginUseCase(query, password, issuer, new FixedTimeProvider(CurrentTime));
    }

    private static LoginUser ActiveUser() => new(
        Guid.NewGuid(),
        "user@example.com",
        "stored-hash",
        UserRole.Employee,
        true);

    private sealed class RecordingLoginQuery(LoginUser? result) : ILoginUserQuery
    {
        public int CallCount { get; private set; }
        public string? NormalizedEmail { get; private set; }

        public Task<LoginUser?> FindByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
        {
            CallCount++;
            NormalizedEmail = normalizedEmail;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPasswordService(
        PasswordVerificationOutcome verification = PasswordVerificationOutcome.Success)
        : IPasswordService
    {
        public string DummyHash { get; } = "valid-dummy-hash";
        public int VerifyCallCount { get; private set; }
        public string? VerifiedHash { get; private set; }

        public string Hash(string password) => "unused";

        public PasswordVerificationOutcome Verify(string passwordHash, string password)
        {
            VerifyCallCount++;
            VerifiedHash = passwordHash;
            return verification;
        }
    }

    private sealed class RecordingTokenIssuer : IAccessTokenIssuer
    {
        public int CallCount { get; private set; }
        public Guid UserId { get; private set; }
        public string? Email { get; private set; }
        public UserRole Role { get; private set; }
        public DateTimeOffset IssuedAt { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }

        public string Issue(
            Guid userId,
            string normalizedEmail,
            UserRole role,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
        {
            CallCount++;
            UserId = userId;
            Email = normalizedEmail;
            Role = role;
            IssuedAt = issuedAt;
            ExpiresAt = expiresAt;
            return "issued-token";
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
