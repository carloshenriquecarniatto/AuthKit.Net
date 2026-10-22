using System.Collections.Concurrent;

namespace AuthKit.Clave;

/// <summary>
/// Records assertion identifiers so a captured assertion cannot be replayed.
/// </summary>
/// <remarks>
/// This cache is the backstop for the request store: <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// has no atomic get-and-delete, so two concurrent posts for the same request may both consume it.
/// Both posts carry the same assertion id, and exactly one of them registers.
/// </remarks>
public interface IClaveReplayCache
{
    /// <summary>Registers an assertion id.</summary>
    /// <param name="assertionId">Assertion <c>ID</c> attribute.</param>
    /// <param name="notOnOrAfter">When the assertion stops being valid; the entry is retained at least until then.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when this is the first registration; <see langword="false"/> on replay.</returns>
    ValueTask<bool> TryRegisterAsync(string assertionId, DateTimeOffset notOnOrAfter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local replay cache for single-instance deployments, tests and demos.
/// </summary>
/// <remarks>
/// A distributed store with <c>SET NX</c> semantics (Redis, a SQL unique constraint, a conditional
/// write) is required behind a load balancer: replay protection that only works on one instance is
/// not replay protection.
/// </remarks>
public sealed class InMemoryClaveReplayCache : IClaveReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>Creates a cache using the system clock.</summary>
    public InMemoryClaveReplayCache()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a cache with an explicit clock, for tests.</summary>
    /// <param name="timeProvider">Clock used for expiry.</param>
    public InMemoryClaveReplayCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _time = timeProvider;
    }

    /// <summary>Number of entries currently held. Intended for tests and diagnostics.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public ValueTask<bool> TryRegisterAsync(string assertionId, DateTimeOffset notOnOrAfter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionId);

        var now = _time.GetUtcNow();
        var expiry = notOnOrAfter > now ? notOnOrAfter : now.AddMinutes(1);

        while (true)
        {
            if (_entries.TryAdd(assertionId, expiry))
            {
                Sweep(now);
                return ValueTask.FromResult(true);
            }

            if (!_entries.TryGetValue(assertionId, out var existing))
            {
                continue;
            }

            // An expired entry may be reused: the assertion it guarded can no longer be accepted
            // for any other reason.
            if (existing > now)
            {
                return ValueTask.FromResult(false);
            }

            if (_entries.TryUpdate(assertionId, expiry, existing))
            {
                return ValueTask.FromResult(true);
            }
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var entry in _entries)
        {
            if (entry.Value <= now)
            {
                _entries.TryRemove(entry);
            }
        }
    }
}
