using System.Globalization;

namespace AuthKit.Clave;

/// <summary>
/// The browser-facing, server-driven provider. It builds the host login URL and reads the host's
/// redirect outcome. It never parses SAML, never holds a token, never refreshes one and never
/// navigates. See docs/spec-clave.md §6.
/// </summary>
public sealed class ClaveAuthProvider : IServerDrivenAuthProvider
{
    private readonly string _loginPath;
    private readonly AuthFederationProvider _descriptor;
    private readonly string _providerRef;

    /// <summary>Creates the provider.</summary>
    /// <param name="descriptor">Provider descriptor rendered by the UI.</param>
    /// <param name="providerRef">Canonical reference, for example <c>clientes/clave</c>.</param>
    /// <param name="loginPath">Host route that starts the federation.</param>
    public ClaveAuthProvider(
        AuthFederationProvider descriptor,
        string providerRef,
        string loginPath = "/clave/login")
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginPath);
        _providerRef = providerRef;
        _loginPath = loginPath;
    }

    /// <inheritdoc />
    public AuthCapabilities Capabilities => new() { Federation = [_descriptor] };

    /// <inheritdoc />
    public AuthSession? CurrentSession => null;

    /// <summary>
    /// Never raised: a server-driven federation has no session in the browser, so there is no
    /// transition to announce here. The host's own authentication state is the source of truth.
    /// </summary>
    public event EventHandler<AuthStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<string> BeginFederationAsync(
        AuthFederationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;

        if (request.Level == AuthAssuranceLevel.Low)
        {
            throw new AuthKitConfigurationException("Cl@ve 2.0 does not offer the 'low' level.");
        }

        if (request.Method is not null && !_descriptor.Methods.Any(m => m.Id == request.Method))
        {
            throw new AuthKitConfigurationException(
                $"Method '{request.Method}' is not declared by provider '{_descriptor.Id}'.");
        }

        var separator = _loginPath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = string.Concat(
            _loginPath,
            separator,
            "loa=", Uri.EscapeDataString(request.Level.ToString().ToLowerInvariant()));

        if (request.Method is not null)
        {
            url = string.Concat(url, "&method=", Uri.EscapeDataString(request.Method));
        }

        if (!string.IsNullOrWhiteSpace(request.ReturnUrl))
        {
            url = string.Concat(url, "&returnUrl=", Uri.EscapeDataString(request.ReturnUrl));
        }

        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public Task<AuthResult> CompleteFederationAsync(
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);
        _ = cancellationToken;

        var query = ParseQuery(callbackUrl);
        if (!query.TryGetValue("error", out var error) || string.IsNullOrWhiteSpace(error))
        {
            return Task.FromResult(new AuthResult { Ok = true, NextStep = AuthNextStep.None });
        }

        var code = ParseCode(error);
        return Task.FromResult(AuthResult.Failure(
            AuthError.From(code, AuthErrorMessageDefaults.For(code))));
    }

    /// <summary>The canonical provider reference this instance was built for.</summary>
    public string ProviderRef => _providerRef;

    /// <inheritdoc />
    public Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> SignUpWithPasswordAsync(string email, string password, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> SendMagicLinkAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> SendPasswordResetAsync(string email, string? redirectTo = null, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> SendEmailOtpAsync(string email, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> VerifyEmailOtpAsync(string email, string token, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> SendPhoneOtpAsync(string phone, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<AuthResult> VerifyPhoneOtpAsync(string phone, string token, CancellationToken cancellationToken = default) =>
        NotAPasswordProvider();

    /// <inheritdoc />
    public Task<string> BeginOAuthAsync(string provider, string? redirectTo = null) =>
        throw new AuthKitConfigurationException(
            "Cl@ve is a server-driven federation. Use BeginFederationAsync, which carries a level and a method.");

    /// <inheritdoc />
    public Task<AuthResult> CompleteOAuthAsync(string callbackUrl, CancellationToken cancellationToken = default) =>
        CompleteFederationAsync(callbackUrl, cancellationToken);

    /// <inheritdoc />
    public Task<AuthResult> RefreshSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthResult { Ok = true, NextStep = AuthNextStep.None });

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static Task<AuthResult> NotAPasswordProvider() =>
        Task.FromResult(AuthResult.Failure(AuthError.From(
            AuthErrorCode.ProviderDisabled,
            AuthErrorMessageDefaults.For(AuthErrorCode.ProviderDisabled))));

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var question = url.IndexOf('?', StringComparison.Ordinal);
        if (question < 0)
        {
            return values;
        }

        var query = url[(question + 1)..];
        var hash = query.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            query = query[..hash];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = separator < 0
                ? string.Empty
                : Uri.UnescapeDataString(pair[(separator + 1)..].Replace("+", " ", StringComparison.Ordinal));
            values[key] = value;
        }

        return values;
    }

    private static AuthErrorCode ParseCode(string value)
    {
        foreach (var pair in AuthContractValues.ErrorCodes)
        {
            if (string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return pair.Key;
            }
        }

        return AuthErrorCode.Unknown;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"ClaveAuthProvider {{ Ref = {_providerRef}, Methods = {_descriptor.Methods.Count} }}");
}
