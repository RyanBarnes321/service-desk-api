using ServiceDesk.Core.Application.Authentication.BootstrapAdmin;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Tests.Application.Authentication;

public class BootstrapAdminUseCaseTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 10, 2, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_EmptyDatabase_NormalizesHashesAndCreatesActiveAdministrator()
    {
        var persistence = new RecordingPersistence(true);
        var password = new RecordingPasswordService();
        var useCase = CreateUseCase(persistence, password);

        var result = await useCase.ExecuteAsync(new BootstrapAdminRequest(
            "  ADMIN@Example.COM ",
            "secret-password",
            "  Alex ",
            " Rivera  "));

        Assert.Equal(BootstrapAdminOutcome.Created, result);
        Assert.Equal(1, persistence.CallCount);
        Assert.Equal("secret-password", password.Password);
        var administrator = Assert.IsType<User>(persistence.Administrator);
        Assert.Equal("admin@example.com", administrator.Email);
        Assert.Equal("generated-hash", administrator.PasswordHash);
        Assert.Equal("Alex", administrator.FirstName);
        Assert.Equal("Rivera", administrator.LastName);
        Assert.Equal(UserRole.Administrator, administrator.Role);
        Assert.True(administrator.IsActive);
        Assert.Equal(CurrentTime, administrator.CreatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_DatabaseContainsAnyUser_ReturnsRefusal()
    {
        var persistence = new RecordingPersistence(false);
        var useCase = CreateUseCase(persistence, new RecordingPasswordService());

        var result = await useCase.ExecuteAsync(ValidRequest());

        Assert.Equal(BootstrapAdminOutcome.RefusedExistingUsers, result);
        Assert.Equal(1, persistence.CallCount);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    public async Task ExecuteAsync_MissingRequiredValue_ThrowsWithoutPersistence(string missing)
    {
        var persistence = new RecordingPersistence(true);
        var useCase = CreateUseCase(persistence, new RecordingPasswordService());
        var request = ValidRequest() with
        {
            Email = missing == "email" ? " " : ValidRequest().Email,
            Password = missing == "password" ? " " : ValidRequest().Password,
            FirstName = missing == "firstName" ? " " : ValidRequest().FirstName,
            LastName = missing == "lastName" ? " " : ValidRequest().LastName
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => useCase.ExecuteAsync(request));

        Assert.Equal(missing, exception.ParamName);
        Assert.Equal(0, persistence.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_ThrowsWithoutPersistence()
    {
        var persistence = new RecordingPersistence(true);
        var useCase = CreateUseCase(persistence, new RecordingPasswordService());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(ValidRequest(), cancellationSource.Token));

        Assert.Equal(0, persistence.CallCount);
    }

    private static BootstrapAdminUseCase CreateUseCase(
        IBootstrapAdminPersistence persistence,
        IPasswordService password)
    {
        return new BootstrapAdminUseCase(
            persistence,
            password,
            new FixedTimeProvider(CurrentTime));
    }

    private static BootstrapAdminRequest ValidRequest() =>
        new("admin@example.com", "secret-password", "Alex", "Rivera");

    private sealed class RecordingPersistence(bool create) : IBootstrapAdminPersistence
    {
        public int CallCount { get; private set; }
        public User? Administrator { get; private set; }

        public Task<bool> CreateIfDatabaseEmptyAsync(
            User administrator,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Administrator = administrator;
            return Task.FromResult(create);
        }
    }

    private sealed class RecordingPasswordService : IPasswordService
    {
        public string DummyHash { get; } = "unused-dummy-hash";
        public string? Password { get; private set; }

        public string Hash(string password)
        {
            Password = password;
            return "generated-hash";
        }

        public PasswordVerificationOutcome Verify(string passwordHash, string password) =>
            PasswordVerificationOutcome.Failed;
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
