using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthKit.Blazor;

/// <summary>Registration helpers for the Blazor renderer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuthKitState"/> and the session store used by the components.
    /// </summary>
    /// <param name="services">Service collection to extend.</param>
    /// <param name="storage">
    /// <see cref="AuthSessionStorageMode.BrowserSession"/> keeps the session in sessionStorage
    /// (WebAssembly and Auto hosting). Server-side Blazor must use
    /// <see cref="AuthSessionStorageMode.Memory"/> and keep refresh tokens on the server.
    /// </param>
    public static IServiceCollection AddAuthKitBlazor(
        this IServiceCollection services,
        AuthSessionStorageMode storage = AuthSessionStorageMode.Memory)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AuthKitState>();

        // Warms a deferred catalog before the first component renders. No-op for static catalogs.
        services.AddHostedService<AuthKitMessagesWarmup>();

        if (storage == AuthSessionStorageMode.BrowserSession)
        {
            services.TryAddScoped<IAuthSessionStore, BrowserSessionStore>();
        }
        else
        {
            services.TryAddScoped<IAuthSessionStore, InMemoryAuthSessionStore>();
        }

        return services;
    }
}
