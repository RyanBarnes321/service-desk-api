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
using ServiceDesk.Core.Application.Tickets.Comments;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.History;
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

    [Theory]
    [InlineData("POST", "/api/tickets/11111111-1111-1111-1111-111111111111/start")]
    [InlineData("POST", "/api/tickets/11111111-1111-1111-1111-111111111111/wait")]
    [InlineData("POST", "/api/tickets/11111111-1111-1111-1111-111111111111/resume")]
    [InlineData("POST", "/api/tickets/11111111-1111-1111-1111-111111111111/resolve")]
    [InlineData("PUT", "/api/tickets/11111111-1111-1111-1111-111111111111/priority")]
    public async Task TechnicalRoute_AnonymousRequest_ReturnsUnauthorized(string method, string path)
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient(authenticated: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Content = path.EndsWith("/resolve", StringComparison.Ordinal)
            ? JsonContent.Create(new ResolveTicketRequest("Resolved."))
            : path.EndsWith("/priority", StringComparison.Ordinal)
                ? JsonContent.Create(new ChangeTicketPriorityRequest(TicketPriority.High))
                : null;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TechnicalRoute_InvalidToken_ReturnsUnauthorizedBeforeCurrentUserLookup()
    {
        await using var factory = new TicketApiFactory();
        using var client = factory.CreateHttpsClient(
            accessToken: TestJwt.Create(
                factory.UserId,
                signingKey: "different-test-only-signing-key-that-is-also-at-least-sixty-four-characters"));

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/start", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TechnicalRoute_InactiveOrMissingCurrentUser_ReturnsForbidden(
        bool active,
        bool exists)
    {
        var ticket = TechnicalTicket(Guid.NewGuid(), TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            isCurrentUserActive: active,
            currentUserExists: exists,
            persistedRole: UserRole.Technician,
            currentUserId: ticket.AssignedTechnicianId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Administrator)]
    public async Task StartWork_NonTechnicianPersistedRole_ReturnsForbidden(UserRole role)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Fact]
    public async Task StartWork_DifferentTechnicianAndUnassignedTicket_ReturnForbidden()
    {
        var actorId = Guid.NewGuid();
        var otherTicket = TechnicalTicket(Guid.NewGuid(), TicketStatus.Assigned);
        await using var otherFactory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: actorId,
            assignmentTicket: otherTicket);
        using var otherClient = otherFactory.CreateHttpsClient();

        var otherResponse = await otherClient.PostAsync(
            $"/api/tickets/{otherTicket.Id}/start",
            null);

        var unassignedTicket = AssignmentTicket();
        await using var unassignedFactory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: actorId,
            assignmentTicket: unassignedTicket);
        using var unassignedClient = unassignedFactory.CreateHttpsClient();
        var unassignedResponse = await unassignedClient.PostAsync(
            $"/api/tickets/{unassignedTicket.Id}/start",
            null);

        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
        Assert.Equal(0, otherFactory.AssignmentPersistence.PersistCallCount);
        Assert.Equal(0, unassignedFactory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("start", TicketStatus.Assigned, TicketStatus.InProgress)]
    [InlineData("wait", TicketStatus.InProgress, TicketStatus.Waiting)]
    [InlineData("resume", TicketStatus.Waiting, TicketStatus.InProgress)]
    public async Task StatusTechnicalOperation_ValidTransition_ReturnsOk(
        string operation,
        TicketStatus source,
        TicketStatus expected)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, source);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/{operation}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketTechnicalOperationResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(ticket.Id, body.Id);
        Assert.Equal(expected, body.Status);
        Assert.Equal(userId, body.AssignedTechnicianId);
        Assert.Equal(1, factory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("start", TicketStatus.InProgress)]
    [InlineData("wait", TicketStatus.Assigned)]
    [InlineData("resume", TicketStatus.InProgress)]
    public async Task StatusTechnicalOperation_InvalidTransition_ReturnsConflict(
        string operation,
        TicketStatus source)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, source);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/{operation}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Ticket operation conflict", problem?.Title);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("wait")]
    [InlineData("resume")]
    public async Task StatusTechnicalOperation_MissingTicket_ReturnsNotFound(string operation)
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Technician);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_ValidRequest_ReturnsResolvedResponse()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.InProgress);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();
        const string summary = "Replaced the failed adapter and verified connectivity.";

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/resolve",
            new ResolveTicketRequest(summary));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketTechnicalOperationResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(TicketStatus.Resolved, body.Status);
        Assert.Equal(summary, body.ResolutionSummary);
        Assert.NotNull(body.ResolvedAt);
        Assert.Equal(2, factory.AssignmentPersistence.Histories.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_BlankSummary_ReturnsBadRequest(string summary)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.InProgress);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/resolve",
            new ResolveTicketRequest(summary));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Fact]
    public async Task Resolve_MissingSummary_ReturnsCustomValidationBadRequestWithoutPersistenceOrHistory()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.InProgress);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(
            "{}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/resolve", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Fact]
    public async Task Resolve_MalformedJson_ReturnsFrameworkBadRequestWithoutPersistenceOrHistory()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.InProgress);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(
            "{",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/resolve", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.Waiting)]
    public async Task Resolve_InvalidState_ReturnsConflictWithoutPersistence(TicketStatus status)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, status);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/resolve",
            new ResolveTicketRequest("Resolved."));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Fact]
    public async Task Priority_ValidRequest_ReturnsUpdatedPriority()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/priority",
            new ChangeTicketPriorityRequest(TicketPriority.Low));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketTechnicalOperationResponse>(JsonOptions);
        Assert.Equal(TicketPriority.Low, body?.Priority);
        Assert.Equal(
            TicketHistoryEventType.PriorityChanged,
            Assert.Single(factory.AssignmentPersistence.Histories).EventType);
    }

    [Fact]
    public async Task Priority_UndefinedNumericValue_ReturnsBadRequestWithoutPersistence()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/priority",
            new { priority = 999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"priority\":null}")]
    public async Task Priority_MissingOrNullValue_ReturnsBadRequestWithoutPersistenceOrHistory(
        string json)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PutAsync($"/api/tickets/{ticket.Id}/priority", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Fact]
    public async Task Priority_SameValue_ReturnsConflictWithoutHistory()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/priority",
            new ChangeTicketPriorityRequest(TicketPriority.High));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Theory]
    [InlineData(UserRole.Technician, UserRole.Employee, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Administrator, UserRole.Technician, HttpStatusCode.OK)]
    [InlineData(UserRole.Technician, UserRole.Administrator, HttpStatusCode.Forbidden)]
    public async Task StartWork_PersistedRoleOverridesStaleJwtRole(
        UserRole jwtRole,
        UserRole persistedRole,
        HttpStatusCode expected)
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: persistedRole,
            jwtRole: jwtRole,
            currentUserId: userId,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/start", null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task StartWork_ConcurrencyOutcome_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket,
            assignmentOutcome: TicketAssignmentPersistenceOutcome.ConcurrencyConflict);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartWork_UnrelatedPersistenceFailure_RemainsObservable()
    {
        var userId = Guid.NewGuid();
        var ticket = TechnicalTicket(userId, TicketStatus.Assigned);
        var expected = new InvalidOperationException("unexpected persistence failure");
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            currentUserId: userId,
            assignmentTicket: ticket,
            assignmentException: expected);
        using var client = factory.CreateHttpsClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostAsync($"/api/tickets/{ticket.Id}/start", null));

        Assert.Same(expected, exception);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject-resolution")]
    public async Task ResolutionReview_AnonymousRequest_ReturnsUnauthorized(string operation)
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Administrator);
        using var client = factory.CreateHttpsClient(authenticated: false);

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.UserState.CallCount);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject-resolution")]
    public async Task ResolutionReview_InvalidToken_ReturnsUnauthorizedBeforeCurrentUserLookup(
        string operation)
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Administrator);
        using var client = factory.CreateHttpsClient(accessToken: "not-a-token");

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData("close", false, true)]
    [InlineData("reject-resolution", false, true)]
    [InlineData("close", true, false)]
    [InlineData("reject-resolution", true, false)]
    public async Task ResolutionReview_InactiveOrMissingCurrentUser_ReturnsForbidden(
        string operation,
        bool active,
        bool exists)
    {
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            isCurrentUserActive: active,
            currentUserExists: exists);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Theory]
    [InlineData("close", UserRole.Employee)]
    [InlineData("close", UserRole.Technician)]
    [InlineData("reject-resolution", UserRole.Employee)]
    [InlineData("reject-resolution", UserRole.Technician)]
    public async Task ResolutionReview_NonAdministrator_ReturnsForbiddenBeforeTicketLoad(
        string operation,
        UserRole role)
    {
        await using var factory = new TicketApiFactory(persistedRole: role);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.FindCallCount);
    }

    [Fact]
    public async Task Close_ResolvedTicket_ReturnsClosedResponseAndExactHistories()
    {
        var technicianId = Guid.NewGuid();
        var ticket = TechnicalTicket(technicianId, TicketStatus.Resolved);
        var summary = ticket.ResolutionSummary;
        var resolvedAt = ticket.ResolvedAt;
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/close", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketResolutionReviewResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(TicketStatus.Closed, body.Status);
        Assert.Equal(technicianId, body.AssignedTechnicianId);
        Assert.Equal(summary, body.ResolutionSummary);
        Assert.Equal(resolvedAt, body.ResolvedAt);
        Assert.NotNull(body.ClosedAt);
        Assert.Equal(1, factory.AssignmentPersistence.PersistCallCount);
        Assert.Equal(TicketHistoryEventType.Closed, factory.AssignmentPersistence.Histories[0].EventType);
        Assert.Null(factory.AssignmentPersistence.Histories[0].OldValue);
        Assert.Null(factory.AssignmentPersistence.Histories[0].NewValue);
        Assert.Equal(TicketHistoryEventType.StatusChanged, factory.AssignmentPersistence.Histories[1].EventType);
        Assert.Equal("Resolved", factory.AssignmentPersistence.Histories[1].OldValue);
        Assert.Equal("Closed", factory.AssignmentPersistence.Histories[1].NewValue);
    }

    [Fact]
    public async Task RejectResolution_ResolvedTicket_ReturnsInProgressAndAuditsRejectedSummary()
    {
        var technicianId = Guid.NewGuid();
        var ticket = TechnicalTicket(technicianId, TicketStatus.Resolved);
        var rejectedSummary = ticket.ResolutionSummary;
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/reject-resolution", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketResolutionReviewResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(TicketStatus.InProgress, body.Status);
        Assert.Equal(technicianId, body.AssignedTechnicianId);
        Assert.Null(body.ResolutionSummary);
        Assert.Null(body.ResolvedAt);
        Assert.Null(body.ClosedAt);
        var rejected = factory.AssignmentPersistence.Histories[0];
        Assert.Equal(TicketHistoryEventType.ResolutionRejected, rejected.EventType);
        Assert.Equal(rejectedSummary, rejected.OldValue);
        Assert.Null(rejected.NewValue);
        Assert.Equal(factory.UserId, rejected.PerformedByUserId);
        Assert.Equal(rejected.CreatedAt, factory.AssignmentPersistence.Histories[1].CreatedAt);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject-resolution")]
    public async Task ResolutionReview_MissingTicket_ReturnsNotFound(string operation)
    {
        await using var factory = new TicketApiFactory(persistedRole: UserRole.Administrator);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/{operation}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject-resolution")]
    public async Task ResolutionReview_InvalidState_ReturnsConflictWithoutPersistence(string operation)
    {
        var ticket = TechnicalTicket(Guid.NewGuid(), TicketStatus.InProgress);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/{operation}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Ticket resolution review conflict", problem?.Title);
        Assert.Equal(0, factory.AssignmentPersistence.PersistCallCount);
        Assert.Empty(factory.AssignmentPersistence.Histories);
    }

    [Theory]
    [InlineData("close", UserRole.Administrator, UserRole.Employee, HttpStatusCode.Forbidden)]
    [InlineData("reject-resolution", UserRole.Administrator, UserRole.Employee, HttpStatusCode.Forbidden)]
    [InlineData("close", UserRole.Administrator, UserRole.Technician, HttpStatusCode.Forbidden)]
    [InlineData("reject-resolution", UserRole.Administrator, UserRole.Technician, HttpStatusCode.Forbidden)]
    [InlineData("close", UserRole.Employee, UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData("reject-resolution", UserRole.Employee, UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData("close", UserRole.Technician, UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData("reject-resolution", UserRole.Technician, UserRole.Administrator, HttpStatusCode.OK)]
    public async Task ResolutionReview_PersistedRoleOverridesStaleJwtRole(
        string operation,
        UserRole jwtRole,
        UserRole persistedRole,
        HttpStatusCode expected)
    {
        var ticket = TechnicalTicket(Guid.NewGuid(), TicketStatus.Resolved);
        await using var factory = new TicketApiFactory(
            persistedRole: persistedRole,
            jwtRole: jwtRole,
            assignmentTicket: ticket);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/{operation}", null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Close_ConcurrencyOutcome_ReturnsConflict()
    {
        var ticket = TechnicalTicket(Guid.NewGuid(), TicketStatus.Resolved);
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket,
            assignmentOutcome: TicketAssignmentPersistenceOutcome.ConcurrencyConflict);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync($"/api/tickets/{ticket.Id}/close", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RejectResolution_UnrelatedPersistenceFailure_RemainsObservable()
    {
        var ticket = TechnicalTicket(Guid.NewGuid(), TicketStatus.Resolved);
        var expected = new InvalidOperationException("unexpected persistence failure");
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            assignmentTicket: ticket,
            assignmentException: expected);
        using var client = factory.CreateHttpsClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostAsync($"/api/tickets/{ticket.Id}/reject-resolution", null));

        Assert.Same(expected, exception);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Comments_AnonymousRequest_ReturnsUnauthorized(string method)
    {
        await using var factory = new TicketApiFactory(commentTicketId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient(authenticated: false);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{Guid.NewGuid()}/comments");
        if (method == "POST") request.Content = JsonContent.Create(new CreateTicketCommentRequest("Note"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.FindCallCount);
        Assert.Equal(0, factory.CommentsPersistence.ListCallCount);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Comments_InvalidToken_ReturnsUnauthorizedBeforeCurrentUserLookup(string method)
    {
        await using var factory = new TicketApiFactory(commentTicketId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient(accessToken: "not-a-token");
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{Guid.NewGuid()}/comments");
        if (method == "POST") request.Content = JsonContent.Create(new CreateTicketCommentRequest("Note"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.UserState.CallCount);
    }

    [Theory]
    [InlineData("POST", false, true)]
    [InlineData("GET", false, true)]
    [InlineData("POST", true, false)]
    [InlineData("GET", true, false)]
    public async Task Comments_MissingOrInactiveCurrentUser_ReturnsForbidden(
        string method,
        bool active,
        bool exists)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            isCurrentUserActive: active,
            currentUserExists: exists,
            commentTicketId: ticketId);
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{ticketId}/comments");
        if (method == "POST") request.Content = JsonContent.Create(new CreateTicketCommentRequest("Note"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.ListCallCount);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Comments_NonexistentTicket_ReturnsNotFound(string method)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            commentTicketId: ticketId,
            commentTicketExists: false);
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{ticketId}/comments");
        if (method == "POST") request.Content = JsonContent.Create(new CreateTicketCommentRequest("Note"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_EmployeeOwnOpenTicket_ReturnsCreatedWithActorAsAuthor()
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            currentUserId: userId,
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: userId);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new
            {
                content = "The issue returned.",
                authorUserId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TicketCommentResponse>();
        Assert.NotNull(body);
        Assert.Equal(ticketId, body.TicketId);
        Assert.Equal(userId, body.AuthorUserId);
        Assert.Equal("The issue returned.", body.Content);
        Assert.Equal($"/api/tickets/{ticketId}/comments", response.Headers.Location?.ToString());
        Assert.Equal(1, factory.CommentsPersistence.PersistCallCount);
    }

    [Fact]
    public async Task CreateComment_EmployeeOtherTicket_IsConcealed()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: Guid.NewGuid(),
            commentTicketStatus: TicketStatus.Closed);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new CreateTicketCommentRequest("Hidden ticket note."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task CreateComment_PrivilegedRole_UnownedActiveTicketSucceeds(UserRole role)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            commentTicketId: ticketId,
            commentTicketCreatorId: Guid.NewGuid(),
            commentTicketStatus: TicketStatus.Resolved);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new CreateTicketCommentRequest("Review note."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task CreateComment_VisibleClosedTicket_ReturnsConflict(UserRole role)
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            currentUserId: userId,
            persistedRole: role,
            commentTicketId: ticketId,
            commentTicketCreatorId: userId,
            commentTicketStatus: TicketStatus.Closed);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new CreateTicketCommentRequest("Late note."));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.PersistCallCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"content\":null}")]
    [InlineData("{\"content\":\"\"}")]
    [InlineData("{\"content\":\"   \"}")]
    public async Task CreateComment_InvalidContent_ReturnsBadRequestWithoutPersistence(string json)
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            currentUserId: userId,
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: userId);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/tickets/{ticketId}/comments", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.CommentsPersistence.PersistCallCount);
    }

    [Fact]
    public async Task CreateComment_MalformedJson_ReturnsFrameworkBadRequestWithoutCommentQuery()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            commentTicketId: ticketId);
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/tickets/{ticketId}/comments", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.FindCallCount);
        Assert.Equal(0, factory.CommentsPersistence.PersistCallCount);
    }

    [Fact]
    public async Task CreateComment_OverLimitReturnsBadRequest_ExactLimitSucceeds()
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            currentUserId: userId,
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: userId);
        using var client = factory.CreateHttpsClient();

        var invalid = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new CreateTicketCommentRequest(new string('x', TicketComment.MaximumContentLength + 1)));
        var valid = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/comments",
            new CreateTicketCommentRequest(new string('x', TicketComment.MaximumContentLength)));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Equal(1, factory.CommentsPersistence.PersistCallCount);
    }

    [Fact]
    public async Task ListComments_EmployeeOwnClosedTicket_ReturnsItemsAndMetadata()
    {
        var userId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var first = new TicketCommentResult(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), ticketId, userId,
            "First", new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero));
        var second = new TicketCommentResult(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), ticketId, Guid.NewGuid(),
            "Second", first.CreatedAt.AddMinutes(1));
        await using var factory = new TicketApiFactory(
            currentUserId: userId,
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: userId,
            commentTicketStatus: TicketStatus.Closed,
            commentsResult: new ListTicketCommentsResult([first, second], 1, 20, 2, 1));
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/comments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListTicketCommentsResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal([first.Id, second.Id], body.Items.Select(item => item.Id));
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(2, body.TotalCount);
        Assert.Equal(1, body.TotalPages);
        Assert.Equal(new ListTicketCommentsRequest(), factory.CommentsPersistence.ListRequest);
    }

    [Fact]
    public async Task ListComments_EmployeeOtherTicket_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Employee,
            commentTicketId: ticketId,
            commentTicketCreatorId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/comments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task ListComments_PrivilegedRole_CanReadUnownedClosedTicket(UserRole role)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            commentTicketId: ticketId,
            commentTicketCreatorId: Guid.NewGuid(),
            commentTicketStatus: TicketStatus.Closed);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/comments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task ListComments_InvalidPagination_ReturnsBadRequest(string query)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            commentTicketId: ticketId);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/comments{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.CommentsPersistence.ListCallCount);
    }

    [Fact]
    public async Task ListComments_CustomPagination_MapsQueryAndEmptyMetadata()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            commentTicketId: ticketId,
            commentsResult: new ListTicketCommentsResult([], 3, 50, 0, 0));
        using var client = factory.CreateHttpsClient();

        var body = await client.GetFromJsonAsync<ListTicketCommentsResponse>(
            $"/api/tickets/{ticketId}/comments?page=3&pageSize=50");

        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(3, body.Page);
        Assert.Equal(50, body.PageSize);
        Assert.Equal(0, body.TotalCount);
        Assert.Equal(0, body.TotalPages);
    }

    [Theory]
    [InlineData("POST", UserRole.Technician, UserRole.Employee, false)]
    [InlineData("GET", UserRole.Technician, UserRole.Employee, false)]
    [InlineData("POST", UserRole.Administrator, UserRole.Employee, false)]
    [InlineData("GET", UserRole.Administrator, UserRole.Employee, false)]
    [InlineData("POST", UserRole.Employee, UserRole.Technician, true)]
    [InlineData("GET", UserRole.Employee, UserRole.Technician, true)]
    [InlineData("POST", UserRole.Technician, UserRole.Administrator, true)]
    [InlineData("GET", UserRole.Technician, UserRole.Administrator, true)]
    public async Task Comments_PersistedRoleOverridesStaleJwtRole(
        string method,
        UserRole jwtRole,
        UserRole persistedRole,
        bool expectedVisible)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            jwtRole: jwtRole,
            persistedRole: persistedRole,
            commentTicketId: ticketId,
            commentTicketCreatorId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{ticketId}/comments");
        if (method == "POST") request.Content = JsonContent.Create(new CreateTicketCommentRequest("Note"));

        var response = await client.SendAsync(request);

        Assert.Equal(
            expectedVisible ? method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK : HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task TicketHistory_AnonymousRequest_ReturnsUnauthorized()
    {
        await using var factory = new TicketApiFactory(historyTicketId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient(authenticated: false);

        var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.HistoryQuery.CallCount);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TicketHistory_MutationMethod_IsNotAvailableAndDoesNotQuery(string method)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            historyTicketId: ticketId);
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/tickets/{ticketId}/history");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, factory.HistoryQuery.CallCount);
    }

    [Fact]
    public async Task TicketHistory_InvalidToken_ReturnsUnauthorizedBeforeCurrentUserLookup()
    {
        await using var factory = new TicketApiFactory(historyTicketId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient(accessToken: "not-a-token");

        var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.UserState.CallCount);
        Assert.Equal(0, factory.HistoryQuery.CallCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TicketHistory_MissingOrInactiveCurrentUser_ReturnsForbidden(
        bool active,
        bool exists)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            isCurrentUserActive: active,
            currentUserExists: exists,
            historyTicketId: ticketId);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.HistoryQuery.CallCount);
    }

    [Fact]
    public async Task TicketHistory_EmployeeOwnClosedTicket_ReturnsOrderedItemsAndMetadata()
    {
        var employeeId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var first = new TicketHistoryItem(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ticketId,
            employeeId,
            TicketHistoryEventType.Created,
            null,
            null,
            createdAt);
        var second = new TicketHistoryItem(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ticketId,
            null,
            TicketHistoryEventType.StatusChanged,
            "Open",
            "Assigned",
            createdAt.AddMinutes(1));
        await using var factory = new TicketApiFactory(
            currentUserId: employeeId,
            persistedRole: UserRole.Employee,
            historyTicketId: ticketId,
            historyTicketCreatorId: employeeId,
            historyResult: new ListTicketHistoryResult([first, second], 1, 20, 2, 1));
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"eventType\":\"Created\"", json);
        var body = JsonSerializer.Deserialize<ListTicketHistoryResponse>(json, JsonOptions);
        Assert.NotNull(body);
        Assert.Equal([first.Id, second.Id], body.Items.Select(item => item.Id));
        Assert.Equal(first.TicketId, body.Items[0].TicketId);
        Assert.Equal(first.PerformedByUserId, body.Items[0].PerformedByUserId);
        Assert.Equal(first.OldValue, body.Items[0].OldValue);
        Assert.Equal(first.NewValue, body.Items[0].NewValue);
        Assert.Equal(first.CreatedAt, body.Items[0].CreatedAt);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(2, body.TotalCount);
        Assert.Equal(1, body.TotalPages);
        Assert.Equal(new ListTicketHistoryRequest(), factory.HistoryQuery.Request);
    }

    [Fact]
    public async Task TicketHistory_EmployeeOtherTicket_IsConcealed()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Employee,
            historyTicketId: ticketId,
            historyTicketCreatorId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task TicketHistory_PrivilegedRole_CanReadAnyTicket(UserRole role)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: role,
            historyTicketId: ticketId,
            historyTicketCreatorId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TicketHistory_NonexistentTicket_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            historyTicketId: ticketId,
            historyTicketExists: false);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task TicketHistory_InvalidPagination_ReturnsBadRequestWithoutQuery(string query)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Technician,
            historyTicketId: ticketId);
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid request", problem?.Title);
        Assert.Equal(0, factory.HistoryQuery.CallCount);
    }

    [Fact]
    public async Task TicketHistory_CustomPagination_MapsQueryAndEmptyMetadata()
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            persistedRole: UserRole.Administrator,
            historyTicketId: ticketId,
            historyResult: new ListTicketHistoryResult([], 3, 50, 0, 0));
        using var client = factory.CreateHttpsClient();

        var body = await client.GetFromJsonAsync<ListTicketHistoryResponse>(
            $"/api/tickets/{ticketId}/history?page=3&pageSize=50", JsonOptions);

        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(3, body.Page);
        Assert.Equal(50, body.PageSize);
        Assert.Equal(0, body.TotalCount);
        Assert.Equal(0, body.TotalPages);
        Assert.Equal(new ListTicketHistoryRequest(3, 50), factory.HistoryQuery.Request);
    }

    [Theory]
    [InlineData(UserRole.Technician, UserRole.Employee, false)]
    [InlineData(UserRole.Administrator, UserRole.Employee, false)]
    [InlineData(UserRole.Employee, UserRole.Technician, true)]
    [InlineData(UserRole.Technician, UserRole.Administrator, true)]
    public async Task TicketHistory_PersistedRoleOverridesStaleJwtRole(
        UserRole jwtRole,
        UserRole persistedRole,
        bool expectedVisible)
    {
        var ticketId = Guid.NewGuid();
        await using var factory = new TicketApiFactory(
            jwtRole: jwtRole,
            persistedRole: persistedRole,
            historyTicketId: ticketId,
            historyTicketCreatorId: Guid.NewGuid());
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/tickets/{ticketId}/history");

        Assert.Equal(expectedVisible ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
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

    private static Ticket TechnicalTicket(Guid technicianId, TicketStatus status)
    {
        var ticket = AssignmentTicket();
        ticket.Assign(technicianId, ticket.CreatedAt.AddMinutes(1));
        if (status == TicketStatus.Assigned)
        {
            return ticket;
        }

        ticket.StartWork(ticket.UpdatedAt.AddMinutes(1));
        if (status == TicketStatus.InProgress)
        {
            return ticket;
        }

        ticket.Wait(ticket.UpdatedAt.AddMinutes(1));
        if (status == TicketStatus.Waiting)
        {
            return ticket;
        }

        ticket.Resume(ticket.UpdatedAt.AddMinutes(1));
        ticket.Resolve("Resolved.", ticket.UpdatedAt.AddMinutes(1));
        if (status == TicketStatus.Resolved)
        {
            return ticket;
        }

        ticket.Close(ticket.UpdatedAt.AddMinutes(1));
        return ticket;
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
            CurrentUserState? targetUserState = null,
            Guid? commentTicketId = null,
            Guid? commentTicketCreatorId = null,
            TicketStatus commentTicketStatus = TicketStatus.Open,
            bool commentTicketExists = true,
            ListTicketCommentsResult? commentsResult = null,
            Guid? historyTicketId = null,
            Guid? historyTicketCreatorId = null,
            bool historyTicketExists = true,
            ListTicketHistoryResult? historyResult = null)
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
            CommentsPersistence = new StubTicketCommentsPersistence(
                commentTicketExists ? commentTicketId : null,
                commentTicketCreatorId,
                commentTicketStatus,
                commentsResult ?? new ListTicketCommentsResult([], 1, 20, 0, 0));
            HistoryQuery = new StubListTicketHistoryQuery(
                historyTicketExists ? historyTicketId : null,
                historyTicketCreatorId,
                historyResult ?? new ListTicketHistoryResult([], 1, 20, 0, 0));
        }

        public Guid UserId { get; }

        public UserRole JwtRole { get; }

        public RecordingCreateTicketPersistence Persistence { get; }

        public StubGetTicketQuery Query { get; }

        public StubListTicketsQuery ListQuery { get; }

        public StubCurrentUserStateQuery UserState { get; }

        public RecordingTicketAssignmentPersistence AssignmentPersistence { get; }

        public StubTicketCommentsPersistence CommentsPersistence { get; }

        public StubListTicketHistoryQuery HistoryQuery { get; }

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
                services.RemoveAll<ICreateTicketCommentPersistence>();
                services.RemoveAll<IListTicketCommentsQuery>();
                services.RemoveAll<IListTicketHistoryQuery>();
                services.AddSingleton<ICreateTicketPersistence>(Persistence);
                services.AddSingleton<IGetTicketQuery>(Query);
                services.AddSingleton<IListTicketsQuery>(ListQuery);
                services.AddSingleton<ICurrentUserStateQuery>(UserState);
                services.AddSingleton<ITicketAssignmentPersistence>(AssignmentPersistence);
                services.AddSingleton<ICreateTicketCommentPersistence>(CommentsPersistence);
                services.AddSingleton<IListTicketCommentsQuery>(CommentsPersistence);
                services.AddSingleton<IListTicketHistoryQuery>(HistoryQuery);
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

    private sealed class StubTicketCommentsPersistence(
        Guid? ticketId,
        Guid? creatorId,
        TicketStatus status,
        ListTicketCommentsResult listResult)
        : ICreateTicketCommentPersistence, IListTicketCommentsQuery
    {
        public int FindCallCount { get; private set; }
        public int PersistCallCount { get; private set; }
        public int ListCallCount { get; private set; }
        public TicketComment? PersistedComment { get; private set; }
        public ListTicketCommentsRequest? ListRequest { get; private set; }

        public Task<CreateTicketCommentTarget?> FindVisibleTicketAsync(
            Guid requestedTicketId,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            FindCallCount++;
            CreateTicketCommentTarget? result = ticketId == requestedTicketId &&
                (visibility.CreatedByUserId is null || visibility.CreatedByUserId == creatorId)
                ? new(requestedTicketId, status)
                : null;
            return Task.FromResult(result);
        }

        public Task PersistAsync(TicketComment comment, CancellationToken cancellationToken)
        {
            PersistCallCount++;
            PersistedComment = comment;
            return Task.CompletedTask;
        }

        public Task<ListTicketCommentsResult?> ListAsync(
            Guid requestedTicketId,
            ListTicketCommentsRequest request,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            ListRequest = request;
            ListTicketCommentsResult? result = ticketId == requestedTicketId &&
                (visibility.CreatedByUserId is null || visibility.CreatedByUserId == creatorId)
                ? listResult with { Page = request.Page, PageSize = request.PageSize }
                : null;
            return Task.FromResult(result);
        }
    }

    private sealed class StubListTicketHistoryQuery(
        Guid? ticketId,
        Guid? creatorId,
        ListTicketHistoryResult result) : IListTicketHistoryQuery
    {
        public int CallCount { get; private set; }
        public ListTicketHistoryRequest? Request { get; private set; }

        public Task<ListTicketHistoryResult?> ListAsync(
            Guid requestedTicketId,
            ListTicketHistoryRequest request,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            ListTicketHistoryResult? visibleResult = ticketId == requestedTicketId &&
                (visibility.CreatedByUserId is null || visibility.CreatedByUserId == creatorId)
                ? result with { Page = request.Page, PageSize = request.PageSize }
                : null;
            return Task.FromResult(visibleResult);
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
