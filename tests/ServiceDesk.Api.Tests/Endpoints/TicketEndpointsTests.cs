using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceDesk.Api.Contracts.Tickets;
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
        Assert.Equal(request.CreatedByUserId, body.CreatedByUserId);
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

    private static CreateTicketRequest ValidCreateRequest()
    {
        return new CreateTicketRequest(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            Guid.NewGuid());
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
            ListTicketsResult? listResult = null)
        {
            Query = new StubGetTicketQuery(queryResult);
            Persistence = new RecordingCreateTicketPersistence(persistenceException);
            ListQuery = new StubListTicketsQuery(listResult ?? new ListTicketsResult([], 1, 20, 0, 0));
        }

        public RecordingCreateTicketPersistence Persistence { get; }

        public StubGetTicketQuery Query { get; }

        public StubListTicketsQuery ListQuery { get; }

        public HttpClient CreateHttpsClient()
        {
            return CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICreateTicketPersistence>();
                services.RemoveAll<IGetTicketQuery>();
                services.RemoveAll<IListTicketsQuery>();
                services.AddSingleton<ICreateTicketPersistence>(Persistence);
                services.AddSingleton<IGetTicketQuery>(Query);
                services.AddSingleton<IListTicketsQuery>(ListQuery);
            });
        }
    }

    private sealed class RecordingCreateTicketPersistence(ArgumentException? exception = null)
        : ICreateTicketPersistence
    {
        public int CallCount { get; private set; }

        public Task PersistAsync(
            Ticket ticket,
            TicketHistory history,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (exception is not null)
            {
                throw exception;
            }

            return Task.CompletedTask;
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
