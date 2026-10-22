using System.Security.Cryptography;

namespace AuthKit.Clave;

/// <summary>
/// The server-side flow: create a pending request, build the signed <c>AuthnRequest</c>, then
/// validate the response that comes back. It never signs anyone in and never mints a token.
/// </summary>
public sealed class ClaveLoginService
{
    private readonly ClaveOptions _options;
    private readonly IClaveRequestStore _requests;
    private readonly ClaveResponseValidator _validator;
    private readonly TimeProvider _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="options">Validated configuration.</param>
    /// <param name="requests">Pending request store.</param>
    /// <param name="validator">Response validator.</param>
    /// <param name="timeProvider">Clock, for tests.</param>
    public ClaveLoginService(
        ClaveOptions options,
        IClaveRequestStore requests,
        ClaveResponseValidator validator,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Starts a login.</summary>
    /// <param name="request">Requested level, method and return URL.</param>
    /// <param name="forceAuthn">Whether the gateway must re-authenticate the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed request and the URL the host should navigate to.</returns>
    public async Task<ClaveAuthnRequest> BeginAsync(
        AuthFederationRequest request,
        bool forceAuthn = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Level == AuthAssuranceLevel.Low)
        {
            throw new AuthKitConfigurationException("Cl@ve 2.0 does not offer the 'low' level.");
        }

        if (request.Method is not null && !ClaveMethods.All.Contains(request.Method, StringComparer.Ordinal))
        {
            throw new AuthKitConfigurationException($"Unknown Cl@ve method '{request.Method}'.");
        }

        var now = _clock.GetUtcNow();
        var pending = new PendingClaveRequest
        {
            RequestId = "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant(),
            State = Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
            ReturnUrl = request.ReturnUrl,
            Level = request.Level,
            Method = request.Method,
            Tenant = _options.Tenant!,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.RequestLifetime)
        };

        // Build first: a configuration failure must not leave an orphan pending request behind.
        var authnRequest = ClaveAuthnRequestBuilder.Build(_options, pending, forceAuthn, _clock);
        await _requests.SaveAsync(pending, cancellationToken).ConfigureAwait(false);
        return authnRequest;
    }

    /// <summary>Validates the response posted to the ACS.</summary>
    /// <param name="samlResponse">Base64 <c>SAMLResponse</c>.</param>
    /// <param name="relayState"><c>RelayState</c> returned by the gateway.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated identity, or the internal reason it was rejected.</returns>
    public ValueTask<ClaveValidationResult> CompleteAsync(
        string? samlResponse,
        string? relayState,
        CancellationToken cancellationToken = default) =>
        _validator.ValidateAsync(samlResponse, relayState, cancellationToken);

    /// <summary>Builds the SP metadata for CAID.</summary>
    /// <returns>Metadata XML.</returns>
    public string BuildMetadata() => ClaveMetadataBuilder.Build(_options);

    internal static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
