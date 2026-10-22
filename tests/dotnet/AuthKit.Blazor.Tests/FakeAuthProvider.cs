namespace AuthKit.Blazor.Tests;

/// <summary>Deterministic provider used to render components without a network or a backend.</summary>
public sealed class FakeAuthProvider : IAuthProvider
{
    public AuthCapabilities Capabilities { get; set; } = new()
    {
        Password = true,
        SignUp = true,
        MagicLink = true,
        PasswordRecovery = true,
        EmailOtp = true,
        PhoneOtp = true,
        SocialProviders = ["google", "github"]
    };

    public AuthResult SignInResult { get; set; } = AuthResult.Success(new AuthSession
    {
        AccessToken = "access",
        RefreshToken = "refresh",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        User = new AuthUser { Id = "user-1", Email = "ada@example.com" }
    });

    public AuthResult SignUpResult { get; set; } = AuthResult.Continue(AuthNextStep.ConfirmEmail);

    public string AuthorizationUrl { get; set; } = "https://project.supabase.co/auth/v1/authorize?provider=google";

    public string? LastEmail { get; private set; }

    public int MagicLinkCalls { get; private set; }

    public AuthSession? CurrentSession { get; private set; }

    public event EventHandler<AuthStateChangedEventArgs>? StateChanged;

    public Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        LastEmail = email;
        CurrentSession = SignInResult.Session;
        StateChanged?.Invoke(this, new AuthStateChangedEventArgs(AuthState.Authenticated, CurrentSession));
        return Task.FromResult(SignInResult);
    }

    public Task<AuthResult> SignUpWithPasswordAsync(string email, string password, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        LastEmail = email;
        return Task.FromResult(SignUpResult);
    }

    public Task<AuthResult> SendMagicLinkAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default)
    {
        MagicLinkCalls++;
        return Task.FromResult(AuthResult.Continue(AuthNextStep.ConfirmEmail));
    }

    public Task<AuthResult> SendPasswordResetAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthResult.Continue(AuthNextStep.ConfirmEmail));

    public Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthResult.Continue(AuthNextStep.None));

    public Task<AuthResult> SendEmailOtpAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthResult.Continue(AuthNextStep.VerifyOtp));

    public Task<AuthResult> VerifyEmailOtpAsync(string email, string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult);

    public Task<AuthResult> SendPhoneOtpAsync(string phone, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthResult.Continue(AuthNextStep.VerifyOtp));

    public Task<AuthResult> VerifyPhoneOtpAsync(string phone, string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult);

    public Task<string> BeginOAuthAsync(string provider, string? redirectTo = null) =>
        Task.FromResult(AuthorizationUrl);

    public Task<AuthResult> CompleteOAuthAsync(string callbackUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult);

    public Task<AuthResult> RefreshSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult);

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        CurrentSession = null;
        StateChanged?.Invoke(this, new AuthStateChangedEventArgs(AuthState.Idle, null));
        return Task.CompletedTask;
    }
}
