using System.Collections.ObjectModel;
using System.Text;

namespace AuthKit;

/// <summary>
/// A user identity as exposed by an <see cref="IAuthProvider"/>.
/// Mirrors the <c>AuthUser</c> type in contracts/authkit.contract.json.
/// </summary>
public sealed record AuthUser
{
    /// <summary>Stable provider-side identifier. Never an email address.</summary>
    public required string Id { get; init; }

    /// <summary>Email address, when the provider exposes one.</summary>
    public string? Email { get; init; }

    /// <summary>Phone number in E.164 format, when available.</summary>
    public string? Phone { get; init; }

    /// <summary>When the email was confirmed, if it has been.</summary>
    public DateTimeOffset? EmailConfirmedAt { get; init; }

    /// <summary>When the phone number was confirmed, if it has been.</summary>
    public DateTimeOffset? PhoneConfirmedAt { get; init; }

    /// <summary>Display name supplied by the provider or by sign-up metadata.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// User metadata. Values are strings so the contract can be reproduced identically
    /// in TypeScript; callers that need richer values should parse them explicitly.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// An authenticated session. Mirrors the <c>AuthSession</c> type in the contract.
/// </summary>
/// <remarks>
/// Token values are deliberately redacted from <see cref="ToString"/> and from record
/// deconstruction output. Logging a session must never leak credentials; see
/// docs/threat-model.md (T-07).
/// </remarks>
public sealed record AuthSession
{
    /// <summary>Short-lived bearer token sent to the data APIs.</summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Long-lived refresh token. Hosts should store it encrypted at rest, and on
    /// server-side Blazor it must never reach the browser.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>Absolute expiry of <see cref="AccessToken"/>.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Token scheme reported by the provider, normally <c>bearer</c>.</summary>
    public string TokenType { get; init; } = "bearer";

    /// <summary>The user this session belongs to.</summary>
    public required AuthUser User { get; init; }

    /// <summary>
    /// Provenance of the session, written once at issue time and never recomputed. Additive in
    /// contract 2.0.0; single-provider hosts may leave it <see langword="null"/>.
    /// </summary>
    public AuthProviderSnapshot? Provider { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> when the access token is expired or expires within 30
    /// seconds. The skew avoids sending a token that dies in flight.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now.AddSeconds(30);

    /// <summary>Time left before expiry, clamped at zero.</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        var remaining = ExpiresAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public override string ToString() =>
        $"AuthSession {{ User = {User?.Id ?? "<none>"}, ExpiresAt = {ExpiresAt:O}, Tokens = <redacted> }}";

    // Replaces the compiler-generated member dump, which would print both tokens.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("User = ").Append(User?.Id ?? "<none>");
        builder.Append(", ExpiresAt = ").Append(ExpiresAt.ToString("O"));
        builder.Append(", Tokens = <redacted>");
        return true;
    }
}

/// <summary>What the caller should do next after an operation completed.</summary>
public enum AuthNextStep
{
    /// <summary>Nothing else is required.</summary>
    None,

    /// <summary>The provider sent a confirmation email; the user must open it.</summary>
    ConfirmEmail,

    /// <summary>An OTP was sent; the user must type it.</summary>
    VerifyOtp,

    /// <summary>The account requires a second factor.</summary>
    Mfa
}

/// <summary>
/// The outcome of every provider operation. Failures are values, not exceptions, so UI
/// code cannot accidentally leak provider exception text to end users.
/// </summary>
public sealed record AuthResult
{
    /// <summary>Whether the operation itself succeeded.</summary>
    public required bool Ok { get; init; }

    /// <summary>Session produced by the operation, when one was issued.</summary>
    public AuthSession? Session { get; init; }

    /// <summary>Structured failure, when <see cref="Ok"/> is <see langword="false"/>.</summary>
    public AuthError? Error { get; init; }

    /// <summary>Required follow-up, if any.</summary>
    public AuthNextStep NextStep { get; init; } = AuthNextStep.None;

    /// <summary>Creates a successful result carrying a session.</summary>
    public static AuthResult Success(AuthSession session) =>
        new() { Ok = true, Session = session };

    /// <summary>
    /// Creates a successful result that still needs a user action, such as confirming an
    /// email or typing an OTP.
    /// </summary>
    public static AuthResult Continue(AuthNextStep step) =>
        new() { Ok = true, NextStep = step };

    /// <summary>Creates a failed result from a structured error.</summary>
    public static AuthResult Failure(AuthError error) =>
        new() { Ok = false, Error = error };
}

/// <summary>Lifecycle of an authentication form, shared by every UI renderer.</summary>
public enum AuthState
{
    /// <summary>Waiting for input.</summary>
    Idle,

    /// <summary>An operation is in flight; inputs must be disabled.</summary>
    Submitting,

    /// <summary>Waiting for the user to open a confirmation email.</summary>
    AwaitingEmailConfirmation,

    /// <summary>Waiting for the user to type an OTP.</summary>
    AwaitingOtp,

    /// <summary>The user is being sent to an external provider.</summary>
    Redirecting,

    /// <summary>A session is available.</summary>
    Authenticated,

    /// <summary>The last operation failed.</summary>
    Error
}

/// <summary>
/// What a provider can actually do. UI renders only the flows the provider declares, which is
/// what makes components provider-agnostic.
/// </summary>
public sealed record AuthCapabilities
{
    /// <summary>Email and password sign-in.</summary>
    public bool Password { get; init; }

    /// <summary>Account creation.</summary>
    public bool SignUp { get; init; }

    /// <summary>Passwordless sign-in through an emailed link.</summary>
    public bool MagicLink { get; init; }

    /// <summary>Passwordless sign-in through an emailed one-time code.</summary>
    public bool EmailOtp { get; init; }

    /// <summary>Passwordless sign-in through an SMS one-time code.</summary>
    public bool PhoneOtp { get; init; }

    /// <summary>Password recovery through email.</summary>
    public bool PasswordRecovery { get; init; }

    /// <summary>Multi-factor authentication support.</summary>
    public bool Mfa { get; init; }

    /// <summary>Social providers accepted by <see cref="IAuthProvider.BeginOAuthAsync"/>.</summary>
    public IReadOnlyList<string> SocialProviders { get; init; } = [];
}
