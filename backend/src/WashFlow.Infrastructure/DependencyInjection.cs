using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WashFlow.Infrastructure.Health;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("WashFlow");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__WashFlow yapılandırması gereklidir.");
        }

        services.AddScoped<WashFlow.Application.Businesses.IBusinessSettingsStore, Businesses.EfBusinessSettingsStore>();
        services.AddDbContext<WashFlowDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddScoped<WashFlow.Application.Authorization.ICurrentUserContextStore, Authorization.EfUserContextStore>();
        services.AddScoped<WashFlow.Application.Authorization.IBusinessAuthorizationStore, Authorization.EfBusinessAuthorizationStore>();
        var invitationHours = configuration.GetValue<int?>("Invitations:LifetimeHours") ?? 24;
        if (invitationHours is < 1 or > 720) throw new InvalidOperationException("Invitations:LifetimeHours 1–720 arasında olmalıdır.");
        services.AddSingleton(new WashFlow.Application.Invitations.InvitationOptions { Lifetime = TimeSpan.FromHours(invitationHours) });
        services.AddSingleton<WashFlow.Application.Invitations.IInvitationTokens, Invitations.InvitationTokens>();
        services.AddScoped<WashFlow.Application.Invitations.IInvitationStore, Invitations.EfInvitationStore>();
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
