using Microsoft.Extensions.DependencyInjection;

namespace AuthKit;

/// <summary>
/// Adapter that starts with a fallback catalog and swaps in the real one once the source has
/// loaded. Components never block: they render the fallback for the first paint and the loaded
/// strings immediately after.
/// </summary>
/// <remarks>
/// Register it through <see cref="AuthMessagesServiceCollectionExtensions.AddAuthKitMessages"/>
/// and let the host warm it up. Blazor does that automatically; other hosts call
/// <see cref="AuthMessagesServiceCollectionExtensions.EnsureAuthKitMessagesAsync"/> once.
/// </remarks>
public sealed class DeferredAuthMessages : IAuthMessages
{
    private readonly IAuthMessagesSource _source;
    private readonly IAuthMessages _fallback;
    private readonly object _gate = new();
    private volatile IAuthMessages _current;
    private Task<IAuthMessages>? _loading;

    public DeferredAuthMessages(IAuthMessagesSource source, IAuthMessages? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _fallback = fallback ?? AuthMessages.English;
        _current = _fallback;
    }

    /// <summary>Whether the real catalog has replaced the fallback.</summary>
    public bool IsLoaded => !ReferenceEquals(_current, _fallback);

    /// <inheritdoc />
    public string Resolve(AuthMessageKey key, string? provider = null) => _current.Resolve(key, provider);

    /// <summary>
    /// Loads the catalog once. Concurrent callers share the same task, and a failed load can be
    /// retried without restarting the process.
    /// </summary>
    public Task<IAuthMessages> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return _loading ??= LoadCoreAsync(cancellationToken);
        }
    }

    private async Task<IAuthMessages> LoadCoreAsync(CancellationToken cancellationToken)
    {
        // Yield first: the caller assigns this task to _loading, and the reset below must happen
        // after that assignment or the faulted task would stick forever.
        await Task.Yield();

        try
        {
            var loaded = await _source.LoadAsync(cancellationToken).ConfigureAwait(false);
            _current = loaded;
            return loaded;
        }
        catch
        {
            lock (_gate)
            {
                _loading = null;
            }

            throw;
        }
    }
}

/// <summary>Registration helpers for message catalogs.</summary>
public static class AuthMessagesServiceCollectionExtensions
{
    /// <summary>
    /// Registers a catalog source. The provider used by every component resolves
    /// <see cref="IAuthMessages"/> from the container, so this can be called before or after
    /// <c>AddAuthKitSupabase</c>.
    /// </summary>
    public static IServiceCollection AddAuthKitMessages(
        this IServiceCollection services,
        IAuthMessagesSource source,
        IAuthMessages? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);

        services.AddSingleton(source);
        services.AddSingleton(provider =>
            new DeferredAuthMessages(provider.GetRequiredService<IAuthMessagesSource>(), fallback));
        services.AddSingleton<IAuthMessages>(provider => provider.GetRequiredService<DeferredAuthMessages>());

        return services;
    }

    /// <summary>Registers a catalog read from a JSON file on disk.</summary>
    public static IServiceCollection AddAuthKitMessagesFromJson(
        this IServiceCollection services,
        string path,
        IAuthMessages? fallback = null) =>
        services.AddAuthKitMessages(new FileAuthMessagesSource(path, fallback), fallback);

    /// <summary>Registers a catalog fetched from an HTTP(S) URL.</summary>
    public static IServiceCollection AddAuthKitMessagesFromUrl(
        this IServiceCollection services,
        Uri url,
        HttpClient? httpClient = null,
        IAuthMessages? fallback = null) =>
        services.AddAuthKitMessages(
            new HttpAuthMessagesSource(httpClient ?? new HttpClient(), url, fallback),
            fallback);

    /// <summary>
    /// Loads the catalog now. Blazor calls this for you at startup; call it yourself on hosts
    /// without a hosted-service pipeline, before rendering the first component.
    /// </summary>
    public static async Task<IServiceProvider> EnsureAuthKitMessagesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<DeferredAuthMessages>() is { } deferred)
        {
            await deferred.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        }

        return services;
    }
}
