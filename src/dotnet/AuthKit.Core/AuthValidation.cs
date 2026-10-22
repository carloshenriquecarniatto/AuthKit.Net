using System.Text.RegularExpressions;

namespace AuthKit;

/// <summary>
/// Input rules shared by every renderer. Keeping them here means the Blazor and Angular forms
/// reject exactly the same input, with the same wording.
/// </summary>
public static partial class AuthValidation
{
    /// <summary>Default minimum password length. Deliberately stricter than Supabase's floor of 6.</summary>
    public const int DefaultMinimumPasswordLength = 8;

    /// <summary>Upper bound accepted by the forms, so a hostile paste cannot exhaust the client.</summary>
    public const int MaximumPasswordLength = 1024;

    /// <summary>
    /// Validates an email address. The pattern is non-backtracking, so hostile input cannot
    /// cause catastrophic backtracking (ReDoS); see docs/threat-model.md (T-10).
    /// </summary>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }

        return EmailPattern().IsMatch(email);
    }

    /// <summary>Validates a password against the minimum length rule.</summary>
    public static bool IsValidPassword(string? password, int minimumLength = DefaultMinimumPasswordLength) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= minimumLength &&
        password.Length <= MaximumPasswordLength;

    /// <summary>Validates that two password fields match.</summary>
    public static bool PasswordsMatch(string? password, string? confirmation) =>
        string.Equals(password, confirmation, StringComparison.Ordinal);

    [GeneratedRegex(
        @"^[^@\s]{1,64}@[^@\s.]+(\.[^@\s.]+)+$",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

/// <summary>Form-flow state machine shared by all renderers.</summary>
/// <remarks>
/// The flow guards against double submission, which is both a UX bug and an amplification
/// vector against the provider's rate limits.
/// </remarks>
public sealed class AuthFlow
{
    private readonly object _gate = new();

    /// <summary>Current state.</summary>
    public AuthState State { get; private set; } = AuthState.Idle;

    /// <summary>Last failure, cleared when a new attempt starts.</summary>
    public AuthError? Error { get; private set; }

    /// <summary>Session produced by the last successful attempt.</summary>
    public AuthSession? Session { get; private set; }

    /// <summary>Raised after every state change.</summary>
    public event Action? Changed;

    /// <summary>Whether an attempt is currently in flight.</summary>
    public bool IsBusy => State == AuthState.Submitting;

    /// <summary>
    /// Runs a provider operation, moving through <see cref="AuthState.Submitting"/> and ending in
    /// <see cref="AuthState.Authenticated"/>, <see cref="AuthState.Error"/> or a waiting state.
    /// Concurrent calls are rejected without touching the provider.
    /// </summary>
    public async Task<AuthResult> RunAsync(
        Func<CancellationToken, Task<AuthResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_gate)
        {
            if (IsBusy)
            {
                return AuthResult.Failure(
                    AuthError.From(AuthErrorCode.Unknown, "An authentication request is already in progress."));
            }

            SetState(AuthState.Submitting, clearError: true);
        }

        AuthResult result;
        try
        {
            result = await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A provider that throws is a provider bug; never surface its exception text.
            result = AuthResult.Failure(
                AuthError.From(AuthErrorCode.Unknown, AuthMessages.English.Resolve(AuthMessageKey.UnexpectedError)));
        }

        if (!result.Ok)
        {
            lock (_gate)
            {
                Error = result.Error ?? AuthError.From(
                    AuthErrorCode.Unknown,
                    AuthMessages.English.Resolve(AuthMessageKey.UnexpectedError));
                State = AuthState.Error;
            }

            Changed?.Invoke();
            return result;
        }

        lock (_gate)
        {
            Error = null;
            Session = result.Session;

            State = result.NextStep switch
            {
                AuthNextStep.ConfirmEmail => AuthState.AwaitingEmailConfirmation,
                AuthNextStep.VerifyOtp => AuthState.AwaitingOtp,
                _ => result.Session is not null ? AuthState.Authenticated : AuthState.Idle
            };
        }

        Changed?.Invoke();
        return result;
    }

    /// <summary>Forces a state and notifies listeners.</summary>
    public void SetState(AuthState state, bool clearError = false)
    {
        lock (_gate)
        {
            State = state;
            if (clearError)
            {
                Error = null;
            }
        }

        Changed?.Invoke();
    }
}
