using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ServiceDesk.Api.Tests.Security;
using ServiceDesk.Api.Contracts.Tickets;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Tests.Endpoints;

public class TicketEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Post_ValidRequest_ReturnsCreatedBodyLocationAndReadableEnums()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();
        var request = ValidCreateRequest();

        var response = await client.PostAsJsonAsync("/api/tickets", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateTicketResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(request.Title, body.Title);
        Assert.Equal(request.Description, body.Description);
        Assert.Equal(TicketCategory.Network, body.Category);
        Assert.Equal(TicketPriority.High, body.Priority);
        Assert.Equal(TicketStatus.Open, body.Status);
        Assert.Equal(factory.UserId, body.CreatedByUserId);
        Assert.Equal(factory.UserId, factory.Persistence.PersistedTicket?.CreatedByUserId);
        Assert.Equal($"/api/tickets/{body.Id}", response.Headers.Location?.AbsolutePath);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"category\":\"Network\"", json);
        Assert.Contains("\"priority\":\"High\"", json);
        Assert.Contains("\"status\":\"Open\"", json);
        Assert.Equal(1, factory.Persistence.CallCount);
    }

    [Fact]
    public async Task Post_DomainInvalidRequest_ReturnsProblemDetailsWithoutPersistence()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();
        var request = ValidCreateRequest() with { Title = " " };

        var response = await client.PostAsJsonAsync("/api/tickets", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.Persistence.CallCount);
    }

    [Fact]
    public async Task Post_PersistenceThrowsUnrelatedArgumentException_DoesNotTranslateToInvalidRequest()
    {
        var persistenceException = new ArgumentException(
            "Persistence configuration is invalid.",
            "connectionConfiguration");
        await using var factory = new TicketApiFactory(persistenceException: persistenceException);
        using var client = factory.CreateHttpsClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.PostAsJsonAsync("/api/tickets", ValidCreateRequest()));

        Assert.Same(persistenceException, exception);
        Assert.Equal(1, factory.Persistence.CallCount);
    }

    [Fact]
    public async Task Get_ExistingTicket_ReturnsOkBodyAndReadableEnums()
    {
        var ticketId = Guid.NewGuid();
        var ticket = TicketResult(ticketId);
        await using var factory = new TicketApiFactory(ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetTicketResponse>(JsonOptions);
        Assert.Equal(ticket, body is null ? null : new GetTicketResult(
            body.Id,
            body.Title,
            body.Description,
            body.Category,
            body.Priority,
            body.Status,
            body.CreatedByUserId,
            body.AssignedTechnicianId,
            body.ResolutionSummary,
            body.CreatedAt,
            body.UpdatedAt,
            body.ResolvedAt,
            body.ClosedAt));

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"category\":\"Network\"", json);
        Assert.Contains("\"priority\":\"High\"", json);
        Assert.Contains("\"status\":\"Open\"", json);
        Assert.Equal(ticketId, factory.Query.QueriedId);
    }

    [Fact]
    public async Task Get_MissingTicket_ReturnsNotFound()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, factory.Query.CallCount);
    }

    [Fact]
    public async Task Get_EmptyId_ReturnsProblemDetailsWithoutQuerying()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.Query.CallCount);
    }

    [Fact]
    public async Task List_DefaultRequest_ReturnsPageMetadataAndReadableEnums()
    {
        var item = ListItem(Guid.NewGuid());
        var listResult = new ListTicketsResult([item], 1, 20, 1, 1);
        await using var factory = new TicketApiFactory(listResult: listResult);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListTicketsResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(1, body.TotalCount);
        Assert.Equal(1, body.TotalPages);
        var responseItem = Assert.Single(body.Items);
        Assert.Equal(item.Id, responseItem.Id);
        Assert.Equal(item.Title, responseItem.Title);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"category\":\"Network\"", json);
        Assert.Contains("\"priority\":\"High\"", json);
        Assert.Contains("\"status\":\"Assigned\"", json);
        Assert.Equal(new ListTicketsRequest(), factory.ListQuery.Request);
    }

    [Fact]
    public async Task List_FiltersPaginationAndSort_MapsExactNormalizedRequest()
    {
        var technicianId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var createdFrom = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var createdTo = createdFrom.AddDays(1);
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();
        var url = "/api/tickets" +
            $"?status=Assigned&priority=High&category=Network" +
            $"&assignedTechnicianId={technicianId}&createdByUserId={creatorId}" +
            $"&createdFrom={Uri.EscapeDataString(createdFrom.ToString("O"))}" +
            $"&createdTo={Uri.EscapeDataString(createdTo.ToString("O"))}" +
            $"&search={Uri.EscapeDataString("  VPN issue  ")}" +
            "&page=2&pageSize=5&sortField=CreatedAt&sortDirection=Asc";

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ListTicketsRequest(
            TicketStatus.Assigned,
            TicketPriority.High,
            TicketCategory.Network,
            technicianId,
            creatorId,
            createdFrom,
            createdTo,
            "VPN issue",
            2,
            5,
            TicketSortField.CreatedAt,
            TicketSortDirection.Asc), factory.ListQuery.Request);
        Assert.Equal(1, factory.ListQuery.CallCount);
    }

    [Fact]
    public async Task List_InvalidPagination_ReturnsProblemDetailsWithoutQuerying()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.ListQuery.CallCount);
    }

    [Fact]
    public async Task List_UndefinedNumericStatus_ReturnsProblemDetailsWithoutQuerying()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets?status=999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.ListQuery.CallCount);
    }

    [Fact]
    public async Task List_InvalidCreatedRange_ReturnsProblemDetailsWithoutQuerying()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync(
            "/api/tickets?createdFrom=2026-10-03T00:00:00Z&createdTo=2026-10-02T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, factory.ListQuery.CallCount);
    }

    [Fact]
    public async Task List_UnsupportedSortField_ReturnsFrameworkBadRequestWithoutQuerying()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets?sortField=Title");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.ListQuery.CallCount);
    }

    [Theory]
    [InlineData("post", "/api/tickets")]
    [InlineData("get", "/api/tickets")]
    [InlineData("get", "/api/tickets/11111111-1111-1111-1111-111111111111")]
    public async Task TicketRoute_AnonymousRequest_ReturnsUnauthorized(string method, string path)
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: false);

        var response = method == "post"
            ? await client.PostAsJsonAsync(path, ValidCreateRequest())
            : await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TicketRoute_DeactivatedCurrentUserWithValidToken_ReturnsForbidden()
    {
        await using var factory = new TicketApiFactory(isCurrentUserActive: false);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, factory.UserState.CallCount);
    }

    [Fact]
    public async Task TicketRoute_InvalidJwtVariants_ReturnUnauthorizedBeforeActiveUserLookup()
    {
        await using var factory = new TicketApiFactory();
        var tokens = new[]
        {
            TestJwt.Create(
                factory.UserId,
                signingKey: "different-test-only-signing-key-that-is-also-at-least-sixty-four-characters"),
            TestJwt.Create(factory.UserId, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)),
            TestJwt.Create(factory.UserId, issuer: "wrong-issuer"),
            TestJwt.Create(factory.UserId, audience: "wrong-audience"),
            TestJwt.Create(factory.UserId, algorithm: SecurityAlgorithms.HmacSha384)
        };

        foreach (var token in tokens)
        {
            using var client = factory.CreateHttpsClient(accessToken: token);
            var response = await client.GetAsync("/api/tickets");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0, factory.UserState.CallCount);
    }

    [Fact]
    public async Task Post_BodyCreatedByUserIdCannotOverrideAuthenticatedSubject()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient();
        var body = new
        {
            title = "Cannot connect to VPN",
            description = "The VPN client times out during connection.",
            category = TicketCategory.Network,
            priority = TicketPriority.High,
            createdByUserId = Guid.NewGuid()
        };

        var response = await client.PostAsJsonAsync("/api/tickets", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(factory.UserId, factory.Persistence.PersistedTicket?.CreatedByUserId);
    }

    private static CreateTicketRequest ValidCreateRequest()
    {
        return new CreateTicketRequest(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High);
    }

    private static GetTicketResult TicketResult(Guid id)
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

        return new GetTicketResult(
            id,
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            TicketStatus.Open,
            Guid.NewGuid(),
            null,
            null,
            createdAt,
            createdAt,
            null,
            null);
    }

    private static ListTicketItem ListItem(Guid id)
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

        return new ListTicketItem(
            id,
            "Cannot connect to VPN",
            TicketCategory.Network,
            TicketPriority.High,
            TicketStatus.Assigned,
            Guid.NewGuid(),
            Guid.NewGuid(),
            createdAt,
            createdAt.AddMinutes(1));
    }

    private sealed class TicketApiFactory : WebApplicationFactory<Program>
    {
        public TicketApiFactory(
            GetTicketResult? queryResult = null,
            ArgumentException? persistenceException = null,
            ListTicketsResult? listResult = null,
            bool isCurrentUserActive = true)
        {
            UserId = Guid.NewGuid();
            Query = new StubGetTicketQuery(queryResult);
            Persistence = new RecordingCreateTicketPersistence(persistenceException);
            ListQuery = new StubListTicketsQuery(listResult ?? new ListTicketsResult([], 1, 20, 0, 0));
            UserState = new StubCurrentUserStateQuery(isCurrentUserActive);
        }

        public Guid UserId { get; }

        public RecordingCreateTicketPersistence Persistence { get; }

        public StubGetTicketQuery Query { get; }

        public StubListTicketsQuery ListQuery { get; }

        public StubCurrentUserStateQuery UserState { get; }

        public HttpClient CreateHttpsClient(
            bool authenticated = true,
            string? accessToken = null)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

            if (authenticated)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken ?? TestJwt.Create(UserId));
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
                services.RemoveAll<ICreateTicketPersistence>();
                services.RemoveAll<IGetTicketQuery>();
                services.RemoveAll<IListTicketsQuery>();
                services.RemoveAll<ICurrentUserStateQuery>();
                services.AddSingleton<ICreateTicketPersistence>(Persistence);
                services.AddSingleton<IGetTicketQuery>(Query);
                services.AddSingleton<IListTicketsQuery>(ListQuery);
                services.AddSingleton<ICurrentUserStateQuery>(UserState);
            });
        }
    }

    private sealed class RecordingCreateTicketPersistence(ArgumentException? exception = null)
        : ICreateTicketPersistence
    {
        public int CallCount { get; private set; }

        public Ticket? PersistedTicket { get; private set; }

        public Task PersistAsync(
            Ticket ticket,
            TicketHistory history,
            CancellationToken cancellationToken)
        {
            CallCount++;
            PersistedTicket = ticket;

            if (exception is not null)
            {
                throw exception;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentUserStateQuery(bool isActive) : ICurrentUserStateQuery
    {
        public int CallCount { get; private set; }

        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(isActive);
        }
    }

    private sealed class StubGetTicketQuery(GetTicketResult? result) : IGetTicketQuery
    {
        public int CallCount { get; private set; }

        public Guid QueriedId { get; private set; }

        public Task<GetTicketResult?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            CallCount++;
            QueriedId = id;
            return Task.FromResult(result);
        }
    }

    private sealed class StubListTicketsQuery(ListTicketsResult result) : IListTicketsQuery
    {
        public int CallCount { get; private set; }

        public ListTicketsRequest? Request { get; private set; }

        public Task<ListTicketsResult> ListAsync(
            ListTicketsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(result);
        }
    }
}
