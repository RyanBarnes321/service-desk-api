using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ServiceDesk.Api.Contracts.Authentication;
using ServiceDesk.Api.Tests.Security;
using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Enums;
using ServiceDesk.Infrastructure.Security;

namespace ServiceDesk.Api.Tests.Endpoints;

public class AuthenticationEndpointsTests
{
    [Fact]
    public async Task Login_ValidCredentials_ReturnsSignedTokenWithRequiredClaimsAndExpiration()
    {
        var passwordService = new AspNetCorePasswordService();
        var user = new LoginUser(
            Guid.NewGuid(),
            "admin@example.com",
            passwordService.Hash("correct-password"),
            UserRole.Administrator,
            true);
        await using var factory = new AuthenticationApiFactory(user);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginHttpRequest("  ADMIN@Example.COM ", "correct-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.Equal("admin@example.com", factory.Query.NormalizedEmail);

        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            body.AccessToken,
            ValidationParameters(),
            out var validatedToken);
        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("admin@example.com", principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal(UserRole.Administrator.ToString(), principal.FindFirst("role")?.Value);
        Assert.Equal(body.ExpiresAt.UtcDateTime, jwt.ValidTo);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("inactive")]
    public async Task Login_InvalidCredentials_ReturnsIdenticalGenericUnauthorized(string scenario)
    {
        var passwordService = new AspNetCorePasswordService();
        var user = scenario == "missing"
            ? null
            : new LoginUser(
                Guid.NewGuid(),
                "user@example.com",
                passwordService.Hash("correct-password"),
                UserRole.Employee,
                scenario != "inactive");
        await using var factory = new AuthenticationApiFactory(user);
        using var client = factory.CreateHttpsClient();
        var password = scenario == "wrong" ? "wrong-password" : "correct-password";

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginHttpRequest("user@example.com", password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid credentials", problem?.Title);
        Assert.DoesNotContain("hash", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "correct-password")]
    [InlineData("user@example.com", " ")]
    public async Task Login_BlankCredentials_ReturnsSameGenericUnauthorizedAsMissingUser(
        string email,
        string password)
    {
        await using var blankFactory = new AuthenticationApiFactory(null);
        using var blankClient = blankFactory.CreateHttpsClient();
        var blankResponse = await blankClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginHttpRequest(email, password));
        var blankProblem = await blankResponse.Content.ReadFromJsonAsync<ProblemDetails>();

        await using var missingFactory = new AuthenticationApiFactory(null);
        using var missingClient = missingFactory.CreateHttpsClient();
        var missingResponse = await missingClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginHttpRequest("missing@example.com", "correct-password"));
        var missingProblem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, blankResponse.StatusCode);
        Assert.Equal(missingResponse.StatusCode, blankResponse.StatusCode);
        Assert.Equal(missingProblem?.Title, blankProblem?.Title);
        Assert.Equal(missingProblem?.Status, blankProblem?.Status);
        Assert.Equal(missingProblem?.Type, blankProblem?.Type);
        Assert.Equal(missingProblem?.Detail, blankProblem?.Detail);
        Assert.Equal("Invalid credentials", blankProblem?.Title);
        Assert.Equal(0, blankFactory.Query.CallCount);
    }

    [Fact]
    public async Task Login_MalformedStoredHash_ReturnsGenericUnauthorized()
    {
        var user = new LoginUser(
            Guid.NewGuid(),
            "user@example.com",
            "not-valid-base64!",
            UserRole.Employee,
            true);
        await using var factory = new AuthenticationApiFactory(user);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginHttpRequest("user@example.com", "any-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid credentials", problem?.Title);
    }

    private static TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.SigningKey)),
        ValidateIssuer = true,
        ValidIssuer = TestJwt.Issuer,
        ValidateAudience = true,
        ValidAudience = TestJwt.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    private sealed class AuthenticationApiFactory(LoginUser? loginUser) : WebApplicationFactory<Program>
    {
        public StubLoginUserQuery Query { get; } = new(loginUser);

        public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

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
                services.RemoveAll<ILoginUserQuery>();
                services.AddSingleton<ILoginUserQuery>(Query);
            });
        }
    }

    private sealed class StubLoginUserQuery(LoginUser? result) : ILoginUserQuery
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
}
