using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ServiceDesk.Api.Tests.Security;

namespace ServiceDesk.Api.Tests.Endpoints;

public class OpenApiDocumentTests
{
    [Fact]
    public async Task Health_DatabaseUnavailable_ReturnsServiceUnavailable()
    {
        await using var factory = new OpenApiApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ActualHttpOperations_AreDocumentedWithSecurityAndResponses()
    {
        await using var factory = new OpenApiApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var documentedOperations = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation =>
                    $"{operation.Name.ToUpperInvariant()} {NormalizeRoute(path.Name)}"))
            .ToHashSet(StringComparer.Ordinal);

        var expectedOperations = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is not null)
            .Where(endpoint => !NormalizeRoute(endpoint.RoutePattern.RawText!)
                .StartsWith("/openapi", StringComparison.Ordinal))
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method =>
                    $"{method.ToUpperInvariant()} {NormalizeRoute(endpoint.RoutePattern.RawText!)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            expectedOperations.SetEquals(documentedOperations),
            $"Expected: {string.Join(", ", expectedOperations.Order())}; " +
            $"documented: {string.Join(", ", documentedOperations.Order())}");

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject()
                         .Where(operation => HttpMethods.Contains(operation.Name)))
            {
                Assert.False(string.IsNullOrWhiteSpace(operation.Value.GetProperty("summary").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(operation.Value.GetProperty("description").GetString()));
                Assert.NotEmpty(operation.Value.GetProperty("responses").EnumerateObject());

                if (path.Name is not "/api/auth/login" and not "/health")
                {
                    var security = Assert.Single(operation.Value.GetProperty("security").EnumerateArray());
                    Assert.True(security.TryGetProperty("Bearer", out _));
                }
            }
        }

        var bearer = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var loginExample = paths.GetProperty("/api/auth/login")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example");
        Assert.Equal("admin@example.test", loginExample.GetProperty("email").GetString());

        var createTicketPath = paths.EnumerateObject()
            .Single(path => path.Name.TrimEnd('/') == "/api/tickets")
            .Value;
        var createTicketExample = createTicketPath
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example");
        Assert.Equal("Network", createTicketExample.GetProperty("category").GetString());

        var getUserResponses = paths.GetProperty("/api/users/{id}")
            .GetProperty("get")
            .GetProperty("responses");
        Assert.True(getUserResponses.TryGetProperty("400", out _));
    }

    private static readonly HashSet<string> HttpMethods =
        ["get", "post", "put", "delete", "patch"];

    private static string NormalizeRoute(string route) =>
        (route.StartsWith('/') ? route : $"/{route}")
        .Replace(":guid", string.Empty, StringComparison.Ordinal)
        .TrimEnd('/');

    private sealed class OpenApiApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
            builder.UseSetting(
                "ConnectionStrings:ServiceDesk",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");
        }
    }
}
