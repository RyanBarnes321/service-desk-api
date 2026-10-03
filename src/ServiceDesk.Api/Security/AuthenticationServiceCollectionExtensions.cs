using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using ServiceDesk.Core.Application.Authentication.Login;

namespace ServiceDesk.Api.Security;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddServiceDeskAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = JwtSettings.FromConfiguration(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));

        services.AddSingleton(settings);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Email,
                    RoleClaimType = "role"
                };
            });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ActiveUserPolicy.Name, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveUserRequirement());
            });
        });
        services.AddScoped<IAuthorizationHandler, ActiveUserAuthorizationHandler>();

        return services;
    }
}
