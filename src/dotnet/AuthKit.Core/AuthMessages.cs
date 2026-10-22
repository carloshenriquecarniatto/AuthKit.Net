namespace AuthKit;

/// <summary>
/// Every user-visible string a component can render. Mirrors <c>AuthMessageKey</c> in the
/// contract, so the Angular client exposes the same keys.
/// </summary>
public enum AuthMessageKey
{
    SignIn,
    SignUp,
    Email,
    Password,
    ConfirmPassword,
    ForgotPassword,
    DontHaveAccount,
    HaveAccount,
    SendResetEmail,
    ResetEmailSent,
    BackToSignIn,
    MagicLink,
    CheckYourEmail,
    SigningIn,
    ContinueWithProvider,
    RequiredField,
    InvalidEmail,
    PasswordTooShort,
    PasswordMismatch,
    InvalidOtp,
    EnterOtp,
    Verify,
    NewPassword,
    UpdatePassword,
    PasswordUpdated,
    ShowPassword,
    HidePassword,
    UnexpectedError,
    SessionExpired
}

/// <summary>
/// Port: resolves user-visible strings. Implement it, or use one of the bundled adapters
/// (<see cref="DictionaryAuthMessages"/>, <see cref="JsonAuthMessages"/>).
/// </summary>
public interface IAuthMessages
{
    /// <summary>
    /// Returns the text for a key. <paramref name="provider"/> is supplied only for
    /// <see cref="AuthMessageKey.ContinueWithProvider"/>.
    /// </summary>
    string Resolve(AuthMessageKey key, string? provider = null);
}

/// <summary>
/// Adapter: an in-memory dictionary with an optional fallback for missing keys.
/// This is the default and the fastest option; it never touches the network or the disk.
/// </summary>
public class DictionaryAuthMessages : IAuthMessages
{
    private readonly IReadOnlyDictionary<AuthMessageKey, string> _strings;
    private readonly IAuthMessages? _fallback;

    /// <summary>Creates a catalog. Missing keys fall back, then to the key name.</summary>
    public DictionaryAuthMessages(
        IReadOnlyDictionary<AuthMessageKey, string> strings,
        IAuthMessages? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(strings);
        _strings = strings;
        _fallback = fallback;
    }

    /// <inheritdoc />
    public virtual string Resolve(AuthMessageKey key, string? provider = null)
    {
        var text = _strings.TryGetValue(key, out var value)
            ? value
            : _fallback?.Resolve(key) ?? key.ToString();

        return provider is null
            ? text
            : text.Replace("{provider}", provider, StringComparison.Ordinal);
    }
}

/// <summary>English strings, matching the wording used by supabase_auth_ui.</summary>
public sealed class EnglishAuthMessages : DictionaryAuthMessages
{
    internal static readonly Dictionary<AuthMessageKey, string> DefaultStrings = new()
    {
        [AuthMessageKey.SignIn] = "Sign In",
        [AuthMessageKey.SignUp] = "Sign Up",
        [AuthMessageKey.Email] = "Enter your email",
        [AuthMessageKey.Password] = "Enter your password",
        [AuthMessageKey.ConfirmPassword] = "Confirm Password",
        [AuthMessageKey.ForgotPassword] = "Forgot your password?",
        [AuthMessageKey.DontHaveAccount] = "Don't have an account? Sign up",
        [AuthMessageKey.HaveAccount] = "Already have an account? Sign in",
        [AuthMessageKey.SendResetEmail] = "Send password reset email",
        [AuthMessageKey.ResetEmailSent] = "Password reset email has been sent",
        [AuthMessageKey.BackToSignIn] = "Back to sign in",
        [AuthMessageKey.MagicLink] = "Continue with magic link",
        [AuthMessageKey.CheckYourEmail] = "Check your email inbox!",
        [AuthMessageKey.SigningIn] = "Signing you in",
        [AuthMessageKey.ContinueWithProvider] = "Continue with {provider}",
        [AuthMessageKey.RequiredField] = "This field is required",
        [AuthMessageKey.InvalidEmail] = "Please enter a valid email address",
        [AuthMessageKey.PasswordTooShort] =
            "Please enter a password that is at least {min} characters long",
        [AuthMessageKey.PasswordMismatch] = "Passwords do not match",
        [AuthMessageKey.InvalidOtp] = "Invalid OTP code",
        [AuthMessageKey.EnterOtp] = "Enter the code sent",
        [AuthMessageKey.Verify] = "Verify",
        [AuthMessageKey.NewPassword] = "Enter new password",
        [AuthMessageKey.UpdatePassword] = "Update Password",
        [AuthMessageKey.PasswordUpdated] = "Password successfully updated",
        [AuthMessageKey.ShowPassword] = "Show",
        [AuthMessageKey.HidePassword] = "Hide",
        [AuthMessageKey.UnexpectedError] = "An unexpected error occurred",
        [AuthMessageKey.SessionExpired] = "Your session has expired. Please sign in again."
    };

    /// <summary>Creates the English catalog.</summary>
    public EnglishAuthMessages() : base(DefaultStrings)
    {
    }
}

/// <summary>Access to the bundled message sets and the adapters.</summary>
public static class AuthMessages
{
    private static readonly IAuthMessages EnglishInstance = new EnglishAuthMessages();

    /// <summary>English messages.</summary>
    public static IAuthMessages English => EnglishInstance;

    /// <summary>Builds a catalog from an in-memory dictionary.</summary>
    public static IAuthMessages FromDictionary(
        IReadOnlyDictionary<AuthMessageKey, string> strings,
        IAuthMessages? fallback = null) => new DictionaryAuthMessages(strings, fallback ?? English);

    /// <summary>
    /// Builds a catalog from a JSON object such as <c>{ "signIn": "Entrar" }</c>.
    /// Unknown keys are ignored and missing keys fall back to English.
    /// </summary>
    public static IAuthMessages FromJson(string json, IAuthMessages? fallback = null) =>
        new JsonAuthMessages(json, fallback ?? English);

    /// <summary>Reads a catalog from a file on disk.</summary>
    public static Task<IAuthMessages> FromFileAsync(
        string path,
        IAuthMessages? fallback = null,
        CancellationToken cancellationToken = default) =>
        new FileAuthMessagesSource(path, fallback).LoadAsync(cancellationToken).AsTask();

    /// <summary>Fetches a catalog from a URL.</summary>
    public static Task<IAuthMessages> FromUrlAsync(
        HttpClient httpClient,
        Uri url,
        IAuthMessages? fallback = null,
        CancellationToken cancellationToken = default) =>
        new HttpAuthMessagesSource(httpClient, url, fallback).LoadAsync(cancellationToken).AsTask();
}
