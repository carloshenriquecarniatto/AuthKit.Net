using System.Security.Claims;
using System.Text;

namespace AuthKit;

/// <summary>
/// Type of a national identity document. Mirrors <c>AuthDocumentType</c> in the contract.
/// </summary>
public enum AuthDocumentType
{
    /// <summary>Documento Nacional de Identidad.</summary>
    Dni,

    /// <summary>Número de Identificación de Extranjero.</summary>
    Nie,

    /// <summary>Legal entity identifier.</summary>
    Cif,

    /// <summary>A shape the library recognises but does not classify.</summary>
    Unknown
}

/// <summary>
/// eIDAS level of assurance, ordered from weakest to strongest. The declaration order is the
/// comparison order; <see cref="AuthAssuranceLevels.Satisfies"/> relies on it.
/// </summary>
public enum AuthAssuranceLevel
{
    /// <summary>Low.</summary>
    Low,

    /// <summary>Substantial.</summary>
    Substantial,

    /// <summary>High.</summary>
    High
}

/// <summary>Maps <see cref="AuthAssuranceLevel"/> to and from the eIDAS authentication context URIs.</summary>
public static class AuthAssuranceLevels
{
    private const string Prefix = "http://eidas.europa.eu/LoA/";

    /// <summary>Returns the <c>acr</c> value for a level.</summary>
    /// <param name="level">Level to map.</param>
    /// <returns>Absolute URI.</returns>
    public static string ToAcrValue(AuthAssuranceLevel level) => level switch
    {
        AuthAssuranceLevel.Low => Prefix + "low",
        AuthAssuranceLevel.Substantial => Prefix + "substantial",
        AuthAssuranceLevel.High => Prefix + "high",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown assurance level.")
    };

    /// <summary>Parses an <c>acr</c> value.</summary>
    /// <param name="acrValue">Value from an assertion or token.</param>
    /// <param name="level">Parsed level when the value is recognised.</param>
    /// <returns><see langword="true"/> when the value maps to a known level.</returns>
    public static bool TryParseAcrValue(string? acrValue, out AuthAssuranceLevel level)
    {
        level = default;
        if (string.IsNullOrWhiteSpace(acrValue) || !acrValue.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tail = acrValue.AsSpan(Prefix.Length);
        if (tail.Equals("low", StringComparison.OrdinalIgnoreCase))
        {
            level = AuthAssuranceLevel.Low;
            return true;
        }

        if (tail.Equals("substantial", StringComparison.OrdinalIgnoreCase))
        {
            level = AuthAssuranceLevel.Substantial;
            return true;
        }

        if (tail.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            level = AuthAssuranceLevel.High;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Compares two levels. A missing <c>acr</c> fails closed: the caller must parse first and
    /// reject when parsing fails.
    /// </summary>
    /// <param name="granted">Level returned by the identity source.</param>
    /// <param name="requested">Level the route requires.</param>
    /// <returns>Whether the granted level is at least the requested one.</returns>
    public static bool Satisfies(AuthAssuranceLevel granted, AuthAssuranceLevel requested) => granted >= requested;
}

/// <summary>
/// The legal identity behind a session, independent of the protocol that delivered it.
/// Mirrors <c>AuthIdentity</c> in the contract.
/// </summary>
/// <remarks>
/// <see cref="Subject"/> is opaque and stable per (tenant, document); the document number is never
/// the subject. The document itself is PII and must never be logged. See docs/spec-clave.md §5.6.
/// </remarks>
public sealed record AuthIdentity
{
    /// <summary>Opaque, stable identifier. Never the document number.</summary>
    public required string Subject { get; init; }

    /// <summary>Document type.</summary>
    public required AuthDocumentType DocumentType { get; init; }

    /// <summary>Normalised document value.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Document value exactly as the identity source sent it, for audit.</summary>
    public string? DocumentIdRaw { get; init; }

    /// <summary>Given name, when available.</summary>
    public string? GivenName { get; init; }

    /// <summary>Family name, when available.</summary>
    public string? FamilyName { get; init; }

    /// <summary>Date of birth, when available and requested.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Granted level of assurance.</summary>
    public required AuthAssuranceLevel Level { get; init; }

    /// <summary>Authentication method: <c>clave_movil</c>, <c>certificate</c>, ...</summary>
    public required string Method { get; init; }

    /// <summary>Tenant label declared by the host.</summary>
    public string? Tenant { get; init; }

    /// <summary>Document of the represented party, when acting on behalf of someone.</summary>
    public string? RepresentedDocumentId { get; init; }

    /// <summary>When the identity source authenticated the user.</summary>
    public DateTimeOffset AuthenticatedAt { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"AuthIdentity {{ Type = {DocumentType}, Level = {Level}, Method = {Method}, Tenant = {Tenant ?? "<none>"}, Document = <redacted> }}";

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Type = ").Append(DocumentType);
        builder.Append(", Level = ").Append(Level);
        builder.Append(", Method = ").Append(Method);
        builder.Append(", Tenant = ").Append(Tenant ?? "<none>");
        builder.Append(", Document = <redacted>");
        return true;
    }
}

/// <summary>
/// One authentication method offered by a federation provider. Rendered by the UI from data;
/// there is no switch on method identifiers anywhere in AuthKit.
/// </summary>
public sealed record AuthFederationMethod
{
    /// <summary>Stable identifier, for example <c>clave_movil</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Label shown to the user. Supplied by the host, never hardcoded.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional help text.</summary>
    public string? Description { get; init; }

    /// <summary>Levels this method can grant.</summary>
    public IReadOnlyList<AuthAssuranceLevel> Levels { get; init; } = [];

    /// <summary>Whether this is the method the UI should suggest first.</summary>
    public bool Recommended { get; init; }
}

/// <summary>
/// A federation provider as the UI needs it: identity, labels and the methods it offers.
/// Mirrors <c>AuthFederationProvider</c> in the contract.
/// </summary>
public sealed record AuthFederationProvider
{
    /// <summary>Provider identifier, unique within its audience.</summary>
    public required string Id { get; init; }

    /// <summary>Technology behind the provider: <c>clave</c>, <c>entra</c>, ...</summary>
    public required string Kind { get; init; }

    /// <summary>Human label, unique within the audience.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional description shown by a picker.</summary>
    public string? Description { get; init; }

    /// <summary>Optional icon URL. Hosts supply the official logotype.</summary>
    public string? IconUrl { get; init; }

    /// <summary>Methods rendered as buttons.</summary>
    public IReadOnlyList<AuthFederationMethod> Methods { get; init; } = [];

    /// <summary>Levels the provider can grant. A requested level outside this set is invalid.</summary>
    public IReadOnlyList<AuthAssuranceLevel> Levels { get; init; } = [];

    /// <summary>Whether this provider is the default for its audience.</summary>
    public bool IsPrimary { get; init; }
}

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

    /// <summary>Federation-level tenant label, when the provider has one.</summary>
    public string? Tenant { get; init; }

    /// <summary>When the snapshot was taken.</summary>
    public DateTimeOffset CapturedAt { get; init; }
}

/// <summary>
/// Builds the canonical claim set once, for every platform adapter. A second mapping would drift,
/// and a drifted <c>acr</c> is an authorization bypass. See docs/spec-host-integration.md §2.
/// </summary>
public static class AuthKitClaimsFactory
{
    /// <summary>Claim carrying the canonical provider reference.</summary>
    public const string Provider = "authkit:provider";

    /// <summary>Claim carrying the audience.</summary>
    public const string Audience = "authkit:audience";

    /// <summary>Claim carrying the federation tenant.</summary>
    public const string Tenant = "authkit:tenant";

    /// <summary>Claim carrying the document type.</summary>
    public const string DocumentType = "authkit:doc_type";

    /// <summary>Claim carrying the document number. PII: opt-in only.</summary>
    public const string DocumentId = "authkit:doc_id";

    /// <summary>Builds the principal for an identity.</summary>
    /// <param name="identity">Validated identity.</param>
    /// <param name="providerRef">Canonical provider reference, for example <c>clientes/clave</c>.</param>
    /// <param name="audience">Audience when it differs from the reference prefix.</param>
    /// <param name="authenticationType">Authentication type stored on the identity.</param>
    /// <param name="includeDocumentId">
    /// Adds the document number as a claim. Off by default: it is PII that most resource servers
    /// do not need, and a claim is a log field waiting to happen.
    /// </param>
    /// <returns>A principal carrying the normative claim set.</returns>
    public static ClaimsPrincipal Create(
        AuthIdentity identity,
        string providerRef,
        string? audience = null,
        string authenticationType = "AuthKit",
        bool includeDocumentId = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Subject);

        var claims = new List<Claim>
        {
            new("sub", identity.Subject),
            new("acr", AuthAssuranceLevels.ToAcrValue(identity.Level)),
            new("amr", identity.Method),
            new(Provider, providerRef)
        };

        var resolvedAudience = audience
            ?? (AuthProviderRef.TryParse(providerRef, out var parsed) ? parsed.Audience : null);
        if (!string.IsNullOrWhiteSpace(resolvedAudience))
        {
            claims.Add(new Claim(Audience, resolvedAudience));
        }

        var displayName = string.Join(
            ' ',
            new[] { identity.GivenName, identity.FamilyName }.Where(static s => !string.IsNullOrWhiteSpace(s)));
        if (displayName.Length > 0)
        {
            claims.Add(new Claim("name", displayName));
        }

        if (!string.IsNullOrWhiteSpace(identity.Tenant))
        {
            claims.Add(new Claim(Tenant, identity.Tenant));
        }

        if (!string.IsNullOrWhiteSpace(identity.RepresentedDocumentId))
        {
            claims.Add(new Claim("act", identity.RepresentedDocumentId));
        }

        claims.Add(new Claim(DocumentType, identity.DocumentType switch
        {
            AuthDocumentType.Dni => "dni",
            AuthDocumentType.Nie => "nie",
            AuthDocumentType.Cif => "cif",
            _ => "unknown"
        }));

        if (includeDocumentId)
        {
            claims.Add(new Claim(DocumentId, identity.DocumentId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
