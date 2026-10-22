using System.Security.Cryptography.X509Certificates;

namespace AuthKit.Clave;

/// <summary>
/// Configuration for a Cl@ve entity. Every value here is part of the CAID registration, so the
/// validator treats all of them as release-blocking.
/// </summary>
public sealed class ClaveOptions
{
    /// <summary>Gateway environment. Preproduction and production are separate federations.</summary>
    public ClaveEnvironment Environment { get; set; } = ClaveEnvironment.Preproduction;

    /// <summary>
    /// Service provider entity id, exactly as registered in the CAID. Compared literally by the
    /// gateway, including case and trailing slash.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>Absolute URL the gateway posts the assertion to. Must match the CAID registration.</summary>
    public string? AcsUrl { get; set; }

    /// <summary>
    /// Gateway endpoint. When <see langword="null"/>, the endpoint of <see cref="Environment"/> is
    /// used. Hosts only set this against a local stub.
    /// </summary>
    public Uri? GatewayEndpoint { get; set; }

    /// <summary>
    /// Issuer the gateway is expected to sign with. Defaults to the environment's gateway URL.
    /// </summary>
    public string? ExpectedIssuer { get; set; }

    /// <summary>Name the gateway shows to the user while it authenticates them.</summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// Whether this entity is a public administration or a private company. Part of the eIDAS
    /// extension and visible to the user, so it is configuration, not a default.
    /// </summary>
    public ClaveServiceProviderType ServiceProviderType { get; set; } = ClaveServiceProviderType.Public;

    /// <summary>
    /// When <see langword="true"/>, the gateway re-authenticates even if it already has a session.
    /// Used for high-value routes; the host decides per request through the login endpoint.
    /// </summary>
    public bool ForceAuthn { get; set; }

    /// <summary>Largest response body accepted, in bytes. Bounds the parser before it runs.</summary>
    public int MaxResponseBytes { get; set; } = 512 * 1024;

    /// <summary>SP signing certificate. Must contain the private key; the library never persists it.</summary>
    public X509Certificate2? SigningCertificate { get; set; }

    /// <summary>
    /// Certificate the gateway signs its responses with. Pinned at configuration time and never
    /// fetched at runtime.
    /// </summary>
    public X509Certificate2? PasarelaSigningCertificate { get; set; }

    /// <summary>eIDAS attributes to request. Order is preserved in the request.</summary>
    public IReadOnlyList<string> RequestedAttributes { get; set; } =
    [
        ClaveAttributes.PersonIdentifier,
        ClaveAttributes.GivenName,
        ClaveAttributes.FamilyName,
        ClaveAttributes.DateOfBirth
    ];

    /// <summary>
    /// Attributes marked <c>isRequired="true"</c>. Deliberately narrower than
    /// <see cref="RequestedAttributes"/>: requiring a date of birth breaks login for citizens whose
    /// Cl@ve profile does not carry one.
    /// </summary>
    public IReadOnlyList<string> RequiredAttributes { get; set; } = [ClaveAttributes.PersonIdentifier];

    /// <summary>Level requested when the caller does not specify one.</summary>
    public AuthAssuranceLevel DefaultLevel { get; set; } = AuthAssuranceLevel.Substantial;

    /// <summary>Tenant label, used in the derived subject, claims and audit.</summary>
    public string? Tenant { get; set; }

    /// <summary>How long a started login stays valid.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Tolerance applied to assertion time comparisons.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>HMAC key used to derive the opaque subject. At least 32 bytes.</summary>
    public byte[]? SubjectKey { get; set; }

    /// <summary>Reject DNI/NIE values whose check letter does not match.</summary>
    public bool RequireValidDocumentChecksum { get; set; } = true;
}
