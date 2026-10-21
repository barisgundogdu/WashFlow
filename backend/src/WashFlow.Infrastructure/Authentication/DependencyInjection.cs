using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WashFlow.Application.Authentication;

namespace WashFlow.Infrastructure.Authentication;

public static class DependencyInjection
{
    public static IServiceCollection AddCoreAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = JwtSettings.Read(configuration);
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddScoped<IAuthStore, EfAuthStore>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = settings.ValidationParameters();
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId) ||
                        !Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sessionId) ||
                        !await context.HttpContext.RequestServices.GetRequiredService<IAuthStore>()
                            .IsSessionActiveAsync(userId, sessionId, TimeProvider.System.GetUtcNow(), context.HttpContext.RequestAborted))
                        context.Fail("Oturum geçersiz.");
                }
            };
        });
        services.AddAuthorization();
        return services;
    }
}
