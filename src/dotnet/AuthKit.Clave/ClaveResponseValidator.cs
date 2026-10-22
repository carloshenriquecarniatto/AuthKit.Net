using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace AuthKit.Clave;

/// <summary>
/// Outcome of validating an assertion. Failures carry an internal reason for logs and a generic
/// outward error: telling a remote user which check failed tells an attacker where to aim.
/// </summary>
public sealed record ClaveValidationResult
{
    /// <summary>Whether the response is acceptable.</summary>
    public required bool Ok { get; init; }

    /// <summary>Validated identity, when <see cref="Ok"/>.</summary>
    public ClaveIdentity? Identity { get; init; }

    /// <summary>Internal reason, for logs and metrics only.</summary>
    public ClaveFailureReason Reason { get; init; } = ClaveFailureReason.None;

    /// <summary>Second-level status returned by the gateway, when it failed on purpose.</summary>
    public string? StatusCode { get; init; }

    /// <summary>A successful result.</summary>
    /// <param name="identity">Validated identity.</param>
    /// <returns>The result.</returns>
    public static ClaveValidationResult Success(ClaveIdentity identity) => new() { Ok = true, Identity = identity };

    /// <summary>A rejected result.</summary>
    /// <param name="reason">Internal reason.</param>
    /// <param name="statusCode">Second-level gateway status, when known.</param>
    /// <returns>The result.</returns>
    public static ClaveValidationResult Fail(ClaveFailureReason reason, string? statusCode = null) =>
        new() { Ok = false, Reason = reason, StatusCode = statusCode };
}

/// <summary>
/// Validates a Cl@ve response, in the order defined by docs/spec-clave.md §5.5. Every check is
/// fail-closed: an absent value is a rejection, never a default.
/// </summary>
public sealed class ClaveResponseValidator
{
    private const string SamlProtocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string SamlAssertion = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string Ds = "http://www.w3.org/2000/09/xmldsig#";
    private const string SuccessStatus = "urn:oasis:names:tc:SAML:2.0:status:Success";
    private const string MethodAttribute = "http://eidas.europa.eu/attributes/naturalperson/AuthnMethod";
    private const string TenantAttribute = "urn:authkit:attribute:tenant";

    private readonly ClaveOptions _options;
    private readonly IClaveRequestStore _requests;
    private readonly IClaveReplayCache _replay;
    private readonly TimeProvider _clock;

    /// <summary>Creates a validator.</summary>
    /// <param name="options">Validated configuration.</param>
    /// <param name="requests">Pending request store.</param>
    /// <param name="replay">Replay cache.</param>
    /// <param name="timeProvider">Clock, for tests.</param>
    public ClaveResponseValidator(
        ClaveOptions options,
        IClaveRequestStore requests,
        IClaveReplayCache replay,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Validates a base64 <c>SAMLResponse</c> posted to the ACS.</summary>
    /// <param name="samlResponse">Base64 value of the form field.</param>
    /// <param name="relayState">Value of the <c>RelayState</c> form field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity, or the internal reason it was rejected.</returns>
    public async ValueTask<ClaveValidationResult> ValidateAsync(
        string? samlResponse,
        string? relayState,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(samlResponse))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.MalformedResponse);
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(samlResponse);
        }
        catch (FormatException)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.MalformedResponse);
        }

        if (payload.Length == 0 || payload.Length > _options.MaxResponseBytes)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.MalformedResponse);
        }

        XmlDocument document;
        try
        {
            document = Load(payload, _options.MaxResponseBytes);
        }
        catch (XmlException)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.UnsafeXml);
        }

        var response = document.DocumentElement;
        if (response is null
            || !IsElement(response, "Response", SamlProtocol))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.MalformedResponse);
        }

        var assertions = response.GetElementsByTagName("Assertion", SamlAssertion);
        if (assertions.Count != 1 || assertions.Item(0) is not XmlElement assertion)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.MalformedResponse);
        }

        // Signature first: nothing read from the document is trustworthy before it validates.
        if (!VerifySignature(response, assertion, out var signatureFailure))
        {
            return ClaveValidationResult.Fail(signatureFailure);
        }

        var expectedIssuer = ExpectedIssuer();
        var responseIssuer = IssuerOf(response);
        var assertionIssuer = IssuerOf(assertion);
        if (!string.Equals(TrimSlash(responseIssuer), expectedIssuer, StringComparison.Ordinal)
            || !string.Equals(TrimSlash(assertionIssuer), expectedIssuer, StringComparison.Ordinal))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.IssuerMismatch);
        }

        var (topStatus, secondLevelStatus) = StatusCodes(response);
        if (!string.Equals(topStatus, SuccessStatus, StringComparison.Ordinal))
        {
            return ClaveValidationResult.Fail(
                ClaveFailureReason.StatusNotSuccess,
                secondLevelStatus ?? topStatus);
        }

        var destination = response.GetAttribute("Destination");
        if (!string.Equals(TrimSlash(destination), TrimSlash(_options.AcsUrl), StringComparison.Ordinal))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.DestinationMismatch);
        }

        var inResponseTo = response.GetAttribute("InResponseTo");
        if (string.IsNullOrWhiteSpace(inResponseTo))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.RequestUnknown);
        }

        var pending = await _requests.TakeAsync(inResponseTo, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.RequestUnknown);
        }

        var now = _clock.GetUtcNow();
        if (pending.ExpiresAt <= now)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.RequestExpired);
        }

        if (!SameState(pending.State, relayState))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.RelayStateMismatch);
        }

        if (!AudienceContainsEntity(assertion))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.AudienceMismatch);
        }

        if (!TimeWindowIsAcceptable(assertion, now))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.AssertionExpired);
        }

        if (!SubjectConfirmationIsAcceptable(assertion, pending, now))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.RequestUnknown);
        }

        var notOnOrAfter = AssertionNotOnOrAfter(assertion) ?? now.AddMinutes(5);
        var assertionId = assertion.GetAttribute("ID");
        if (string.IsNullOrWhiteSpace(assertionId)
            || !await _replay.TryRegisterAsync(assertionId, notOnOrAfter, cancellationToken).ConfigureAwait(false))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.ReplayDetected);
        }

        var personIdentifier = AttributeValue(assertion, ClaveAttributes.PersonIdentifier);
        if (string.IsNullOrWhiteSpace(personIdentifier)
            || !IdentityDocumentParser.TryParse(personIdentifier, out var documentValue, _options.RequireValidDocumentChecksum))
        {
            return ClaveValidationResult.Fail(
                string.IsNullOrWhiteSpace(personIdentifier)
                    ? ClaveFailureReason.SubjectMissing
                    : ClaveFailureReason.DocumentInvalid);
        }

        var assertedTenant = AttributeValue(assertion, TenantAttribute);
        if (!string.IsNullOrWhiteSpace(assertedTenant)
            && !string.Equals(assertedTenant, _options.Tenant, StringComparison.Ordinal))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.TenantMismatch);
        }

        var acr = AuthnContextClassRef(assertion);
        if (!AuthAssuranceLevels.TryParseAcrValue(acr, out var granted))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.LevelBelowRequested);
        }

        if (!AuthAssuranceLevels.Satisfies(granted, pending.Level))
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.LevelBelowRequested);
        }

        if (_options.SubjectKey is null || _options.Tenant is null)
        {
            return ClaveValidationResult.Fail(ClaveFailureReason.StateUnavailable);
        }

        var method = pending.Method
            ?? AttributeValue(assertion, MethodAttribute)
            ?? "clave";

        var identity = new ClaveIdentity
        {
            Subject = ClaveIdentity.DeriveSubject(documentValue.Value, _options.Tenant, _options.SubjectKey),
            DocumentType = documentValue.Type,
            DocumentId = documentValue.Value,
            DocumentIdRaw = documentValue.Raw,
            GivenName = AttributeValue(assertion, ClaveAttributes.GivenName),
            FamilyName = AttributeValue(assertion, ClaveAttributes.FamilyName),
            DateOfBirth = ParseDate(AttributeValue(assertion, ClaveAttributes.DateOfBirth)),
            Level = granted,
            Method = method,
            Tenant = _options.Tenant,
            RepresentedDocumentId = AttributeValue(assertion, "urn:authkit:attribute:represented"),
            AuthenticatedAt = AuthnInstant(assertion) ?? now,
            ReturnUrl = pending.ReturnUrl
        };

        return ClaveValidationResult.Success(identity);
    }

    private static XmlDocument Load(byte[] payload, int maxCharacters)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true
        };

        var document = new XmlDocument { XmlResolver = null, PreserveWhitespace = false };
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        document.Load(reader);
        return document;
    }

    private bool VerifySignature(XmlElement response, XmlElement assertion, out ClaveFailureReason reason)
    {
        reason = ClaveFailureReason.SignatureInvalid;

        var certificate = _options.PasarelaSigningCertificate;
        if (certificate is null)
        {
            reason = ClaveFailureReason.CertificateUnknown;
            return false;
        }

        using var publicKey = certificate.GetRSAPublicKey();
        if (publicKey is null)
        {
            reason = ClaveFailureReason.CertificateUnknown;
            return false;
        }

        var signature = DirectChild(assertion, "Signature", Ds) ?? DirectChild(response, "Signature", Ds);
        if (signature is null)
        {
            return false;
        }

        var signedElement = ReferenceEquals(signature.ParentNode, assertion) ? assertion : response;
        var signedId = signedElement.GetAttribute("ID");
        if (string.IsNullOrWhiteSpace(signedId))
        {
            return false;
        }

        SignedXml signedXml;
        try
        {
            signedXml = new SignedXml(document: signedElement.OwnerDocument!);
            signedXml.LoadXml(signature);
        }
        catch (CryptographicException)
        {
            return false;
        }

        // A signature must cover exactly the element whose values we read. Anything else is the
        // classic wrapping attack: a valid signature over an attacker-chosen fragment.
        if (signedXml.SignedInfo!.References.Count != 1
            || signedXml.SignedInfo!.References[0] is not Reference reference
            || !string.Equals(reference.Uri, "#" + signedId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!signedXml.CheckSignature(publicKey))
        {
            return false;
        }

        return true;
    }

    private string ExpectedIssuer() =>
        (_options.ExpectedIssuer ?? (_options.GatewayEndpoint ?? ClaveEndpoints.For(_options.Environment)).AbsoluteUri)
            .TrimEnd('/');

    private bool AudienceContainsEntity(XmlElement assertion)
    {
        var audiences = assertion.GetElementsByTagName("Audience", SamlAssertion);
        for (var i = 0; i < audiences.Count; i++)
        {
            if (string.Equals(TrimSlash(audiences.Item(i)?.InnerText), TrimSlash(_options.EntityId), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool TimeWindowIsAcceptable(XmlElement assertion, DateTimeOffset now)
    {
        var skew = _options.ClockSkew;
        var conditions = DirectChild(assertion, "Conditions", SamlAssertion);
        if (conditions is not null)
        {
            var notBefore = ParseInstant(conditions.GetAttribute("NotBefore"));
            if (notBefore is not null && now + skew < notBefore)
            {
                return false;
            }

            var notOnOrAfter = ParseInstant(conditions.GetAttribute("NotOnOrAfter"));
            if (notOnOrAfter is not null && now - skew >= notOnOrAfter)
            {
                return false;
            }
        }

        var statement = DirectChild(assertion, "AuthnStatement", SamlAssertion);
        if (statement is not null)
        {
            var sessionNotOnOrAfter = ParseInstant(statement.GetAttribute("SessionNotOnOrAfter"));
            if (sessionNotOnOrAfter is not null && now - skew >= sessionNotOnOrAfter)
            {
                return false;
            }
        }

        return true;
    }

    private bool SubjectConfirmationIsAcceptable(XmlElement assertion, PendingClaveRequest pending, DateTimeOffset now)
    {
        var subject = DirectChild(assertion, "Subject", SamlAssertion);
        if (subject is null)
        {
            return false;
        }

        var confirmations = subject.GetElementsByTagName("SubjectConfirmationData", SamlAssertion);
        if (confirmations.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < confirmations.Count; i++)
        {
            if (confirmations.Item(i) is not XmlElement data)
            {
                continue;
            }

            var recipient = data.GetAttribute("Recipient");
            if (recipient.Length > 0
                && !string.Equals(TrimSlash(recipient), TrimSlash(_options.AcsUrl), StringComparison.Ordinal))
            {
                continue;
            }

            var inResponseTo = data.GetAttribute("InResponseTo");
            if (inResponseTo.Length > 0
                && !string.Equals(inResponseTo, pending.RequestId, StringComparison.Ordinal))
            {
                continue;
            }

            var notOnOrAfter = ParseInstant(data.GetAttribute("NotOnOrAfter"));
            if (notOnOrAfter is not null && now - _options.ClockSkew >= notOnOrAfter)
            {
                continue;
            }

            var notBefore = ParseInstant(data.GetAttribute("NotBefore"));
            if (notBefore is not null && now + _options.ClockSkew < notBefore)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static DateTimeOffset? AssertionNotOnOrAfter(XmlElement assertion) =>
        ParseInstant(DirectChild(assertion, "Conditions", SamlAssertion)?.GetAttribute("NotOnOrAfter"));

    private static string? IssuerOf(XmlElement element) =>
        DirectChild(element, "Issuer", SamlAssertion)?.InnerText;

    private static (string? Top, string? Second) StatusCodes(XmlElement response)
    {
        var status = DirectChild(response, "Status", SamlProtocol);
        var code = status is null ? null : DirectChild(status, "StatusCode", SamlProtocol);
        var second = code is null ? null : DirectChild(code, "StatusCode", SamlProtocol);
        return (code?.GetAttribute("Value"), second?.GetAttribute("Value"));
    }

    private static string? AuthnContextClassRef(XmlElement assertion)
    {
        var statement = DirectChild(assertion, "AuthnStatement", SamlAssertion);
        var context = statement is null ? null : DirectChild(statement, "AuthnContext", SamlAssertion);
        return context is null ? null : DirectChild(context, "AuthnContextClassRef", SamlAssertion)?.InnerText;
    }

    private static DateTimeOffset? AuthnInstant(XmlElement assertion) =>
        ParseInstant(DirectChild(assertion, "AuthnStatement", SamlAssertion)?.GetAttribute("AuthnInstant"));

    private static string? AttributeValue(XmlElement assertion, string name)
    {
        var statements = assertion.GetElementsByTagName("AttributeStatement", SamlAssertion);
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements.Item(i) is not XmlElement statement)
            {
                continue;
            }

            foreach (var node in statement.ChildNodes)
            {
                if (node is XmlElement attribute
                    && IsElement(attribute, "Attribute", SamlAssertion)
                    && string.Equals(attribute.GetAttribute("Name"), name, StringComparison.Ordinal))
                {
                    var value = DirectChild(attribute, "AttributeValue", SamlAssertion);
                    return value?.InnerText;
                }
            }
        }

        return null;
    }

    private static XmlElement? DirectChild(XmlElement parent, string localName, string namespaceUri)
    {
        foreach (var node in parent.ChildNodes)
        {
            if (node is XmlElement element && IsElement(element, localName, namespaceUri))
            {
                return element;
            }
        }

        return null;
    }

    private static bool IsElement(XmlElement element, string localName, string namespaceUri) =>
        string.Equals(element.LocalName, localName, StringComparison.Ordinal)
        && string.Equals(element.NamespaceURI, namespaceUri, StringComparison.Ordinal);

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static bool SameState(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(provided);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? TrimSlash(string? value) => value?.TrimEnd('/');
}
