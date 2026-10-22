namespace AuthKit.Clave;

/// <summary>
/// Why a response was rejected. For logs and metrics only.
/// </summary>
/// <remarks>
/// These values must never reach a browser: telling a remote user which check failed tells an
/// attacker where to aim. The outward error is always a generic federation failure.
/// </remarks>
public enum ClaveFailureReason
{
    /// <summary>No reason recorded.</summary>
    None = 0,

    /// <summary>The response was not well-formed, or exceeded the size limit.</summary>
    MalformedResponse,

    /// <summary>A DTD or an external entity was present.</summary>
    UnsafeXml,

    /// <summary>The signature is missing or does not validate against the pinned certificate.</summary>
    SignatureInvalid,

    /// <summary>The issuer is not the expected Cl@ve entity for this environment.</summary>
    IssuerMismatch,

    /// <summary><c>InResponseTo</c> does not match a pending request issued by this host.</summary>
    RequestUnknown,

    /// <summary>The pending request existed but had expired.</summary>
    RequestExpired,

    /// <summary><c>Destination</c> does not equal the configured ACS URL.</summary>
    DestinationMismatch,

    /// <summary>The audience restriction does not contain this entity id.</summary>
    AudienceMismatch,

    /// <summary>The gateway returned a non-success status.</summary>
    StatusNotSuccess,

    /// <summary>The assertion is outside the accepted time window.</summary>
    AssertionExpired,

    /// <summary>The assertion was already seen.</summary>
    ReplayDetected,

    /// <summary>The subject or the person identifier is missing.</summary>
    SubjectMissing,

    /// <summary>The document could not be normalised, or its checksum is wrong.</summary>
    DocumentInvalid,

    /// <summary>The granted level is below the requested one.</summary>
    LevelBelowRequested,

    /// <summary>The state cache is unavailable, so the login cannot be safely completed.</summary>
    StateUnavailable,

    /// <summary>The pinned certificate is not usable for validation.</summary>
    CertificateUnknown,

    /// <summary><c>RelayState</c> does not match the value this host issued.</summary>
    RelayStateMismatch,

    /// <summary>The assertion belongs to another tenant.</summary>
    TenantMismatch
}
