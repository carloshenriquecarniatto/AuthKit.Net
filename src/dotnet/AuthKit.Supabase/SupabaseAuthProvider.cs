using System.Globalization;
using System.Web;
using Supabase.Gotrue;
using static Supabase.Gotrue.Constants;
using SupabaseClient = global::Supabase.Client;
using Supabase.Gotrue.Interfaces;

namespace AuthKit.Supabase;

/// <summary>
/// Supabase Auth implementation of <see cref="IAuthProvider"/>.
/// </summary>
/// <remarks>
/// Register this type with a scoped lifetime. In Blazor Server a single instance must never be
/// shared between circuits, otherwise one user's session would be visible to another
/// (docs/threat-model.md, T-01).
/// </remarks>
public sealed class SupabaseAuthProvider : AuthProviderBase, IInitializableAuthProvider, IDisposable
{
    private readonly SupabaseClient _client;
    private readonly SupabaseAuthOptions _options;
    private readonly IOAuthStateStore _oauthState;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SupabaseAuthProvider(
        SupabaseClient client,
        SupabaseAuthOptions options,
        IOAuthStateStore? oauthState = null,
        IAuthSessionStore? sessionStore = null)
        : base(sessionStore ?? options.SessionStore)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _oauthState = oauthState ?? new InMemoryOAuthStateStore();
    }

    /// <inheritdoc />
    public override AuthCapabilities Capabilities => new()
    {
        Password = true,
        SignUp = true,
        MagicLink = true,
        EmailOtp = true,
        PhoneOtp = true,
        PasswordRecovery = true,
        Mfa = true,
        SocialProviders = _options.SocialProviders
    };

    /// <inheritdoc />
    public async Task<AuthResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.SessionStore is null)
        {
            return AuthResult.Continue(AuthNextStep.None);
        }

        var stored = await _options.SessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return AuthResult.Continue(AuthNextStep.None);
        }

        try
        {
            var expired = stored.IsExpired(DateTimeOffset.UtcNow);
            var session = await _client.Auth
                .SetSession(stored.AccessToken, stored.RefreshToken ?? string.Empty, expired)
                .ConfigureAwait(false);

            var mapped = MapSession(session);
            await ApplySessionAsync(mapped, cancellationToken).ConfigureAwait(false);
            return AuthResult.Success(mapped);
        }
        catch (Exception exception)
        {
            var error = SupabaseErrorMapper.Map(exception);
            await SignOutAsync(cancellationToken).ConfigureAwait(false);
            RaiseError(error);
            return AuthResult.Failure(error);
        }
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidEmail(email) || string.IsNullOrEmpty(password))
        {
            return AuthResult.Failure(AuthError.From(
                AuthErrorCode.InvalidCredentials,
                "Enter a valid email and password."));
        }

        return await GuardAsync(async () =>
        {
            var session = await _client.Auth.SignInWithPassword(email, password).ConfigureAwait(false);

            // The SDK returns null when the account exists but the email was never confirmed.
            return session is null
                ? AuthResult.Failure(AuthError.From(
                    AuthErrorCode.EmailNotConfirmed,
                    "Please confirm your email address before signing in."))
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SignUpWithPasswordAsync(
        string email,
        string password,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidEmail(email))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Enter a valid email address."));
        }

        if (!AuthValidation.IsValidPassword(password))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.WeakPassword, "The password does not meet the minimum requirements."));
        }

        return await GuardAsync(async () =>
        {
            var options = new SignUpOptions
            {
                Data = metadata?.ToDictionary(pair => pair.Key, pair => (object)pair.Value)
            };

            var session = await _client.Auth.SignUp(email, password, options).ConfigureAwait(false);

            // When email confirmation is enabled the SDK still returns a Session object, but
            // without an access token. That is a "check your inbox" state, not a login.
            return session is null || string.IsNullOrEmpty(session.AccessToken)
                ? AuthResult.Continue(AuthNextStep.ConfirmEmail)
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SendMagicLinkAsync(
        string email,
        string? redirectTo = null,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidEmail(email))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Enter a valid email address."));
        }

        return await GuardAsync(async () =>
        {
            var sent = await _client.Auth.SendMagicLink(email, new SignInOptions
            {
                RedirectTo = redirectTo ?? _options.RedirectTo,
                FlowType = OAuthFlowType.PKCE
            }).ConfigureAwait(false);

            return sent
                ? AuthResult.Continue(AuthNextStep.ConfirmEmail)
                : AuthResult.Failure(AuthError.From(AuthErrorCode.Unknown, "The magic link could not be sent."));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SendPasswordResetAsync(
        string email,
        string? redirectTo = null,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidEmail(email))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Enter a valid email address."));
        }

        return await GuardAsync(async () =>
        {
            var state = await _client.Auth.ResetPasswordForEmail(new ResetPasswordForEmailOptions(email)
            {
                RedirectTo = redirectTo ?? _options.RedirectTo,
                FlowType = OAuthFlowType.PKCE
            }).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(state.PKCEVerifier))
            {
                await _oauthState.SaveAsync(state.PKCEVerifier, cancellationToken).ConfigureAwait(false);
            }

            return AuthResult.Continue(AuthNextStep.ConfirmEmail);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> UpdatePasswordAsync(
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidPassword(newPassword))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.WeakPassword, "The password does not meet the minimum requirements."));
        }

        return await GuardAsync(async () =>
        {
            await _client.Auth.Update(new UserAttributes { Password = newPassword }).ConfigureAwait(false);
            return CurrentSession is null
                ? AuthResult.Continue(AuthNextStep.None)
                : AuthResult.Success(CurrentSession);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SendEmailOtpAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsValidEmail(email))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Enter a valid email address."));
        }

        return await GuardAsync(async () =>
        {
            await _client.Auth.SignInWithOtp(new SignInWithPasswordlessEmailOptions(email)
            {
                EmailRedirectTo = _options.RedirectTo,
                FlowType = OAuthFlowType.PKCE
            }).ConfigureAwait(false);

            return AuthResult.Continue(AuthNextStep.VerifyOtp);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> VerifyEmailOtpAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidOtp, "Enter the code you received."));
        }

        return await GuardAsync(async () =>
        {
            var session = await _client.Auth
                .VerifyOTP(email, token, EmailOtpType.Email)
                .ConfigureAwait(false);

            return session is null
                ? AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidOtp, "Invalid code. Check the digits and try again."))
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> SendPhoneOtpAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Enter a phone number."));
        }

        return await GuardAsync(async () =>
        {
            await _client.Auth.SignInWithOtp(new SignInWithPasswordlessPhoneOptions(phone)).ConfigureAwait(false);
            return AuthResult.Continue(AuthNextStep.VerifyOtp);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> VerifyPhoneOtpAsync(
        string phone,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidOtp, "Enter the code you received."));
        }

        return await GuardAsync(async () =>
        {
            var session = await _client.Auth
                .VerifyOTP(phone, token, MobileOtpType.SMS)
                .ConfigureAwait(false);

            return session is null
                ? AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidOtp, "Invalid code. Check the digits and try again."))
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<string> BeginOAuthAsync(string provider, string? redirectTo = null)
    {
        if (!Enum.TryParse<Provider>(provider, ignoreCase: true, out var parsed))
        {
            throw new AuthKitConfigurationException(
                $"Unknown social provider '{provider}'. Use a Supabase provider name such as google, github or apple.");
        }

        var state = await _client.Auth.SignIn(parsed, new SignInOptions
        {
            RedirectTo = redirectTo ?? _options.RedirectTo,
            FlowType = OAuthFlowType.PKCE
        }).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(state.PKCEVerifier))
        {
            await _oauthState.SaveAsync(state.PKCEVerifier).ConfigureAwait(false);
        }

        return state.Uri.ToString();
    }

    /// <inheritdoc />
    public override async Task<AuthResult> CompleteOAuthAsync(
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri))
        {
            return AuthResult.Failure(AuthError.From(AuthErrorCode.Unknown, "The authentication callback URL is invalid."));
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query["error"] is { Length: > 0 } oauthError)
        {
            return AuthResult.Failure(AuthError.From(
                oauthError.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
                    ? AuthErrorCode.ProviderDisabled
                    : AuthErrorCode.Unknown,
                "The provider did not complete the sign in."));
        }

        return await GuardAsync(async () =>
        {
            var code = query["code"];
            Session? session;

            if (!string.IsNullOrEmpty(code))
            {
                var verifier = await _oauthState.TakeAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(verifier))
                {
                    return AuthResult.Failure(AuthError.From(
                        AuthErrorCode.SessionExpired,
                        "This sign in attempt expired. Start again."));
                }

                session = await _client.Auth.ExchangeCodeForSession(verifier, code).ConfigureAwait(false);
            }
            else
            {
                session = await _client.Auth.GetSessionFromUrl(uri, storeSession: false).ConfigureAwait(false);
            }

            return session is null
                ? AuthResult.Failure(AuthError.From(AuthErrorCode.Unknown, "The provider did not return a session."))
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<AuthResult> RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSession is null)
        {
            return AuthResult.Failure(AuthError.From(
                AuthErrorCode.SessionExpired,
                "Your session has expired. Please sign in again."));
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _client.Auth.RefreshSession().ConfigureAwait(false);
            return session is null
                ? AuthResult.Failure(AuthError.From(AuthErrorCode.SessionExpired, "Your session has expired."))
                : await CompleteAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var error = SupabaseErrorMapper.Map(exception);
            RaiseError(error);
            return AuthResult.Failure(error);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    /// <inheritdoc />
    public override async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Auth.SignOut(SignOutScope.Global).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Signing out locally must succeed even when the network call fails.
        }

        await ClearSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthResult> CompleteAsync(Session session, CancellationToken cancellationToken)
    {
        var mapped = MapSession(session);
        await ApplySessionAsync(mapped, cancellationToken).ConfigureAwait(false);
        return AuthResult.Success(mapped);
    }

    private async Task<AuthResult> GuardAsync(
        Func<Task<AuthResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = SupabaseErrorMapper.Map(exception);
            RaiseError(error);
            return AuthResult.Failure(error);
        }
    }

    private static AuthSession MapSession(Session session)
    {
        var expiresAt = session.ExpiresIn > 0
            ? DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn)
            : new DateTimeOffset(session.CreatedAt, TimeSpan.Zero).AddHours(1);

        return new AuthSession
        {
            AccessToken = session.AccessToken ?? string.Empty,
            RefreshToken = session.RefreshToken,
            ExpiresAt = expiresAt,
            TokenType = string.IsNullOrEmpty(session.TokenType) ? "bearer" : session.TokenType,
            User = MapUser(session.User)
        };
    }

    private static AuthUser MapUser(User? user)
    {
        if (user is null)
        {
            return new AuthUser { Id = string.Empty };
        }

        var metadata = user.UserMetadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : user.UserMetadata.ToDictionary(
                pair => pair.Key,
                pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);

        return new AuthUser
        {
            Id = user.Id ?? string.Empty,
            Email = user.Email,
            Phone = user.Phone,
            EmailConfirmedAt = ToOffset(user.EmailConfirmedAt),
            PhoneConfirmedAt = ToOffset(user.PhoneConfirmedAt),
            DisplayName = FirstNonEmpty(metadata, "display_name", "full_name", "name", "username"),
            Metadata = metadata
        };
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero);

    private static string? FirstNonEmpty(Dictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
