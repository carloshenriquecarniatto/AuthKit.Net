using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthKit.Clave;

/// <summary>Registration helpers for the Cl@ve library.</summary>
public static class ClaveServiceCollectionExtensions
{
    /// <summary>
    /// Registers Cl@ve services and validates the configuration immediately.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="AuthKitConfigurationException">
    /// Thrown at startup, never at request time, when the configuration cannot work.
    /// </exception>
    public static IServiceCollection AddAuthKitClave(this IServiceCollection services, Action<ClaveOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ClaveOptions();
        configure(options);
        ClaveOptionsValidator.ValidateAndThrow(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        RegisterState(services, options);
        services.TryAddSingleton(provider => new ClaveResponseValidator(
            options,
            provider.GetRequiredService<IClaveRequestStore>(),
            provider.GetRequiredService<IClaveReplayCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(provider => new ClaveLoginService(
            options,
            provider.GetRequiredService<IClaveRequestStore>(),
            provider.GetRequiredService<ClaveResponseValidator>(),
            provider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Chooses the request store and replay cache at registration time, following the order in
    /// docs/spec-clave.md §5.9: host implementation, then a distributed cache adapter, then memory.
    /// </summary>
    private static void RegisterState(IServiceCollection services, ClaveOptions options)
    {
        var hasRequestStore = services.Any(d => d.ServiceType == typeof(IClaveRequestStore));
        var hasReplayCache = services.Any(d => d.ServiceType == typeof(IClaveReplayCache));
        if (hasRequestStore && hasReplayCache)
        {
            return;
        }

        var hasDistributedCache = services.Any(d => d.ServiceType == typeof(IDistributedCache));
        if (hasDistributedCache)
        {
            if (!hasRequestStore)
            {
                services.TryAddSingleton<IClaveRequestStore>(provider => new DistributedClaveRequestStore(
                    provider.GetRequiredService<IDistributedCache>(),
                    options.Tenant!));
            }

            if (!hasReplayCache)
            {
                services.TryAddSingleton<IClaveReplayCache>(provider => new DistributedClaveReplayCache(
                    provider.GetRequiredService<IDistributedCache>(),
                    options.Tenant!,
                    provider.GetService<TimeProvider>()));
            }

            return;
        }

        if (!hasRequestStore)
        {
            services.TryAddSingleton<IClaveRequestStore>(provider =>
                new InMemoryClaveRequestStore(provider.GetRequiredService<TimeProvider>()));
        }

        if (!hasReplayCache)
        {
            services.TryAddSingleton<IClaveReplayCache>(provider =>
                new InMemoryClaveReplayCache(provider.GetRequiredService<TimeProvider>()));
        }
    }

}
