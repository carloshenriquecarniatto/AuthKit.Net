using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace AuthKit.Clave;

/// <summary>
/// <see cref="IClaveRequestStore"/> over <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// <see cref="IDistributedCache"/> has no atomic get-and-delete, so two concurrent ACS posts for the
/// same request can both read the entry. The replay cache is the backstop and it works because both
/// posts carry the same assertion id. A host that needs true at-most-once consumption registers its
/// own store over a backend with <c>SET NX</c> semantics.
/// </remarks>
public sealed class DistributedClaveRequestStore : IClaveRequestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly string _prefix;

    /// <summary>Creates the adapter.</summary>
    /// <param name="cache">Distributed cache.</param>
    /// <param name="tenant">Tenant prefix, so two entities in one deployment cannot collide.</param>
    public DistributedClaveRequestStore(IDistributedCache cache, string tenant)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        _prefix = "authkit:clave:" + tenant + ":req:";
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(PendingClaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);
        var lifetime = request.ExpiresAt - DateTimeOffset.UtcNow;
        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = TimeSpan.FromMinutes(1);
        }

        await _cache.SetAsync(
            _prefix + request.RequestId,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<PendingClaveRequest?> TakeAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var key = _prefix + requestId;
        byte[]? payload;
        try
        {
            payload = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ClaveStateUnavailableException("The pending request store is unavailable.", exception);
        }

        if (payload is null)
        {
            return null;
        }

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<PendingClaveRequest>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            // A corrupt entry is dropped for one provider only; it must never break the flow
            // permanently or leak its contents into a log.
            return null;
        }
    }
}

/// <summary>
/// <see cref="IClaveReplayCache"/> over <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// This adapter cannot distinguish a first write from an overwrite, so it is replay-safe only for
/// single-instance deployments. Behind a load balancer, register an implementation over a store with
/// conditional write semantics.
/// </remarks>
public sealed class DistributedClaveReplayCache : IClaveReplayCache
{
    private static readonly byte[] Marker = [1];

    private readonly IDistributedCache _cache;
    private readonly string _prefix;
    private readonly TimeProvider _clock;

    /// <summary>Creates the adapter.</summary>
    /// <param name="cache">Distributed cache.</param>
    /// <param name="tenant">Tenant prefix.</param>
    /// <param name="timeProvider">Clock, for tests.</param>
    public DistributedClaveReplayCache(IDistributedCache cache, string tenant, TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        _prefix = "authkit:clave:" + tenant + ":rpl:";
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryRegisterAsync(string assertionId, DateTimeOffset notOnOrAfter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionId);

        var key = _prefix + assertionId;
        var lifetime = notOnOrAfter - _clock.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = TimeSpan.FromMinutes(1);
        }

        try
        {
            var existing = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return false;
            }

            await _cache.SetAsync(
                key,
                Marker,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ClaveStateUnavailableException("The replay cache is unavailable.", exception);
        }
    }
}

/// <summary>
/// Thrown when federated state cannot be read or written. The login fails closed: a cache outage is
/// a login outage for a sede electrónica, which is the correct trade.
/// </summary>
public sealed class ClaveStateUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Cause, for logs.</param>
    /// <param name="innerException">Underlying failure.</param>
    public ClaveStateUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
