namespace AuthKit;

/// <summary>
/// Canonical provider identity: <c>audience/providerId</c>. Computed, never parsed from user input.
/// </summary>
public readonly record struct AuthProviderRef
{
    /// <summary>Creates a reference.</summary>
    /// <param name="audience">Host-declared audience.</param>
    /// <param name="providerId">Provider identifier, unique within the audience.</param>
    public AuthProviderRef(string audience, string providerId)
    {
        Audience = audience ?? throw new ArgumentNullException(nameof(audience));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }

    /// <summary>Host-declared audience.</summary>
    public string Audience { get; }

    /// <summary>Provider identifier.</summary>
    public string ProviderId { get; }

    /// <summary>Canonical string, <c>audience/providerId</c>.</summary>
    public string Value => string.Concat(Audience, "/", ProviderId);

    /// <summary>Parses a canonical reference.</summary>
    /// <param name="value">Value in <c>audience/providerId</c> form.</param>
    /// <param name="reference">Parsed reference.</param>
    /// <returns><see langword="true"/> when the value has exactly one separator and both parts.</returns>
    public static bool TryParse(string? value, out AuthProviderRef reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf('/', separator + 1) >= 0)
        {
            return false;
        }

        reference = new AuthProviderRef(value[..separator], value[(separator + 1)..]);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Immutable provenance written onto a session at issue time. A session can always say where it
/// came from, even after the provider is de-registered.
/// </summary>
public sealed record AuthProviderSnapshot
{
    /// <summary>Canonical reference.</summary>
    public required string Ref { get; init; }

    /// <summary>Audience.</summary>
    public required string Audience { get; init; }

    /// <summary>Provider identifier.</summary>
    public required string ProviderId { get; init; }

    /// <summary>Technology.</summary>
    public required string Kind { get; init; }

    /// <summary>Human label at issue time.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Where the provider runs: <c>self_hosted</c>, <c>cloud</c>, <c>third_party</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>Provider-level tenant label, when the provider has one.</summary>
    public string? Tenant { get; init; }

    /// <summary>When the snapshot was taken.</summary>
    public DateTimeOffset CapturedAt { get; init; }
}
