using Microsoft.Extensions.DependencyInjection;

namespace WashFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application use cases here as subsequent phases introduce them.
        return services;
    }
}
