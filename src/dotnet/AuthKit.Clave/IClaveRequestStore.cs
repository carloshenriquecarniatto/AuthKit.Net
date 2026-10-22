using System.Collections.Concurrent;

namespace AuthKit.Clave;

/// <summary>
/// Stores pending login attempts so that <c>InResponseTo</c> can be validated.
/// </summary>
/// <remarks>
/// There is no stateless mode: without this store, any assertion that is properly signed and
/// addressed to this entity would be accepted, including one replayed from a previous session.
/// </remarks>
public interface IClaveRequestStore
{
    /// <summary>Stores a pending request, replacing any entry with the same id.</summary>
    /// <param name="request">Request to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is stored.</returns>
    ValueTask SaveAsync(PendingClaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a pending request, at most once.
    /// </summary>
    /// <param name="requestId">Request id from <c>InResponseTo</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored request, or <see langword="null"/> when unknown or already consumed.</returns>
    ValueTask<PendingClaveRequest?> TakeAsync(string requestId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local request store for single-instance deployments, tests and demos.
/// </summary>
/// <remarks>
/// Behind a load balancer this fails intermittently and silently: the gateway posts to an instance
/// that never saw the request. Register a distributed implementation for any scaled-out deployment.
/// </remarks>
public sealed class InMemoryClaveRequestStore : IClaveRequestStore
{
    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, PendingClaveRequest> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>Creates a store using the system clock.</summary>
    public InMemoryClaveRequestStore()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a store with an explicit clock, for tests.</summary>
    /// <param name="timeProvider">Clock used for expiry sweeps.</param>
    public InMemoryClaveRequestStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _time = timeProvider;
    }

    /// <summary>Number of entries currently held. Intended for tests and diagnostics.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public ValueTask SaveAsync(PendingClaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _entries[request.RequestId] = request;
        Sweep();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<PendingClaveRequest?> TakeAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        _entries.TryRemove(requestId, out var pending);
        return ValueTask.FromResult(pending);
    }

    private void Sweep()
    {
        // Expired entries are retained briefly on purpose: the ACS reports "RequestExpired"
        // instead of "RequestUnknown" when the user comes back late, which is an operational
        // fact the host needs. The retention window bounds memory without erasing the reason.
        var now = _time.GetUtcNow();
        foreach (var entry in _entries)
        {
            if (entry.Value.ExpiresAt + RetentionAfterExpiry <= now)
            {
                _entries.TryRemove(entry);
            }
        }
    }
}
