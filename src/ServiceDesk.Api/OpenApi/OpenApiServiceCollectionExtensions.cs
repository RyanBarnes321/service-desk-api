using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace ServiceDesk.Api.OpenApi;

public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddServiceDeskOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Service Desk API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "Authenticated ticket workflow API. Roles and account activity are reloaded " +
                    "from PostgreSQL for every protected request.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT returned by POST /api/auth/login."
                };
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var isProtected = metadata.OfType<IAuthorizeData>().Any() &&
                                  !metadata.OfType<IAllowAnonymous>().Any();
                if (isProtected)
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(
                                "Bearer",
                                context.Document,
                                null)] = []
                        }
                    ];
                }

                var example = RequestExample(
                    context.Description.HttpMethod,
                    context.Description.RelativePath);
                var requestContent = operation.RequestBody?.Content;
                if (example is not null &&
                    requestContent is not null &&
                    requestContent.TryGetValue(
                        "application/json",
                        out var mediaType) == true &&
                    mediaType is not null)
                {
                    mediaType.Example = JsonNode.Parse(example);
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static string? RequestExample(string? method, string? path)
    {
        return (method, path?.TrimEnd('/').Replace(":guid", string.Empty, StringComparison.Ordinal)) switch
        {
            ("POST", "api/auth/login") =>
                """{"email":"admin@example.test","password":"local-password"}""",
            ("POST", "api/tickets") =>
                """{"title":"VPN access","description":"Cannot connect from home.","category":"Network","priority":"High"}""",
            ("PUT", "api/tickets/{id}/assignment") =>
                """{"technicianId":"11111111-1111-1111-1111-111111111111"}""",
            ("POST", "api/tickets/{id}/resolve") =>
                """{"resolutionSummary":"Updated the VPN profile and verified connectivity."}""",
            ("PUT", "api/tickets/{id}/priority") =>
                """{"priority":"Critical"}""",
            ("POST", "api/tickets/{id}/comments") =>
                """{"content":"Diagnostics attached to the support record."}""",
            ("POST", "api/users") =>
                """{"email":"technician@example.test","password":"local-password","firstName":"Taylor","lastName":"Rivera","role":"Technician"}""",
            ("PUT", "api/users/{id}/role") =>
                """{"role":"Administrator"}""",
            ("PUT", "api/users/{id}/activation") =>
                """{"isActive":false}""",
            _ => null
        };
    }
}
