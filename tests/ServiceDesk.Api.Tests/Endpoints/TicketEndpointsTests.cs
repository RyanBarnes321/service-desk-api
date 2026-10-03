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

    private sealed class TicketApiFactory : WebApplicationFactory<Program>
    {
        public TicketApiFactory(
            GetTicketResult? queryResult = null,
            ArgumentException? persistenceException = null)
        {
            Query = new StubGetTicketQuery(queryResult);
            Persistence = new RecordingCreateTicketPersistence(persistenceException);
        }

        public RecordingCreateTicketPersistence Persistence { get; }

        public StubGetTicketQuery Query { get; }

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
                services.AddSingleton<ICreateTicketPersistence>(Persistence);
                services.AddSingleton<IGetTicketQuery>(Query);
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
}
