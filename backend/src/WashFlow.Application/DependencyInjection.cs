using Microsoft.Extensions.DependencyInjection;
using WashFlow.Application.Authentication;

namespace WashFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        return services;
    }
}
