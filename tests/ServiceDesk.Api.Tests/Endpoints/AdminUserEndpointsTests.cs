using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceDesk.Api.Contracts.Users;
using ServiceDesk.Api.Tests.Security;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.UserManagement;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Api.Tests.Endpoints;

public class AdminUserEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("POST", "/api/users")]
    [InlineData("PUT", "/api/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/role")]
    [InlineData("PUT", "/api/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/activation")]
    public async Task UserManagement_Anonymous_ReturnsUnauthorized(string method, string path)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET") request.Content = JsonContent.Create(new { role = "Employee" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task UserManagement_NonAdministrator_ReturnsForbiddenWithoutDependencies(UserRole role)
    {
        await using var factory = new AdminUserApiFactory(persistedRole: role);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Store.QueryCallCount);
    }

    [Theory]
    [InlineData("role", "{}")]
    [InlineData("activation", "{}")]
    public async Task Mutation_NonAdministratorMissingInput_ReturnsForbidden(
        string operation,
        string json)
    {
        var target = Guid.NewGuid();
        await using var factory = new AdminUserApiFactory(persistedRole: UserRole.Employee);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PutAsync($"/api/users/{target}/{operation}", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Store.MutationCallCount);
    }

    [Theory]
    [InlineData("/api/users?page=0&pageSize=101")]
    [InlineData("/api/users/00000000-0000-0000-0000-000000000000")]
    public async Task Read_NonAdministratorInvalidInput_ReturnsForbidden(string path)
    {
        await using var factory = new AdminUserApiFactory(persistedRole: UserRole.Technician);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Store.QueryCallCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UserManagement_InactiveOrMissingCurrentUser_ReturnsForbidden(
        bool active,
        bool exists)
    {
        await using var factory = new AdminUserApiFactory(active: active, exists: exists);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Store.QueryCallCount);
    }

    [Fact]
    public async Task List_Administrator_ReturnsDeterministicMetadataWithoutPasswordHash()
    {
        var user = UserResult(Guid.NewGuid(), UserRole.Technician);
        await using var factory = new AdminUserApiFactory(
            listResult: new ListAdminUsersResult([user], 1, 20, 1, 1));
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var body = JsonSerializer.Deserialize<ListAdminUsersResponse>(json, JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(user.Id, Assert.Single(body.Items).Id);
        Assert.Equal(UserRole.Technician, body.Items[0].Role);
        Assert.Equal(1, body.TotalCount);
        Assert.Equal(new ListAdminUsersRequest(), factory.Store.ListRequest);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task List_InvalidPagination_ReturnsBadRequestWithoutQuery(string query)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/users{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Store.QueryCallCount);
    }

    [Fact]
    public async Task Get_ExistingAndMissing_ReturnOkAndNotFound()
    {
        var user = UserResult(Guid.NewGuid(), UserRole.Employee);
        await using var foundFactory = new AdminUserApiFactory(foundUser: user);
        using var foundClient = foundFactory.CreateHttpsClient();
        await using var missingFactory = new AdminUserApiFactory();
        using var missingClient = missingFactory.CreateHttpsClient();

        var found = await foundClient.GetAsync($"/api/users/{user.Id}");
        var missing = await missingClient.GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task Create_ValidAccount_ReturnsCreatedNormalizedSafeBody(UserRole role)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/users", new CreateAdminUserHttpRequest(
            "  New.User@Example.COM ", "secret password", " Jane ", " Doe ", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var body = JsonSerializer.Deserialize<AdminUserResponse>(json, JsonOptions);
        Assert.Equal("new.user@example.com", body?.Email);
        Assert.Equal(role, body?.Role);
        Assert.Equal("secret password", factory.Password.HashedPassword);
        Assert.Equal("hashed:secret password", factory.Store.CreatedUser?.PasswordHash);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData((UserRole)999)]
    public async Task Create_DisallowedRole_ReturnsBadRequest(UserRole role)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/users", new CreateAdminUserHttpRequest(
            "new@example.com", "password", "Jane", "Doe", role), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Store.CreateCallCount);
    }

    [Fact]
    public async Task Create_DuplicateEmail_ReturnsSafeConflict()
    {
        await using var factory = new AdminUserApiFactory(createOutcome: AdminUserOutcome.DuplicateEmail);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/users", new CreateAdminUserHttpRequest(
            "existing@example.com", "password", "Jane", "Doe", UserRole.Employee), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("User email conflict", problem?.Title);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("activation")]
    public async Task Mutation_SelfLockout_ReturnsConflictWithoutPersistence(string operation)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();
        var path = $"/api/users/{factory.UserId}/{operation}";
        var body = operation == "role"
            ? JsonContent.Create(new ChangeAdminUserRoleRequest(UserRole.Employee), options: JsonOptions)
            : JsonContent.Create(new SetAdminUserActivationRequest(false), options: JsonOptions);

        var response = await client.PutAsync(path, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, factory.Store.MutationCallCount);
    }

    [Theory]
    [InlineData("role", "{}")]
    [InlineData("role", "{\"role\":999}")]
    [InlineData("activation", "{}")]
    public async Task Mutation_AdministratorInvalidInput_ReturnsBadRequestWithoutPersistence(
        string operation,
        string json)
    {
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PutAsync(
            $"/api/users/{Guid.NewGuid()}/{operation}",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Store.MutationCallCount);
    }

    [Theory]
    [InlineData(AdminUserOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(AdminUserOutcome.LastActiveAdministrator, HttpStatusCode.Conflict)]
    [InlineData(AdminUserOutcome.Conflict, HttpStatusCode.Conflict)]
    [InlineData(AdminUserOutcome.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Mutation_MapsPurposeSpecificOutcome(
        AdminUserOutcome outcome,
        HttpStatusCode status)
    {
        var target = Guid.NewGuid();
        await using var factory = new AdminUserApiFactory(mutationOutcome: outcome);
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/users/{target}/role",
            new ChangeAdminUserRoleRequest(UserRole.Technician),
            JsonOptions);

        Assert.Equal(status, response.StatusCode);
    }

    [Theory]
    [InlineData(true, AdminUserMutation.Activate)]
    [InlineData(false, AdminUserMutation.Deactivate)]
    public async Task Activation_ValidRequest_ReturnsUpdatedUser(
        bool isActive,
        AdminUserMutation expectedMutation)
    {
        var target = Guid.NewGuid();
        await using var factory = new AdminUserApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/users/{target}/activation",
            new SetAdminUserActivationRequest(isActive));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedMutation, factory.Store.MutationRequest?.Mutation);
    }

    [Theory]
    [InlineData(UserRole.Administrator, UserRole.Employee, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Employee, UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.Technician, UserRole.Administrator, HttpStatusCode.OK)]
    public async Task List_PersistedRoleOverridesJwtRole(
        UserRole jwtRole,
        UserRole persistedRole,
        HttpStatusCode expected)
    {
        await using var factory = new AdminUserApiFactory(
            jwtRole: jwtRole,
            persistedRole: persistedRole);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(expected, response.StatusCode);
    }

    private static AdminUserResult UserResult(Guid id, UserRole role) =>
        new(id, "user@example.com", "Jane", "Doe", role, true,
            new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    private sealed class AdminUserApiFactory : WebApplicationFactory<Program>
    {
        public AdminUserApiFactory(
            UserRole persistedRole = UserRole.Administrator,
            UserRole jwtRole = UserRole.Administrator,
            bool active = true,
            bool exists = true,
            ListAdminUsersResult? listResult = null,
            AdminUserResult? foundUser = null,
            AdminUserOutcome createOutcome = AdminUserOutcome.Success,
            AdminUserOutcome mutationOutcome = AdminUserOutcome.Success)
        {
            UserId = Guid.NewGuid();
            JwtRole = jwtRole;
            CurrentUser = new StubCurrentUserState(UserId, persistedRole, active, exists);
            Store = new StubAdminUserStore(
                listResult ?? new ListAdminUsersResult([], 1, 20, 0, 0),
                foundUser,
                createOutcome,
                mutationOutcome);
            Password = new RecordingPasswordService();
        }

        public Guid UserId { get; }
        public UserRole JwtRole { get; }
        public StubCurrentUserState CurrentUser { get; }
        public StubAdminUserStore Store { get; }
        public RecordingPasswordService Password { get; }

        public HttpClient CreateHttpsClient(bool authenticated = true)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            if (authenticated)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer", TestJwt.Create(UserId, role: JwtRole));
            }
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestJwt.Configuration));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUserStateQuery>();
                services.RemoveAll<IAdminUserQuery>();
                services.RemoveAll<ICreateAdminUserPersistence>();
                services.RemoveAll<IAdminUserMutationPersistence>();
                services.RemoveAll<IPasswordService>();
                services.AddSingleton<ICurrentUserStateQuery>(CurrentUser);
                services.AddSingleton<IAdminUserQuery>(Store);
                services.AddSingleton<ICreateAdminUserPersistence>(Store);
                services.AddSingleton<IAdminUserMutationPersistence>(Store);
                services.AddSingleton<IPasswordService>(Password);
            });
        }
    }

    private sealed class StubCurrentUserState(
        Guid userId,
        UserRole role,
        bool active,
        bool exists) : ICurrentUserStateQuery
    {
        public Task<CurrentUserState?> FindAsync(Guid requestedId, CancellationToken cancellationToken)
        {
            CurrentUserState? state = exists && requestedId == userId
                ? new(userId, role, active)
                : null;
            return Task.FromResult(state);
        }
    }

    private sealed class StubAdminUserStore(
        ListAdminUsersResult listResult,
        AdminUserResult? foundUser,
        AdminUserOutcome createOutcome,
        AdminUserOutcome mutationOutcome)
        : IAdminUserQuery, ICreateAdminUserPersistence, IAdminUserMutationPersistence
    {
        public int QueryCallCount { get; private set; }
        public int CreateCallCount { get; private set; }
        public int MutationCallCount { get; private set; }
        public ListAdminUsersRequest? ListRequest { get; private set; }
        public User? CreatedUser { get; private set; }
        public AdminUserMutationRequest? MutationRequest { get; private set; }

        public Task<ListAdminUsersResult> ListAsync(
            ListAdminUsersRequest request, CancellationToken cancellationToken)
        {
            QueryCallCount++;
            ListRequest = request;
            return Task.FromResult(listResult with { Page = request.Page, PageSize = request.PageSize });
        }
        public Task<AdminUserResult?> FindAsync(Guid userId, CancellationToken cancellationToken)
        {
            QueryCallCount++;
            return Task.FromResult(foundUser?.Id == userId ? foundUser : null);
        }
        public Task<AdminUserOutcome> CreateAsync(User user, CancellationToken cancellationToken)
        {
            CreateCallCount++;
            CreatedUser = user;
            return Task.FromResult(createOutcome);
        }
        public Task<AdminUserExecution> ExecuteAsync(
            AdminUserMutationRequest request, CancellationToken cancellationToken)
        {
            MutationCallCount++;
            MutationRequest = request;
            var user = UserResult(request.TargetUserId, request.Role ?? UserRole.Employee) with
            {
                IsActive = request.Mutation != AdminUserMutation.Deactivate
            };
            return Task.FromResult(new AdminUserExecution(
                mutationOutcome,
                mutationOutcome == AdminUserOutcome.Success ? user : null));
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
}
