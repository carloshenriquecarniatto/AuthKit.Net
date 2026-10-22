using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using AuthKit.Clave;

namespace AuthKit.Clave.Tests;

/// <summary>Deterministic clock for protocol tests.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset now = default) =>
        Now = now == default ? new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero) : now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Builds the certificates, options and signed assertions the validation tests need. Everything is
/// generated in-process: the suite never touches the network, and no real Cl@ve material is stored.
/// </summary>
internal sealed class ClaveTestEnvironment : IDisposable
{
    public const string EntityId = "https://sede.example.es/clave/metadata";
    public const string AcsUrl = "https://sede.example.es/clave/acs";
    public const string Tenant = "caib";
    public const string Gateway = "https://se-pasarela.clave.gob.es/Proxy2";

    public ClaveTestEnvironment()
    {
        Signing = TestCertificates.Create("CN=sp-signing");
        Pasarela = TestCertificates.Create("CN=pasarela");
        Attacker = TestCertificates.Create("CN=attacker");

        Options = new ClaveOptions
        {
            Environment = ClaveEnvironment.Preproduction,
            EntityId = EntityId,
            AcsUrl = AcsUrl,
            SigningCertificate = Signing,
            PasarelaSigningCertificate = Pasarela,
            DefaultLevel = AuthAssuranceLevel.Substantial,
            Tenant = Tenant,
            SubjectKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()
        };

        Requests = new InMemoryClaveRequestStore(Clock);
        Replay = new InMemoryClaveReplayCache(Clock);
        Validator = new ClaveResponseValidator(Options, Requests, Replay, Clock);
        Service = new ClaveLoginService(Options, Requests, Validator, Clock);
    }

    public FixedTimeProvider Clock { get; } = new();

    public X509Certificate2 Signing { get; }

    public X509Certificate2 Pasarela { get; }

    public X509Certificate2 Attacker { get; }

    public ClaveOptions Options { get; }

    public InMemoryClaveRequestStore Requests { get; }

    public InMemoryClaveReplayCache Replay { get; }

    public ClaveResponseValidator Validator { get; }

    public ClaveLoginService Service { get; }

    public PendingClaveRequest Pending(
        string? requestId = null,
        AuthAssuranceLevel level = AuthAssuranceLevel.Substantial,
        string? method = "clave_movil",
        string? returnUrl = "/expedientes",
        DateTimeOffset? expiresAt = null) => new()
        {
            RequestId = requestId ?? "_req" + Guid.NewGuid().ToString("N"),
            State = "state-" + Guid.NewGuid().ToString("N"),
            ReturnUrl = returnUrl,
            Level = level,
            Method = method,
            Tenant = Tenant,
            CreatedAt = Clock.Now,
            ExpiresAt = expiresAt ?? Clock.Now.AddMinutes(15)
        };

    public SignedAssertion Sign(
        PendingClaveRequest pending,
        X509Certificate2? signingCertificate = null,
        string? audience = null,
        string? destination = null,
        string? issuer = null,
        string? documentId = "12345678Z",
        AuthAssuranceLevel granted = AuthAssuranceLevel.Substantial,
        string? assertionId = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notOnOrAfter = null,
        string? statusCode = null)
    {
        var now = Clock.Now;
        var xml = ClaveFixture.Build(
            pending,
            audience ?? EntityId,
            destination ?? AcsUrl,
            issuer ?? Gateway,
            documentId,
            AuthAssuranceLevels.ToAcrValue(granted),
            assertionId ?? "_a" + Guid.NewGuid().ToString("N"),
            notBefore ?? now.AddMinutes(-1),
            notOnOrAfter ?? now.AddMinutes(5),
            statusCode);

        return new SignedAssertion(
            ClaveFixture.Sign(xml, signingCertificate ?? Pasarela),
            pending.State);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Signing.Dispose();
        Pasarela.Dispose();
        Attacker.Dispose();
    }
}

/// <summary>A signed response ready to be posted to the ACS.</summary>
internal sealed record SignedAssertion(string Base64, string RelayState);

internal static class TestCertificates
{
    public static X509Certificate2 Create(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: false));

        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), password: null);
    }
}

/// <summary>
/// Loads the committed, anonymised protocol fixture and signs it. The XML body is a fixture; the
/// signature is generated per test so the suite can exercise tampering without storing a private key.
/// </summary>
internal static class ClaveFixture
{
    private static readonly string Template = LoadTemplate();

    public static string Build(
        PendingClaveRequest pending,
        string audience,
        string destination,
        string issuer,
        string? documentId,
        string acr,
        string assertionId,
        DateTimeOffset notBefore,
        DateTimeOffset notOnOrAfter,
        string? statusCode)
    {
        var status = statusCode ?? "urn:oasis:names:tc:SAML:2.0:status:Success";
        var responseStatus = statusCode is null
            ? "<samlp:Status><samlp:StatusCode Value=\"urn:oasis:names:tc:SAML:2.0:status:Success\" /></samlp:Status>"
            : string.Concat(
                "<samlp:Status><samlp:StatusCode Value=\"urn:oasis:names:tc:SAML:2.0:status:Responder\">",
                "<samlp:StatusCode Value=\"", Escape(status), "\" /></samlp:StatusCode></samlp:Status>");

        var personIdentifier = documentId is null
            ? string.Empty
            : string.Concat(
                "<saml:Attribute Name=\"http://eidas.europa.eu/attributes/naturalperson/PersonIdentifier\">",
                "<saml:AttributeValue>", Escape(documentId), "</saml:AttributeValue></saml:Attribute>");

        return Template
            .Replace("${RESPONSE_ID}", "_r" + Guid.NewGuid().ToString("N"), StringComparison.Ordinal)
            .Replace("${ISSUE_INSTANT}", Instant(notBefore), StringComparison.Ordinal)
            .Replace("${DESTINATION}", Escape(destination), StringComparison.Ordinal)
            .Replace("${REQUEST_ID}", Escape(pending.RequestId), StringComparison.Ordinal)
            .Replace("${ISSUER}", Escape(issuer), StringComparison.Ordinal)
            .Replace("${STATUS}", responseStatus, StringComparison.Ordinal)
            .Replace("${ASSERTION_ID}", Escape(assertionId), StringComparison.Ordinal)
            .Replace("${NOT_BEFORE}", Instant(notBefore), StringComparison.Ordinal)
            .Replace("${NOT_ON_OR_AFTER}", Instant(notOnOrAfter), StringComparison.Ordinal)
            .Replace("${AUDIENCE}", Escape(audience), StringComparison.Ordinal)
            .Replace("${RECIPIENT}", Escape(destination), StringComparison.Ordinal)
            .Replace("${IN_RESPONSE_TO}", Escape(pending.RequestId), StringComparison.Ordinal)
            .Replace("${AUTHN_INSTANT}", Instant(notBefore), StringComparison.Ordinal)
            .Replace("${ACR}", Escape(acr), StringComparison.Ordinal)
            .Replace("${PERSON_IDENTIFIER}", personIdentifier, StringComparison.Ordinal);
    }

    public static string Sign(string xml, X509Certificate2 certificate)
    {
        var document = new XmlDocument { PreserveWhitespace = false, XmlResolver = null };
        document.LoadXml(xml);

        var assertion = document.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]
            ?? throw new InvalidOperationException("The fixture has no Assertion element.");
        var assertionId = assertion.Attributes?["ID"]?.Value
            ?? throw new InvalidOperationException("The fixture assertion has no ID.");

        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The test certificate has no private key.");

        var signedXml = new SignedXml(document) { SigningKey = rsa };
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("#" + assertionId) { DigestMethod = SignedXml.XmlDsigSHA256Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);
        signedXml.KeyInfo = new KeyInfo();
        signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate));
        signedXml.ComputeSignature();

        var signature = document.ImportNode(signedXml.GetXml(), true);
        var issuer = assertion.SelectSingleNode("*[local-name()='Issuer']")!;
        assertion.InsertAfter(signature, issuer);

        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(document.OuterXml));
    }

    private static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    private static string LoadTemplate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "messages", "clave", "response-template.xml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("samples/messages/clave/response-template.xml was not found.");
    }
}
