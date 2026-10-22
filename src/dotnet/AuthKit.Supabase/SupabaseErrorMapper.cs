using Supabase.Gotrue.Exceptions;

namespace AuthKit.Supabase;

/// <summary>
/// Translates GoTrue failures into the AuthKit taxonomy.
/// </summary>
/// <remarks>
/// Server-supplied text is never forwarded: messages are owned by AuthKit, which keeps internal
/// details out of the UI and out of logs. The raw HTTP status is preserved for diagnostics.
/// </remarks>
internal static class SupabaseErrorMapper
{
    public static AuthError Map(Exception exception) => exception switch
    {
        GotrueException gotrue => MapGotrue(gotrue),
        HttpRequestException => AuthError.From(
            AuthErrorCode.NetworkError,
            "Could not reach the authentication service. Check your connection and try again."),
        TaskCanceledException => AuthError.From(
            AuthErrorCode.NetworkError,
            "The authentication request timed out. Try again."),
        _ => AuthError.From(AuthErrorCode.Unknown, "An unexpected error occurred.")
    };

    private static AuthError MapGotrue(GotrueException exception)
    {
        var code = exception.ErrorCode;
        var status = exception.StatusCode is > 0 ? exception.StatusCode : (int?)null;

        if (!string.IsNullOrEmpty(code))
        {
            var mapped = code switch
            {
                "invalid_credentials" or "invalid_grant" => AuthErrorCode.InvalidCredentials,
                "email_not_confirmed" => AuthErrorCode.EmailNotConfirmed,
                "user_already_exists" or "email_exists" => AuthErrorCode.UserAlreadyExists,
                "weak_password" => AuthErrorCode.WeakPassword,
                "otp_expired" or "token_expired" => AuthErrorCode.OtpExpired,
                "invalid_otp" or "otp_disabled" or "token_invalid" => AuthErrorCode.InvalidOtp,
                "over_request_rate_limit" or "over_email_send_rate_limit" or "over_sms_send_rate_limit"
                    => AuthErrorCode.RateLimited,
                "provider_disabled" or "validation_failed" => AuthErrorCode.ProviderDisabled,
                "mfa_required" or "insufficient_aal" => AuthErrorCode.MfaRequired,
                "session_expired" or "refresh_token_not_found" or "refresh_token_already_used"
                    => AuthErrorCode.SessionExpired,
                _ => AuthErrorCode.Unknown
            };

            return AuthError.From(mapped, MessageFor(mapped), status);
        }

        // No machine code: fall back to the status code, then to the transport-level reason.
        return status switch
        {
            400 => AuthError.From(AuthErrorCode.InvalidCredentials, MessageFor(AuthErrorCode.InvalidCredentials), status),
            401 => AuthError.From(AuthErrorCode.SessionExpired, MessageFor(AuthErrorCode.SessionExpired), status),
            403 => AuthError.From(AuthErrorCode.ProviderDisabled, MessageFor(AuthErrorCode.ProviderDisabled), status),
            429 => AuthError.From(AuthErrorCode.RateLimited, MessageFor(AuthErrorCode.RateLimited), status),
            500 or 502 or 503 or 504 => AuthError.From(AuthErrorCode.NetworkError, MessageFor(AuthErrorCode.NetworkError), status),
            _ => AuthError.From(AuthErrorCode.Unknown, MessageFor(AuthErrorCode.Unknown), status)
        };
    }

    private static string MessageFor(AuthErrorCode code) => code switch
    {
        AuthErrorCode.InvalidCredentials => "Invalid email or password.",
        AuthErrorCode.EmailNotConfirmed => "Please confirm your email address before signing in.",
        AuthErrorCode.UserAlreadyExists => "An account with this email already exists.",
        AuthErrorCode.WeakPassword => "The password does not meet the minimum requirements.",
        AuthErrorCode.OtpExpired => "This code has expired. Request a new one.",
        AuthErrorCode.InvalidOtp => "Invalid code. Check the digits and try again.",
        AuthErrorCode.RateLimited => "Too many attempts. Wait a moment and try again.",
        AuthErrorCode.ProviderDisabled => "This sign-in method is not available.",
        AuthErrorCode.MfaRequired => "A second factor is required to continue.",
        AuthErrorCode.SessionExpired => "Your session has expired. Please sign in again.",
        AuthErrorCode.NetworkError => "Could not reach the authentication service. Try again.",
        _ => "An unexpected error occurred."
    };
}
