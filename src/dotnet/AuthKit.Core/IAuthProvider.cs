namespace AuthKit;

/// <summary>
/// The single abstraction every AuthKit UI binds to. A provider owns credentials, transport and
/// error mapping; it never navigates, never renders and never writes to a logger.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent use, must never throw for expected failures
/// (return <see cref="AuthResult.Failure"/> instead) and must never log token material.
/// </remarks>
public interface IAuthProvider
{
    /// <summary>Flows this provider supports. UI renders only what is declared here.</summary>
    AuthCapabilities Capabilities { get; }

    /// <summary>The current session, or <see langword="null"/> when signed out.</summary>
    AuthSession? CurrentSession { get; }

    /// <summary>Raised whenever the session or auth state changes.</summary>
    event EventHandler<AuthStateChangedEventArgs>? StateChanged;

    /// <summary>Signs in with email and password.</summary>
    Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Creates an account. May require email confirmation before a session is issued.</summary>
    Task<AuthResult> SignUpWithPasswordAsync(string email, string password, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a passwordless sign-in link.</summary>
    Task<AuthResult> SendMagicLinkAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a password recovery email.</summary>
    Task<AuthResult> SendPasswordResetAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password for the current (recovery) session.</summary>
    Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-time code by email.</summary>
    Task<AuthResult> SendEmailOtpAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Verifies a one-time code sent by email.</summary>
    Task<AuthResult> VerifyEmailOtpAsync(string email, string token, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-time code by SMS.</summary>
    Task<AuthResult> SendPhoneOtpAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>Verifies a one-time code sent by SMS.</summary>
    Task<AuthResult> VerifyPhoneOtpAsync(string phone, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the provider authorization URL. The host decides how to navigate to it: a full
    /// page redirect on Blazor, an in-app browser on MAUI.
    /// </summary>
    Task<string> BeginOAuthAsync(string provider, string? redirectTo = null);

    /// <summary>Completes an OAuth flow from the URL the provider redirected to.</summary>
    Task<AuthResult> CompleteOAuthAsync(string callbackUrl, CancellationToken cancellationToken = default);

    /// <summary>Forces a token refresh.</summary>
    Task<AuthResult> RefreshSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs out and clears persisted credentials.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability: a provider that must load its persisted session before use.
/// </summary>
public interface IInitializableAuthProvider
{
    /// <summary>Restores the persisted session, refreshing it when needed.</summary>
    Task<AuthResult> InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists a session between process launches or page reloads.
/// </summary>
/// <remarks>
/// Implementations must encrypt at rest when the platform offers it, must never expose the
/// refresh token to a browser in server-side Blazor, and must tolerate a corrupt payload by
/// returning <see langword="null"/> rather than throwing.
/// </remarks>
public interface IAuthSessionStore
{
    /// <summary>Loads the stored session, or <see langword="null"/> when there is none.</summary>
    ValueTask<AuthSession?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the session, replacing any previous value.</summary>
    ValueTask SaveAsync(AuthSession session, CancellationToken cancellationToken = default);

    /// <summary>Removes any stored session.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Payload of <see cref="IAuthProvider.StateChanged"/>.</summary>
public sealed class AuthStateChangedEventArgs : EventArgs
{
    public AuthStateChangedEventArgs(AuthState state, AuthSession? session, AuthError? error = null)
    {
        State = state;
        Session = session;
        Error = error;
    }

    /// <summary>New state.</summary>
    public AuthState State { get; }

    /// <summary>Session after the transition, when authenticated.</summary>
    public AuthSession? Session { get; }

    /// <summary>Failure that caused an <see cref="AuthState.Error"/> transition.</summary>
    public AuthError? Error { get; }
}

/// <summary>
/// Thrown once, at startup, when the provider is misconfigured. Never thrown for runtime auth
/// failures: those are returned as <see cref="AuthResult"/>.
/// </summary>
public sealed class AuthKitConfigurationException : Exception
{
    public AuthKitConfigurationException(string message) : base(message)
    {
    }

    public AuthKitConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Optional base class for providers. Keeps state bookkeeping, session storage and event
/// raising consistent across implementations.
/// </summary>
public abstract class AuthProviderBase : IAuthProvider
{
    private readonly IAuthSessionStore? _store;
    private AuthSession? _currentSession;

    protected AuthProviderBase(IAuthSessionStore? store = null) => _store = store;

    /// <inheritdoc />
    public abstract AuthCapabilities Capabilities { get; }

    /// <inheritdoc />
    public AuthSession? CurrentSession => _currentSession;

    /// <inheritdoc />
    public event EventHandler<AuthStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public abstract Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> SignUpWithPasswordAsync(string email, string password, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> SendMagicLinkAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> SendPasswordResetAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> SendEmailOtpAsync(string email, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> VerifyEmailOtpAsync(string email, string token, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> SendPhoneOtpAsync(string phone, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> VerifyPhoneOtpAsync(string phone, string token, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<string> BeginOAuthAsync(string provider, string? redirectTo = null);

    /// <inheritdoc />
    public abstract Task<AuthResult> CompleteOAuthAsync(string callbackUrl, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<AuthResult> RefreshSessionAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores a persisted session at startup, refreshing it when expired.</summary>
    protected async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            return;
        }

        var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return;
        }

        _currentSession = stored;
        Raise(AuthState.Authenticated, stored);

        if (stored.IsExpired(DateTimeOffset.UtcNow))
        {
            var refreshed = await RefreshSessionAsync(cancellationToken).ConfigureAwait(false);
            if (!refreshed.Ok)
            {
                await SignOutAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Stores the session and raises <see cref="StateChanged"/>.</summary>
    protected async Task ApplySessionAsync(AuthSession session, CancellationToken cancellationToken)
    {
        _currentSession = session;
        if (_store is not null)
        {
            await _store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        }

        Raise(AuthState.Authenticated, session);
    }

    /// <summary>Clears the session and raises <see cref="StateChanged"/>.</summary>
    protected async Task ClearSessionAsync(CancellationToken cancellationToken)
    {
        _currentSession = null;
        if (_store is not null)
        {
            await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        Raise(AuthState.Idle, null);
    }

    /// <summary>Raises <see cref="StateChanged"/> with an error.</summary>
    protected void RaiseError(AuthError error) => Raise(AuthState.Error, _currentSession, error);

    /// <summary>Raises <see cref="StateChanged"/>.</summary>
    protected void Raise(AuthState state, AuthSession? session, AuthError? error = null) =>
        StateChanged?.Invoke(this, new AuthStateChangedEventArgs(state, session, error));
}
