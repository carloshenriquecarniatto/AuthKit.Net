namespace AuthKit;

/// <summary>
/// A request to start a server-driven federation. The level and method are requests, not
/// parameters hidden inside a provider string.
/// </summary>
public sealed record AuthFederationRequest
{
    /// <summary>Canonical provider reference, for example <c>clientes/clave</c>.</summary>
    public required string ProviderRef { get; init; }

    /// <summary>Level the caller asks for.</summary>
    public AuthAssuranceLevel Level { get; init; } = AuthAssuranceLevel.Substantial;

    /// <summary>Method to force, when the host narrowed the journey.</summary>
    public string? Method { get; init; }

    /// <summary>Where the host wants the user to land afterwards. Validated by the host.</summary>
    public string? ReturnUrl { get; init; }
}

/// <summary>
/// A provider whose federation runs on the host server. The browser never sees tokens, never parses
/// assertions, and never completes the flow: the host already did. See docs/spec-clave.md §6.
/// </summary>
public interface IServerDrivenAuthProvider : IAuthProvider
{
    /// <summary>
    /// Builds the host's own login URL for a federation. AuthKit never navigates; the host decides
    /// whether that URL becomes a redirect, an in-app browser or a link.
    /// </summary>
    /// <param name="request">Provider, level, method and return URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Absolute URL the host should send the user to.</returns>
    Task<string> BeginFederationAsync(AuthFederationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the outcome the host put in the redirect. No network call and no token exchange:
    /// success is inferred from the host's own session, which exists by the time this runs.
    /// </summary>
    /// <param name="callbackUrl">URL the browser landed on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A failure when the host reported one; otherwise a successful result whose session is the
    /// provider's current session, which may still be <see langword="null"/> until the next refresh.
    /// </returns>
    Task<AuthResult> CompleteFederationAsync(string callbackUrl, CancellationToken cancellationToken = default);
}
