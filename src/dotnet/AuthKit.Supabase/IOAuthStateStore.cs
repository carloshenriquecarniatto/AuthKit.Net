using System.Collections.Concurrent;

namespace AuthKit.Supabase;

/// <summary>
/// Stores the PKCE verifier between the authorization redirect and the callback.
/// </summary>
/// <remarks>
/// The verifier must survive a page reload in browser hosts, so the Blazor package supplies a
/// storage-backed implementation. The in-memory default is only safe for tests and native hosts
/// where the process survives the round trip.
/// </remarks>
public interface IOAuthStateStore
{
    /// <summary>Persists the verifier for a pending authorization.</summary>
    ValueTask SaveAsync(string verifier, CancellationToken cancellationToken = default);

    /// <summary>Reads and removes the verifier, returning <see langword="null"/> when absent.</summary>
    ValueTask<string?> TakeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Process-local PKCE verifier storage. Intended for tests and native hosts.</summary>
public sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask SaveAsync(string verifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);

        // Only the newest attempt is kept: an abandoned authorization must not be replayable.
        _values.Clear();
        _values["pending"] = verifier;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<string?> TakeAsync(CancellationToken cancellationToken = default)
    {
        _values.TryRemove("pending", out var verifier);
        return ValueTask.FromResult(verifier);
    }
}
