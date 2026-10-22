using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AuthKit.Clave;

/// <summary>
/// A validated Cl@ve identity. This is where the library stops: it never signs anyone in.
/// </summary>
/// <remarks>
/// <see cref="Subject"/> is derived from the document with an HMAC of a host secret, so the same
/// citizen produces a different subject per tenant and the DNI is never a key. See
/// docs/spec-clave.md §5.6.
/// </remarks>
public sealed record ClaveIdentity
{
    /// <summary>Opaque, stable per (tenant, document). Never the document itself.</summary>
    public required string Subject { get; init; }

    /// <summary>Document type.</summary>
    public required AuthDocumentType DocumentType { get; init; }

    /// <summary>Normalised document value.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Document value exactly as the gateway sent it, for audit.</summary>
    public string? DocumentIdRaw { get; init; }

    /// <summary>Given name, when requested and returned.</summary>
    public string? GivenName { get; init; }

    /// <summary>Family name, when requested and returned.</summary>
    public string? FamilyName { get; init; }

    /// <summary>Date of birth, when requested and returned.</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Level of assurance the gateway granted.</summary>
    public required AuthAssuranceLevel Level { get; init; }

    /// <summary>Authentication method reported by the gateway: <c>clave_movil</c>, <c>certificate</c>, ...</summary>
    public required string Method { get; init; }

    /// <summary>Tenant label the host configured.</summary>
    public string? Tenant { get; init; }

    /// <summary>Document of the represented party, when acting on behalf of someone.</summary>
    public string? RepresentedDocumentId { get; init; }

    /// <summary>When the gateway authenticated the user.</summary>
    public DateTimeOffset AuthenticatedAt { get; init; }

    /// <summary>Where to redirect after a successful login. Validated by the host.</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// Projects the protocol-specific identity onto the platform-neutral contract.
    /// </summary>
    /// <returns>The same facts, without the Cl@ve-specific return URL.</returns>
    public AuthIdentity ToIdentity() => new()
    {
        Subject = Subject,
        DocumentType = DocumentType,
        DocumentId = DocumentId,
        DocumentIdRaw = DocumentIdRaw,
        GivenName = GivenName,
        FamilyName = FamilyName,
        DateOfBirth = DateOfBirth,
        Level = Level,
        Method = Method,
        Tenant = Tenant,
        RepresentedDocumentId = RepresentedDocumentId,
        AuthenticatedAt = AuthenticatedAt
    };

    /// <summary>
    /// Projects the identity onto the host's claim set through the single-sourced factory.
    /// See docs/spec-host-integration.md §2.
    /// </summary>
    /// <param name="providerRef">Canonical provider reference, for example <c>clientes/clave</c>.</param>
    /// <param name="includeDocumentId">
    /// When <see langword="true"/>, adds the document identifier as a claim. Off by default because
    /// it is PII that most resource servers do not need.
    /// </param>
    /// <returns>A principal authenticated by the Cl@ve scheme.</returns>
    public ClaimsPrincipal ToClaimsPrincipal(string providerRef, bool includeDocumentId = false) =>
        AuthKitClaimsFactory.Create(ToIdentity(), providerRef, authenticationType: "Clave", includeDocumentId: includeDocumentId);

    /// <summary>
    /// Derives the opaque subject for a document in a tenant.
    /// </summary>
    /// <param name="documentId">Normalised document value.</param>
    /// <param name="tenant">Tenant label.</param>
    /// <param name="key">Host-held HMAC key. At least 32 bytes.</param>
    /// <returns>Base64url-encoded subject.</returns>
    public static string DeriveSubject(string documentId, string tenant, ReadOnlySpan<byte> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        if (key.Length < 32)
        {
            throw new ArgumentException("The subject key must be at least 32 bytes.", nameof(key));
        }

        var payload = Encoding.UTF8.GetBytes(string.Concat(tenant, "\n", documentId));
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, payload, hash);
        return Base64UrlEncode(hash);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"ClaveIdentity {{ Type = {DocumentType}, Level = {Level}, Method = {Method}, Tenant = {Tenant ?? "<none>"}, Document = <redacted> }}";

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Type = ").Append(DocumentType);
        builder.Append(", Level = ").Append(Level);
        builder.Append(", Method = ").Append(Method);
        builder.Append(", Tenant = ").Append(Tenant ?? "<none>");
        builder.Append(", Document = <redacted>");
        return true;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
