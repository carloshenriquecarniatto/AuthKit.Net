namespace AuthKit.Blazor;

/// <summary>
/// What a Cl@ve (or any server-driven federation) callback told the browser. Success is inferred
/// from the host's own session: the callback carries no token, because the browser never had one.
/// </summary>
public sealed record AuthKitFederationOutcome
{
    /// <summary>Whether the host reported an error.</summary>
    public required bool Ok { get; init; }

    /// <summary>Mapped error, when <see cref="Ok"/> is <see langword="false"/>.</summary>
    public AuthError? Error { get; init; }

    /// <summary>
    /// Current URL with the federation query parameters removed. The host may navigate to it or
    /// ignore it; AuthKit never navigates on its own.
    /// </summary>
    public required string CleanUrl { get; init; }

    /// <summary>Absolute URL the host returned, when it supplied one.</summary>
    public string? ReturnUrl { get; init; }
}
