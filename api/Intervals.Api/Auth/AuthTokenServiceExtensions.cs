using Microsoft.Extensions.DependencyInjection;

namespace Intervals.Api.Auth;

public static class AuthTokenServiceExtensions
{
    public static IServiceCollection AddIntervalsAuthTokens(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuthActionTokenService>();
        return services;
    }
}
