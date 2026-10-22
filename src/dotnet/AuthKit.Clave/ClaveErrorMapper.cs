namespace AuthKit.Clave;

/// <summary>
/// Maps internal failure reasons to the host-facing taxonomy. The mapping is deliberately lossy:
/// several reasons collapse into <c>federation_failed</c> so that a browser cannot tell which check
/// rejected the assertion.
/// </summary>
public static class ClaveErrorMapper
{
    /// <summary>Maps a reason to a safe error.</summary>
    /// <param name="reason">Internal reason from the validator.</param>
    /// <param name="message">Message shown to the user. Must stay generic.</param>
    /// <returns>The error the host surfaces.</returns>
    public static AuthError ToAuthError(ClaveFailureReason reason, string? message = null) =>
        AuthError.From(
            ToErrorCode(reason),
            message ?? AuthErrorMessageDefaults.For(ToErrorCode(reason)));

    /// <summary>Maps a reason to the contract's error code.</summary>
    /// <param name="reason">Internal reason.</param>
    /// <returns>Stable error code.</returns>
    public static AuthErrorCode ToErrorCode(ClaveFailureReason reason) => reason switch
    {
        ClaveFailureReason.LevelBelowRequested => AuthErrorCode.LoaNotMet,
        ClaveFailureReason.SubjectMissing or ClaveFailureReason.DocumentInvalid => AuthErrorCode.IdentityNotVerified,
        ClaveFailureReason.CertificateUnknown => AuthErrorCode.CertificateInvalid,
        ClaveFailureReason.TenantMismatch => AuthErrorCode.TenantMismatch,
        ClaveFailureReason.None => AuthErrorCode.Unknown,
        _ => AuthErrorCode.FederationFailed
    };
}
