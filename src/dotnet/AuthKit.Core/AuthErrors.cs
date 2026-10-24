namespace AuthKit;

/// <summary>
/// Stable, provider-independent error taxonomy. Mirrors <c>AuthErrorCode</c> in
/// contracts/authkit.contract.json; the wire representation is snake_case.
/// </summary>
public enum AuthErrorCode
{
    /// <summary>The email/password pair was rejected.</summary>
    InvalidCredentials,

    /// <summary>The account exists but the email was never confirmed.</summary>
    EmailNotConfirmed,

    /// <summary>A sign-up collided with an existing account.</summary>
    UserAlreadyExists,

    /// <summary>The provider rejected the password as too weak.</summary>
    WeakPassword,

    /// <summary>The OTP or link has expired.</summary>
    OtpExpired,

    /// <summary>The OTP is wrong.</summary>
    InvalidOtp,

    /// <summary>The provider is throttling requests.</summary>
    RateLimited,

    /// <summary>The requested social provider is not enabled.</summary>
    ProviderDisabled,

    /// <summary>A second factor is required.</summary>
    MfaRequired,

    /// <summary>The session is gone and must be re-established.</summary>
    SessionExpired,

    /// <summary>The request never reached the provider, or the response was unreadable.</summary>
    NetworkError,

    /// <summary>Anything not covered above. The message is safe to display.</summary>
    Unknown,

    /// <summary>No provider is registered for the requested reference or audience.</summary>
    ProviderNotConfigured,

    /// <summary>More than one provider could serve the request, and none is primary.</summary>
    ProviderAmbiguous,

    /// <summary>Two providers hold sessions that cannot be combined on this screen.</summary>
    SessionConflict,

    /// <summary>A provider reference is not in <c>audience/providerId</c> form.</summary>
    ProviderRefMalformed
}

/// <summary>
/// A structured authentication failure. <see cref="Message"/> must already be safe for
/// end users: providers are responsible for mapping raw server text before constructing it.
/// </summary>
public sealed record AuthError
{
    /// <summary>Stable error category.</summary>
    public required AuthErrorCode Code { get; init; }

    /// <summary>Short, user-safe description.</summary>
    public required string Message { get; init; }

    /// <summary>HTTP status returned by the provider, when there was one.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Creates an error with no HTTP status.</summary>
    public static AuthError From(AuthErrorCode code, string message) =>
        new() { Code = code, Message = message };

    /// <summary>Creates an error carrying the provider's HTTP status.</summary>
    public static AuthError From(AuthErrorCode code, string message, int? statusCode) =>
        new() { Code = code, Message = message, StatusCode = statusCode };
}

/// <summary>
/// User-safe default messages for every error code. Providers may refine them; they must never
/// substitute raw server text.
/// </summary>
public static class AuthErrorMessageDefaults
{
    /// <summary>Returns a short sentence that is safe to show to an end user.</summary>
    /// <param name="code">Error code.</param>
    /// <returns>Generic message with no internal detail.</returns>
    public static string For(AuthErrorCode code) => code switch
    {
        AuthErrorCode.InvalidCredentials => "The email or password is incorrect.",
        AuthErrorCode.EmailNotConfirmed => "Confirm your email address before signing in.",
        AuthErrorCode.UserAlreadyExists => "An account with those details already exists.",
        AuthErrorCode.WeakPassword => "Choose a stronger password.",
        AuthErrorCode.OtpExpired => "That code has expired. Request a new one.",
        AuthErrorCode.InvalidOtp => "That code is not valid.",
        AuthErrorCode.RateLimited => "Too many attempts. Wait a moment and try again.",
        AuthErrorCode.ProviderDisabled => "That sign-in method is not available.",
        AuthErrorCode.MfaRequired => "A second factor is required.",
        AuthErrorCode.SessionExpired => "Your session expired. Sign in again.",
        AuthErrorCode.NetworkError => "The service could not be reached. Try again.",
        AuthErrorCode.ProviderNotConfigured => "That sign-in option is not available.",
        AuthErrorCode.ProviderAmbiguous => "Choose which account you want to use.",
        AuthErrorCode.SessionConflict => "You are signed in to incompatible accounts.",
        AuthErrorCode.ProviderRefMalformed => "That sign-in option is not available.",
        _ => "The login could not be completed. Please try again."
    };
}

/// <summary>Wire values for the contract enums. Consumed by the contract parity tests.</summary>
public static class AuthContractValues
{
    /// <summary>snake_case representation of every <see cref="AuthErrorCode"/>.</summary>
    public static IReadOnlyDictionary<AuthErrorCode, string> ErrorCodes { get; } =
        new Dictionary<AuthErrorCode, string>
        {
            [AuthErrorCode.InvalidCredentials] = "invalid_credentials",
            [AuthErrorCode.EmailNotConfirmed] = "email_not_confirmed",
            [AuthErrorCode.UserAlreadyExists] = "user_already_exists",
            [AuthErrorCode.WeakPassword] = "weak_password",
            [AuthErrorCode.OtpExpired] = "otp_expired",
            [AuthErrorCode.InvalidOtp] = "invalid_otp",
            [AuthErrorCode.RateLimited] = "rate_limited",
            [AuthErrorCode.ProviderDisabled] = "provider_disabled",
            [AuthErrorCode.MfaRequired] = "mfa_required",
            [AuthErrorCode.SessionExpired] = "session_expired",
            [AuthErrorCode.NetworkError] = "network_error",
            [AuthErrorCode.Unknown] = "unknown",
            [AuthErrorCode.ProviderNotConfigured] = "provider_not_configured",
            [AuthErrorCode.ProviderAmbiguous] = "provider_ambiguous",
            [AuthErrorCode.SessionConflict] = "session_conflict",
            [AuthErrorCode.ProviderRefMalformed] = "provider_ref_malformed"
        };

    /// <summary>snake_case representation of every <see cref="AuthState"/>.</summary>
    public static IReadOnlyDictionary<AuthState, string> States { get; } =
        new Dictionary<AuthState, string>
        {
            [AuthState.Idle] = "idle",
            [AuthState.Submitting] = "submitting",
            [AuthState.AwaitingEmailConfirmation] = "awaiting_email_confirmation",
            [AuthState.AwaitingOtp] = "awaiting_otp",
            [AuthState.Redirecting] = "redirecting",
            [AuthState.Authenticated] = "authenticated",
            [AuthState.Error] = "error"
        };

    /// <summary>snake_case representation of every <see cref="AuthNextStep"/>.</summary>
    public static IReadOnlyDictionary<AuthNextStep, string> NextSteps { get; } =
        new Dictionary<AuthNextStep, string>
        {
            [AuthNextStep.None] = "none",
            [AuthNextStep.ConfirmEmail] = "confirm_email",
            [AuthNextStep.VerifyOtp] = "verify_otp",
            [AuthNextStep.Mfa] = "mfa"
        };

    /// <summary>snake_case representation of every <see cref="AuthMessageKey"/>.</summary>
    public static IReadOnlyDictionary<AuthMessageKey, string> MessageKeys { get; } =
        new Dictionary<AuthMessageKey, string>
        {
            [AuthMessageKey.SignIn] = "signIn",
            [AuthMessageKey.SignUp] = "signUp",
            [AuthMessageKey.Email] = "email",
            [AuthMessageKey.Password] = "password",
            [AuthMessageKey.ConfirmPassword] = "confirmPassword",
            [AuthMessageKey.ForgotPassword] = "forgotPassword",
            [AuthMessageKey.DontHaveAccount] = "dontHaveAccount",
            [AuthMessageKey.HaveAccount] = "haveAccount",
            [AuthMessageKey.SendResetEmail] = "sendResetEmail",
            [AuthMessageKey.ResetEmailSent] = "resetEmailSent",
            [AuthMessageKey.BackToSignIn] = "backToSignIn",
            [AuthMessageKey.MagicLink] = "magicLink",
            [AuthMessageKey.CheckYourEmail] = "checkYourEmail",
            [AuthMessageKey.SigningIn] = "signingIn",
            [AuthMessageKey.ContinueWithProvider] = "continueWithProvider",
            [AuthMessageKey.RequiredField] = "requiredField",
            [AuthMessageKey.InvalidEmail] = "invalidEmail",
            [AuthMessageKey.PasswordTooShort] = "passwordTooShort",
            [AuthMessageKey.PasswordMismatch] = "passwordMismatch",
            [AuthMessageKey.InvalidOtp] = "invalidOtp",
            [AuthMessageKey.EnterOtp] = "enterOtp",
            [AuthMessageKey.Verify] = "verify",
            [AuthMessageKey.NewPassword] = "newPassword",
            [AuthMessageKey.UpdatePassword] = "updatePassword",
            [AuthMessageKey.PasswordUpdated] = "passwordUpdated",
            [AuthMessageKey.ShowPassword] = "showPassword",
            [AuthMessageKey.HidePassword] = "hidePassword",
            [AuthMessageKey.UnexpectedError] = "unexpectedError",
            [AuthMessageKey.SessionExpired] = "sessionExpired"
        };
}
