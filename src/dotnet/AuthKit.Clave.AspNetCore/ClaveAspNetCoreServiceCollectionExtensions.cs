using AuthKit.Clave;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthKit.Clave.AspNetCore;

/// <summary>Registration helpers for the ASP.NET Core surface of Cl@ve.</summary>
public static class ClaveAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cl@ve library and its HTTP settings in one call.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">CAID configuration. Validated immediately.</param>
    /// <param name="configureHttp">Routes and return-URL policy.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddClave(
        this IServiceCollection services,
        Action<ClaveOptions> configure,
        Action<ClaveAspNetCoreOptions>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddAuthKitClave(configure);
        return services.AddClaveHttp(configureHttp);
    }

    /// <summary>Registers HTTP settings only, for hosts that already called <c>AddAuthKitClave</c>.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Routes and return-URL policy.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddClaveHttp(
        this IServiceCollection services,
        Action<ClaveAspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ClaveAspNetCoreOptions();
        configure?.Invoke(options);
        Validate(options);

        services.TryAddSingleton(options);
        return services;
    }

    private static void Validate(ClaveAspNetCoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) || !options.RoutePrefix.StartsWith('/'))
        {
            throw new AuthKitConfigurationException("RoutePrefix must be a local path starting with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.LoginPath) || !options.LoginPath.StartsWith('/'))
        {
            throw new AuthKitConfigurationException("LoginPath must be a local path starting with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.FailurePath) || !options.FailurePath.StartsWith('/'))
        {
            throw new AuthKitConfigurationException("FailurePath must be a local path starting with '/'.");
        }

        if (options.MaxAcsBodyBytes is < 64 * 1024 or > 16 * 1024 * 1024)
        {
            throw new AuthKitConfigurationException("MaxAcsBodyBytes must be between 64 KB and 16 MB.");
        }

        foreach (var host in options.AllowedReturnUrlHosts)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Contains('/', StringComparison.Ordinal))
            {
                throw new AuthKitConfigurationException(
                    $"AllowedReturnUrlHosts entry '{host}' must be a bare host name.");
            }
        }
    }
}
