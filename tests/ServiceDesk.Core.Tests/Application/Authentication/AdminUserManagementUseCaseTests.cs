using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.UserManagement;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Tests.Application.Authentication;

public class AdminUserManagementUseCaseTests
{
    private static readonly Guid AdministratorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_Administrator_DelegatesDefaultsAndReturnsSafeProjection()
    {
        var expected = new ListAdminUsersResult([], 1, 20, 0, 0);
        var query = new RecordingQuery(expected);

        var execution = await new ListAdminUsersUseCase(query).ExecuteAsync(
            new ListAdminUsersRequest(),
            Administrator());

        Assert.Equal(AdminUserOutcome.Success, execution.Outcome);
        Assert.Same(expected, execution.Result);
        Assert.Equal(new ListAdminUsersRequest(1, 20), query.Request);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task ReadAsync_NonAdministrator_ReturnsForbiddenWithoutQuery(UserRole role)
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));
        var actor = new RequestActor(Guid.NewGuid(), role);

        var list = await new ListAdminUsersUseCase(query).ExecuteAsync(
            new ListAdminUsersRequest(), actor);
        var get = await new GetAdminUserUseCase(query).ExecuteAsync(Guid.NewGuid(), actor);

        Assert.Equal(AdminUserOutcome.Forbidden, list.Outcome);
        Assert.Equal(AdminUserOutcome.Forbidden, get.Outcome);
        Assert.Equal(0, query.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task ReadAsync_NonAdministratorInvalidInput_StillReturnsForbidden(UserRole role)
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));
        var actor = new RequestActor(Guid.NewGuid(), role);

        var list = await new ListAdminUsersUseCase(query).ExecuteAsync(
            new ListAdminUsersRequest(0, 101), actor);
        var get = await new GetAdminUserUseCase(query).ExecuteAsync(Guid.Empty, actor);

        Assert.Equal(AdminUserOutcome.Forbidden, list.Outcome);
        Assert.Equal(AdminUserOutcome.Forbidden, get.Outcome);
        Assert.Equal(0, query.CallCount);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task ListAsync_InvalidPagination_ThrowsWithoutQuery(
        int page,
        int pageSize,
        string parameterName)
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));

        var exception = await Assert.ThrowsAsync<AdminUserValidationException>(() =>
            new ListAdminUsersUseCase(query).ExecuteAsync(
                new ListAdminUsersRequest(page, pageSize), Administrator()));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task GetAsync_ExistingAndMissing_ReturnExpectedOutcomes()
    {
        var id = Guid.NewGuid();
        var user = Result(id);
        var foundQuery = new RecordingQuery(new([], 1, 20, 0, 0), user);
        var missingQuery = new RecordingQuery(new([], 1, 20, 0, 0));

        var found = await new GetAdminUserUseCase(foundQuery).ExecuteAsync(id, Administrator());
        var missing = await new GetAdminUserUseCase(missingQuery).ExecuteAsync(id, Administrator());

        Assert.Equal(AdminUserOutcome.Success, found.Outcome);
        Assert.Same(user, found.User);
        Assert.Equal(AdminUserOutcome.NotFound, missing.Outcome);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task CreateAsync_AllowedRole_NormalizesHashesAndPersists(UserRole role)
    {
        var persistence = new RecordingCreatePersistence(AdminUserOutcome.Success);
        var password = new RecordingPasswordService();
        var time = new CountingTimeProvider(CreatedAt);

        var execution = await new CreateAdminUserUseCase(persistence, password, time).ExecuteAsync(
            new CreateAdminUserRequest(
                "  NEW.User@Example.COM ", "secure password", " Jane ", " Doe ", role),
            Administrator());

        Assert.Equal(AdminUserOutcome.Success, execution.Outcome);
        Assert.Equal("new.user@example.com", execution.User?.Email);
        Assert.Equal("Jane", execution.User?.FirstName);
        Assert.Equal("Doe", execution.User?.LastName);
        Assert.Equal(role, execution.User?.Role);
        Assert.True(execution.User?.IsActive);
        Assert.Equal(CreatedAt, execution.User?.CreatedAt);
        Assert.Equal("secure password", password.HashedPassword);
        Assert.Equal("hashed:secure password", persistence.User?.PasswordHash);
        Assert.Equal(1, persistence.CallCount);
        Assert.Equal(1, time.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData((UserRole)999)]
    public async Task CreateAsync_DisallowedRole_ThrowsWithoutHashOrPersistence(UserRole role)
    {
        var persistence = new RecordingCreatePersistence(AdminUserOutcome.Success);
        var password = new RecordingPasswordService();

        var exception = await Assert.ThrowsAsync<AdminUserValidationException>(() =>
            new CreateAdminUserUseCase(
                persistence,
                password,
                new CountingTimeProvider(CreatedAt)).ExecuteAsync(
                ValidCreate() with { Role = role },
                Administrator()));

        Assert.Equal("role", exception.ParamName);
        Assert.Null(password.HashedPassword);
        Assert.Equal(0, persistence.CallCount);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    public async Task CreateAsync_InvalidRequiredField_ThrowsWithoutPersistence(string field)
    {
        var request = field switch
        {
            "email" => ValidCreate() with { Email = "not an email" },
            "password" => ValidCreate() with { Password = " " },
            "firstName" => ValidCreate() with { FirstName = " " },
            "lastName" => ValidCreate() with { LastName = new string('x', 101) },
            _ => throw new InvalidOperationException()
        };
        var persistence = new RecordingCreatePersistence(AdminUserOutcome.Success);

        var exception = await Assert.ThrowsAsync<AdminUserValidationException>(() =>
            new CreateAdminUserUseCase(
                persistence,
                new RecordingPasswordService(),
                new CountingTimeProvider(CreatedAt)).ExecuteAsync(request, Administrator()));

        Assert.Equal(field, exception.ParamName);
        Assert.Equal(0, persistence.CallCount);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsSafeConflictWithoutUser()
    {
        var persistence = new RecordingCreatePersistence(AdminUserOutcome.DuplicateEmail);

        var execution = await new CreateAdminUserUseCase(
            persistence,
            new RecordingPasswordService(),
            new CountingTimeProvider(CreatedAt)).ExecuteAsync(ValidCreate(), Administrator());

        Assert.Equal(AdminUserOutcome.DuplicateEmail, execution.Outcome);
        Assert.Null(execution.User);
    }

    [Fact]
    public async Task CreateAsync_NonAdministrator_ReturnsForbiddenBeforeValidationOrHash()
    {
        var password = new RecordingPasswordService();
        var persistence = new RecordingCreatePersistence(AdminUserOutcome.Success);

        var execution = await new CreateAdminUserUseCase(
            persistence,
            password,
            new CountingTimeProvider(CreatedAt)).ExecuteAsync(
            ValidCreate() with { Email = null },
            new RequestActor(Guid.NewGuid(), UserRole.Employee));

        Assert.Equal(AdminUserOutcome.Forbidden, execution.Outcome);
        Assert.Null(password.HashedPassword);
        Assert.Equal(0, persistence.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task Mutation_NonAdministrator_ReturnsForbiddenWithoutPersistence(UserRole role)
    {
        var persistence = new RecordingMutationPersistence();
        var useCase = new MutateAdminUserUseCase(persistence);
        var actor = new RequestActor(Guid.NewGuid(), role);

        var roleResult = await useCase.ChangeRoleAsync(Guid.NewGuid(), UserRole.Employee, actor);
        var activationResult = await useCase.ActivateAsync(Guid.NewGuid(), actor);

        Assert.Equal(AdminUserOutcome.Forbidden, roleResult.Outcome);
        Assert.Equal(AdminUserOutcome.Forbidden, activationResult.Outcome);
        Assert.Equal(0, persistence.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task Mutation_NonAdministratorInvalidInput_StillReturnsForbidden(UserRole role)
    {
        var persistence = new RecordingMutationPersistence();
        var useCase = new MutateAdminUserUseCase(persistence);
        var actor = new RequestActor(Guid.NewGuid(), role);

        var roleResult = await useCase.ChangeRoleAsync(Guid.NewGuid(), (UserRole)999, actor);
        var activationResult = await useCase.SetActivationAsync(Guid.NewGuid(), null, actor);

        Assert.Equal(AdminUserOutcome.Forbidden, roleResult.Outcome);
        Assert.Equal(AdminUserOutcome.Forbidden, activationResult.Outcome);
        Assert.Equal(0, persistence.CallCount);
    }

    [Fact]
    public async Task Mutation_SelfDemotionAndDeactivation_ReturnSelfLockoutWithoutPersistence()
    {
        var persistence = new RecordingMutationPersistence();
        var useCase = new MutateAdminUserUseCase(persistence);
        var actor = Administrator();

        var demotion = await useCase.ChangeRoleAsync(actor.UserId, UserRole.Technician, actor);
        var deactivation = await useCase.DeactivateAsync(actor.UserId, actor);

        Assert.Equal(AdminUserOutcome.SelfLockout, demotion.Outcome);
        Assert.Equal(AdminUserOutcome.SelfLockout, deactivation.Outcome);
        Assert.Equal(0, persistence.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task Mutation_ValidOperations_DelegateExactActorTargetAndValues(UserRole role)
    {
        var persistence = new RecordingMutationPersistence();
        var useCase = new MutateAdminUserUseCase(persistence);
        var target = Guid.NewGuid();

        await useCase.ChangeRoleAsync(target, role, Administrator());
        Assert.Equal(AdminUserMutation.ChangeRole, persistence.Request?.Mutation);
        Assert.Equal(role, persistence.Request?.Role);
        Assert.Equal(AdministratorId, persistence.Request?.ActingAdministratorId);
        Assert.Equal(target, persistence.Request?.TargetUserId);
        await useCase.DeactivateAsync(target, Administrator());
        Assert.Equal(AdminUserMutation.Deactivate, persistence.Request?.Mutation);
        await useCase.ActivateAsync(target, Administrator());
        Assert.Equal(AdminUserMutation.Activate, persistence.Request?.Mutation);
        Assert.Equal(3, persistence.CallCount);
    }

    [Fact]
    public async Task PreCancelledOperations_DoNotCallDependencies()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var query = new RecordingQuery(new([], 1, 20, 0, 0));
        var mutation = new RecordingMutationPersistence();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ListAdminUsersUseCase(query).ExecuteAsync(
                new ListAdminUsersRequest(), Administrator(), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MutateAdminUserUseCase(mutation).ActivateAsync(
                Guid.NewGuid(), Administrator(), source.Token));

        Assert.Equal(0, query.CallCount);
        Assert.Equal(0, mutation.CallCount);
    }

    private static RequestActor Administrator() => new(AdministratorId, UserRole.Administrator);
    private static CreateAdminUserRequest ValidCreate() =>
        new("new@example.com", "password", "Jane", "Doe", UserRole.Employee);
    private static AdminUserResult Result(Guid id) =>
        new(id, "user@example.com", "Jane", "Doe", UserRole.Employee, true, CreatedAt);

    private sealed class RecordingQuery(
        ListAdminUsersResult listResult,
        AdminUserResult? user = null) : IAdminUserQuery
    {
        public int CallCount { get; private set; }
        public ListAdminUsersRequest? Request { get; private set; }
        public Task<ListAdminUsersResult> ListAsync(
            ListAdminUsersRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(listResult);
        }
        public Task<AdminUserResult?> FindAsync(Guid userId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(user?.Id == userId ? user : null);
        }
    }

    private sealed class RecordingCreatePersistence(AdminUserOutcome outcome)
        : ICreateAdminUserPersistence
    {
        public int CallCount { get; private set; }
        public User? User { get; private set; }
        public Task<AdminUserOutcome> CreateAsync(User user, CancellationToken cancellationToken)
        {
            CallCount++;
            User = user;
            return Task.FromResult(outcome);
        }
    }

    private sealed class RecordingMutationPersistence : IAdminUserMutationPersistence
    {
        public int CallCount { get; private set; }
        public AdminUserMutationRequest? Request { get; private set; }
        public Task<AdminUserExecution> ExecuteAsync(
            AdminUserMutationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(new AdminUserExecution(AdminUserOutcome.Success, Result(request.TargetUserId)));
        }
    }

    private sealed class RecordingPasswordService : IPasswordService
    {
        public string DummyHash => "dummy";
        public string? HashedPassword { get; private set; }
        public string Hash(string password)
        {
            HashedPassword = password;
            return $"hashed:{password}";
        }
        public PasswordVerificationOutcome Verify(string passwordHash, string password) =>
            PasswordVerificationOutcome.Failed;
    }

    private sealed class CountingTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public int CallCount { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return timestamp;
        }
    }
}
