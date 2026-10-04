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
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.Assignment;
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
        var userId = Guid.NewGuid();
        var ticket = TicketResult(ticketId, userId);
        await using var factory = new TicketApiFactory(ticket, currentUserId: userId);
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
        var userId = Guid.NewGuid();
        var item = ListItem(Guid.NewGuid(), userId);
        var listResult = new ListTicketsResult([item], 1, 20, 1, 1);
        await using var factory = new TicketApiFactory(
            listResult: listResult,
            currentUserId: userId);
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
    public async Task TicketRoute_MissingCurrentUserWithValidToken_ReturnsForbidden()
    {
        await using var factory = new TicketApiFactory(currentUserExists: false);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/tickets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, factory.UserState.CallCount);
    }

    [Fact]
    public async Task TicketRoute_UndefinedPersistedRole_FailsClosed()
    {
        await using var factory = new TicketApiFactory(persistedRole: (UserRole)999);
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

    [Fact]
    public async Task Get_EmployeeOwnTicketReturnsOkAndOtherTicketReturnsNotFound()
    {
        var employeeId = Guid.NewGuid();
        var ownTicket = TicketResult(Guid.NewGuid(), employeeId);
        await using var ownFactory = new TicketApiFactory(
            ownTicket,
            currentUserId: employeeId,
            persistedRole: UserRole.Employee);
        using var ownClient = ownFactory.CreateHttpsClient();

        var ownResponse = await ownClient.GetAsync($"/api/tickets/{ownTicket.Id}");

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(1, ownFactory.UserState.CallCount);

        var otherTicket = TicketResult(Guid.NewGuid(), Guid.NewGuid());
        await using var otherFactory = new TicketApiFactory(
            otherTicket,
            currentUserId: employeeId,
            persistedRole: UserRole.Employee);
        using var otherClient = otherFactory.CreateHttpsClient();

        var otherResponse = await otherClient.GetAsync($"/api/tickets/{otherTicket.Id}");

        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(1, otherFactory.UserState.CallCount);
    }

    [Fact]
    public async Task List_EmployeeSeesOnlyOwnTicketsAndOtherCreatorFilterIsEmpty()
    {
        var employeeId = Guid.NewGuid();
        var otherCreatorId = Guid.NewGuid();
        var listResult = new ListTicketsResult(
            [ListItem(Guid.NewGuid(), employeeId), ListItem(Guid.NewGuid(), otherCreatorId)],
            1,
            20,
            2,
            1);
        await using var factory = new TicketApiFactory(
            listResult: listResult,
            currentUserId: employeeId,
            persistedRole: UserRole.Employee);
        using var client = factory.CreateHttpsClient();

        var ownResponse = await client.GetFromJsonAsync<ListTicketsResponse>(
            "/api/tickets",
            JsonOptions);
        var otherFilterResponse = await client.GetFromJsonAsync<ListTicketsResponse>(
            $"/api/tickets?createdByUserId={otherCreatorId}",
            JsonOptions);

        Assert.NotNull(ownResponse);
        Assert.Equal(employeeId, Assert.Single(ownResponse.Items).CreatedByUserId);
        Assert.Equal(1, ownResponse.TotalCount);
        Assert.NotNull(otherFilterResponse);
        Assert.Empty(otherFilterResponse.Items);
        Assert.Equal(0, otherFilterResponse.TotalCount);
        Assert.Equal(0, otherFilterResponse.TotalPages);
        Assert.Equal(2, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task Read_PrivilegedPersistedRoleSeesOtherDetailAndMultipleCreators(UserRole role)
    {
        var actorId = Guid.NewGuid();
        var otherCreatorId = Guid.NewGuid();
        var ticket = TicketResult(Guid.NewGuid(), otherCreatorId);
        var listResult = new ListTicketsResult(
            [ListItem(Guid.NewGuid(), actorId), ListItem(Guid.NewGuid(), otherCreatorId)],
            1,
            20,
            2,
            1);
        await using var factory = new TicketApiFactory(
            ticket,
            listResult: listResult,
            currentUserId: actorId,
            persistedRole: role);
        using var client = factory.CreateHttpsClient();

        var detailResponse = await client.GetAsync($"/api/tickets/{ticket.Id}");
        var listResponse = await client.GetFromJsonAsync<ListTicketsResponse>(
            "/api/tickets",
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.NotNull(listResponse);
        Assert.Equal(2, listResponse.Items.Count);
        Assert.Equal(2, listResponse.TotalCount);
        Assert.Equal(2, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Administrator, UserRole.Employee, HttpStatusCode.NotFound)]
    [InlineData(UserRole.Employee, UserRole.Technician, HttpStatusCode.OK)]
    public async Task Get_PersistedRoleOverridesStaleJwtRole(
        UserRole jwtRole,
        UserRole persistedRole,
        HttpStatusCode expectedStatus)
    {
        var actorId = Guid.NewGuid();
        var otherTicket = TicketResult(Guid.NewGuid(), Guid.NewGuid());
        await using var factory = new TicketApiFactory(
            otherTicket,
            currentUserId: actorId,
            persistedRole: persistedRole,
            jwtRole: jwtRole);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{otherTicket.Id}");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(1, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task Claim_TechnicianOrAdministrator_ReturnsAssignedSelf(UserRole role)
    {
        var userId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketAssignmentResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(ticket.Id, body.Id);
        Assert.Equal(TicketStatus.Assigned, body.Status);
        Assert.Equal(userId, body.AssignedTechnicianId);
        Assert.Equal(1, factory.AssignmentPersistence.PersistCallCount);
        Assert.Equal(2, factory.AssignmentPersistence.Histories.Count);
    }

    [Fact]
    public async Task Claim_Employee_ReturnsForbiddenWithoutTicketLoad()
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Claim_InactiveOrMissingCurrentUser_ReturnsForbiddenBeforeTicketLoad(
        bool active,
        bool exists)
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            isCurrentUserActive: active,
            currentUserExists: exists,
            persistedRole: UserRole.Technician,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, factory.UserState.CallCount);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Fact]
    public async Task Assignment_AdministratorAssignsActiveTechnician_ReturnsReadableResponse()
    {
        var targetId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketAssignmentResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(TicketStatus.Assigned, body.Status);
        Assert.Equal(targetId, body.AssignedTechnicianId);
        Assert.Equal(2, factory.UserState.CallCount);
        Assert.Equal(1, factory.AssignmentPersistence.PersistCallCount);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Assigned\"", json);
    }

    [Fact]
    public async Task Unassign_Administrator_ReturnsOpenUnassignedResponse()
    {
        var ticket = AssignmentTicket();
        ticket.Assign(Guid.NewGuid(), ticket.CreatedAt.AddMinutes(1));
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketAssignmentResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(TicketStatus.Open, body.Status);
        Assert.Null(body.AssignedTechnicianId);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task Assignment_NonAdministrator_ReturnsForbidden(UserRole role)
    {
        var ticket = AssignmentTicket();
        var targetId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var assign = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));
        var unassign = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassign.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Theory]
    [InlineData(false, UserRole.Technician)]
    [InlineData(true, UserRole.Employee)]
    [InlineData(true, UserRole.Administrator)]
    public async Task Assignment_UnusableTarget_ReturnsSameSafeBadRequest(
        bool active,
        UserRole role)
    {
        var targetId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: AssignmentTicket(),
            targetUserState: new CurrentUserState(targetId, role, active));
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{factory.AssignmentPersistence.Ticket!.Id}/assignment",
            new AssignTicketRequest(targetId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid technician", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Fact]
    public async Task Assignment_MissingTarget_ReturnsSameSafeBadRequest()
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid technician", problem?.Title);
    }

    [Fact]
    public async Task Assignment_SameTechnicianStateConflict_ReturnsConflict()
    {
        var targetId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        ticket.Assign(targetId, ticket.CreatedAt.AddMinutes(1));
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Ticket assignment conflict", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Assignment_MissingTicket_ReturnsNotFoundAfterValidTarget()
    {
        var targetId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{Guid.NewGuid()}/assignment",
            new AssignTicketRequest(targetId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, factory.AssignmentPersistence.FindCallCount);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Unassign_MissingTicket_ReturnsNotFound()
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Administrator);
        using var client = factory.CreateHttpsClient();

        var response = await client.DeleteAsync($"/api/tickets/{Guid.NewGuid()}/assignment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, factory.AssignmentPersistence.FindCallCount);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Claim_MissingTicket_ReturnsNotFound()
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Technician);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/claim", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unassign_InvalidState_ReturnsConflictProblemDetails()
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Ticket assignment conflict", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Claim_StalePersistence_ReturnsConflictProblemDetails()
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            assignmentTicket: ticket,
            assignmentOutcome: TicketAssignmentPersistenceOutcome.ConcurrencyConflict);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Ticket assignment conflict", problem?.Title);
    }

    [Fact]
    public async Task Claim_UnrelatedPersistenceFailure_RemainsObservable()
    {
        var ticket = AssignmentTicket();
        var expected = new InvalidOperationException("unexpected persistence failure");
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            assignmentTicket: ticket,
            assignmentException: expected);
        using var client = factory.CreateHttpsClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostAsync($"/api/tickets/{ticket.Id}/claim", null));

        Assert.Same(expected, exception);
    }

    [Theory]
    [InlineData(UserRole.Administrator, UserRole.Employee, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Employee, UserRole.Technician, HttpStatusCode.OK)]
    public async Task Claim_PersistedRoleOverridesStaleJwtRole(
        UserRole jwtRole,
        UserRole persistedRole,
        HttpStatusCode expected)
    {
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: persistedRole,
            jwtRole: jwtRole,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AssignAndUnassign_JwtAdministratorPersistedEmployee_ReturnForbidden()
    {
        var targetId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Employee,
            jwtRole: UserRole.Administrator,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var assign = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));
        var unassign = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassign.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Fact]
    public async Task AssignAndUnassign_JwtEmployeePersistedAdministrator_ReturnOk()
    {
        var targetId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            jwtRole: UserRole.Employee,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var assign = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));
        var unassign = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unassign.StatusCode);
        Assert.Equal(2, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Mutations_JwtAdministratorPersistedTechnician_CanClaimButCannotAssignOrUnassign()
    {
        var targetId = Guid.NewGuid();
        var ticket = AssignmentTicket();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            jwtRole: UserRole.Administrator,
            assignmentTicket: ticket,
            targetUserState: new CurrentUserState(targetId, UserRole.Technician, true));
        using var client = factory.CreateHttpsClient();

        var claim = await client.PostAsync($"/api/tickets/{ticket.Id}/claim", null);
        var assign = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment",
            new AssignTicketRequest(targetId));
        var unassign = await client.DeleteAsync($"/api/tickets/{ticket.Id}/assignment");

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassign.StatusCode);
        Assert.Equal(1, factory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("POST", "/api/tickets/11111111-1111-1111-1111-111111111111/claim")]
    [InlineData("PUT", "/api/tickets/11111111-1111-1111-1111-111111111111/assignment")]
    [InlineData("DELETE", "/api/tickets/11111111-1111-1111-1111-111111111111/assignment")]
    public async Task AssignmentRoute_AnonymousRequest_ReturnsUnauthorized(string method, string path)
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new AssignTicketRequest(Guid.NewGuid()));
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static CreateTicketRequest ValidCreateRequest()
    {
        return new CreateTicketRequest(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High);
    }

    private static Ticket AssignmentTicket()
    {
        return Ticket.Create(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    private static GetTicketResult TicketResult(Guid id, Guid? createdByUserId = null)
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

        return new GetTicketResult(
            id,
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            TicketStatus.Open,
            createdByUserId ?? Guid.NewGuid(),
            null,
            null,
            createdAt,
            createdAt,
            null,
            null);
    }

    private static ListTicketItem ListItem(Guid id, Guid? createdByUserId = null)
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

        return new ListTicketItem(
            id,
            "Cannot connect to VPN",
            TicketCategory.Network,
            TicketPriority.High,
            TicketStatus.Assigned,
            createdByUserId ?? Guid.NewGuid(),
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
            bool isCurrentUserActive = true,
            UserRole persistedRole = UserRole.Employee,
            UserRole jwtRole = UserRole.Employee,
            Guid? currentUserId = null,
            bool currentUserExists = true,
            Ticket? assignmentTicket = null,
            TicketAssignmentPersistenceOutcome assignmentOutcome =
                TicketAssignmentPersistenceOutcome.Persisted,
            Exception? assignmentException = null,
            CurrentUserState? targetUserState = null)
        {
            UserId = currentUserId ?? Guid.NewGuid();
            JwtRole = jwtRole;
            Query = new StubGetTicketQuery(queryResult);
            Persistence = new RecordingCreateTicketPersistence(persistenceException);
            ListQuery = new StubListTicketsQuery(listResult ?? new ListTicketsResult([], 1, 20, 0, 0));
            UserState = new StubCurrentUserStateQuery(
                UserId,
                isCurrentUserActive,
                persistedRole,
                currentUserExists,
                targetUserState);
            AssignmentPersistence = new RecordingTicketAssignmentPersistence(
                assignmentTicket,
                assignmentOutcome,
                assignmentException);
        }

        public Guid UserId { get; }

        public UserRole JwtRole { get; }

        public RecordingCreateTicketPersistence Persistence { get; }

        public StubGetTicketQuery Query { get; }

        public StubListTicketsQuery ListQuery { get; }

        public StubCurrentUserStateQuery UserState { get; }

        public RecordingTicketAssignmentPersistence AssignmentPersistence { get; }

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
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken ?? TestJwt.Create(UserId, role: JwtRole));
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
                services.RemoveAll<ITicketAssignmentPersistence>();
                services.AddSingleton<ICreateTicketPersistence>(Persistence);
                services.AddSingleton<IGetTicketQuery>(Query);
                services.AddSingleton<IListTicketsQuery>(ListQuery);
                services.AddSingleton<ICurrentUserStateQuery>(UserState);
                services.AddSingleton<ITicketAssignmentPersistence>(AssignmentPersistence);
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

    private sealed class StubCurrentUserStateQuery(
        Guid currentUserId,
        bool isActive,
        UserRole role,
        bool exists,
        CurrentUserState? targetState) : ICurrentUserStateQuery
    {
        public int CallCount { get; private set; }

        public Task<CurrentUserState?> FindAsync(Guid userId, CancellationToken cancellationToken)
        {
            CallCount++;
            CurrentUserState? state = userId == currentUserId
                ? exists ? new(currentUserId, role, isActive) : null
                : targetState?.UserId == userId ? targetState : null;
            return Task.FromResult<CurrentUserState?>(state);
        }
    }

    private sealed class RecordingTicketAssignmentPersistence(
        Ticket? ticket,
        TicketAssignmentPersistenceOutcome outcome,
        Exception? exception) : ITicketAssignmentPersistence
    {
        public Ticket? Ticket { get; } = ticket;

        public int FindCallCount { get; private set; }

        public int PersistCallCount { get; private set; }

        public IReadOnlyList<TicketHistory> Histories { get; private set; } = [];

        public Task<Ticket?> FindTrackedAsync(Guid ticketId, CancellationToken cancellationToken)
        {
            FindCallCount++;
            return Task.FromResult(Ticket?.Id == ticketId ? Ticket : null);
        }

        public Task<TicketAssignmentPersistenceOutcome> PersistAsync(
            Ticket persistedTicket,
            IReadOnlyCollection<TicketHistory> histories,
            CancellationToken cancellationToken)
        {
            PersistCallCount++;
            Histories = histories.ToArray();
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(outcome);
        }
    }

    private sealed class StubGetTicketQuery(GetTicketResult? result) : IGetTicketQuery
    {
        public int CallCount { get; private set; }

        public Guid QueriedId { get; private set; }

        public Task<GetTicketResult?> FindAsync(
            Guid id,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            CallCount++;
            QueriedId = id;
            var visibleResult = result is not null &&
                (visibility.CreatedByUserId is null ||
                 result.CreatedByUserId == visibility.CreatedByUserId)
                ? result
                : null;
            return Task.FromResult(visibleResult);
        }
    }

    private sealed class StubListTicketsQuery(ListTicketsResult result) : IListTicketsQuery
    {
        public int CallCount { get; private set; }

        public ListTicketsRequest? Request { get; private set; }

        public Task<ListTicketsResult> ListAsync(
            ListTicketsRequest request,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            var items = result.Items
                .Where(item => visibility.CreatedByUserId is null ||
                    item.CreatedByUserId == visibility.CreatedByUserId)
                .Where(item => request.CreatedByUserId is null ||
                    item.CreatedByUserId == request.CreatedByUserId)
                .ToArray();
            var totalPages = items.Length == 0
                ? 0
                : (int)Math.Ceiling(items.Length / (double)request.PageSize);
            return Task.FromResult(new ListTicketsResult(
                items,
                request.Page,
                request.PageSize,
                items.Length,
                totalPages));
        }
    }
}
