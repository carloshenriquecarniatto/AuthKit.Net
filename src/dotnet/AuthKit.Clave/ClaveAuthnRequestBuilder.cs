using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace AuthKit.Clave;

/// <summary>
/// A signed <c>AuthnRequest</c> ready to be sent to the gateway.
/// </summary>
/// <param name="RequestId">SAML request id, also the <c>InResponseTo</c> value expected back.</param>
/// <param name="State">Opaque state echoed by the gateway as <c>RelayState</c>.</param>
/// <param name="Xml">The signed request, for diagnostics and for POST binding.</param>
/// <param name="RedirectUrl">
/// HTTP-Redirect URL including <c>SAMLRequest</c>, <c>RelayState</c>, <c>SigAlg</c> and
/// <c>Signature</c>. The host decides whether to navigate to it.
/// </param>
public sealed record ClaveAuthnRequest(string RequestId, string State, string Xml, string RedirectUrl);

/// <summary>
/// Builds the eIDAS <c>AuthnRequest</c>. The gateway is unforgiving about this document: a missing
/// eIDAS extension or a per-attribute <c>isRequired</c> is rejected with an opaque error.
/// </summary>
public static class ClaveAuthnRequestBuilder
{
    private const string SamlProtocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string SamlAssertion = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string Eidas = "http://eidas.europa.eu/saml-extensions";
    private const string PostBinding = "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST";
    private const string RedirectSigningAlgorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

    /// <summary>Builds and signs a request.</summary>
    /// <param name="options">Validated configuration.</param>
    /// <param name="request">Pending request created by the host, carrying level, method and state.</param>
    /// <param name="forceAuthn">Whether the gateway must re-authenticate the user.</param>
    /// <param name="timeProvider">Clock, for tests.</param>
    /// <returns>The signed request and the redirect URL.</returns>
    /// <exception cref="AuthKitConfigurationException">The configuration cannot produce a request.</exception>
    public static ClaveAuthnRequest Build(
        ClaveOptions options,
        PendingClaveRequest request,
        bool forceAuthn = false,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        if (options.SigningCertificate is null || !options.SigningCertificate.HasPrivateKey)
        {
            throw new AuthKitConfigurationException("Cl@ve needs a signing certificate with a private key.");
        }

        var clock = timeProvider ?? TimeProvider.System;
        var document = new XmlDocument { PreserveWhitespace = false };
        var declaration = document.CreateXmlDeclaration("1.0", "UTF-8", null);
        document.AppendChild(declaration);

        var authnRequest = document.CreateElement("samlp", "AuthnRequest", SamlProtocol);
        authnRequest.SetAttribute("ID", request.RequestId);
        authnRequest.SetAttribute("Version", "2.0");
        authnRequest.SetAttribute("IssueInstant", clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
        authnRequest.SetAttribute("Destination", GatewayDestination(options));
        authnRequest.SetAttribute("AssertionConsumerServiceURL", options.AcsUrl);
        authnRequest.SetAttribute("ProtocolBinding", PostBinding);
        authnRequest.SetAttribute("IsPassive", "false");
        if (!string.IsNullOrWhiteSpace(options.ProviderName))
        {
            authnRequest.SetAttribute("ProviderName", options.ProviderName);
        }

        if (forceAuthn || options.ForceAuthn)
        {
            authnRequest.SetAttribute("ForceAuthn", "true");
        }

        document.AppendChild(authnRequest);

        var issuer = document.CreateElement("saml", "Issuer", SamlAssertion);
        issuer.InnerText = options.EntityId!;
        authnRequest.AppendChild(issuer);

        authnRequest.AppendChild(BuildExtensions(document, options));
        authnRequest.AppendChild(BuildNameIdPolicy(document));
        authnRequest.AppendChild(BuildAuthnContext(document, request));

        Sign(document, options.SigningCertificate);

        var xml = document.OuterXml;
        return new ClaveAuthnRequest(
            request.RequestId,
            request.State,
            xml,
            BuildRedirectUrl(options, xml, request.State));
    }

    private static XmlElement BuildExtensions(XmlDocument document, ClaveOptions options)
    {
        var extensions = document.CreateElement("samlp", "Extensions", SamlProtocol);

        var spType = document.CreateElement("eidas", "SPType", Eidas);
        spType.InnerText = options.ServiceProviderType == ClaveServiceProviderType.Public ? "public" : "private";
        extensions.AppendChild(spType);

        var requested = document.CreateElement("eidas", "RequestedAttributes", Eidas);
        foreach (var attribute in options.RequestedAttributes)
        {
            var element = document.CreateElement("eidas", "RequestedAttribute", Eidas);
            element.SetAttribute("Name", attribute);
            element.SetAttribute("isRequired", options.RequiredAttributes.Contains(attribute, StringComparer.Ordinal) ? "true" : "false");
            requested.AppendChild(element);
        }

        extensions.AppendChild(requested);
        return extensions;
    }

    private static XmlElement BuildNameIdPolicy(XmlDocument document)
    {
        var policy = document.CreateElement("samlp", "NameIDPolicy", SamlProtocol);
        policy.SetAttribute("Format", "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent");
        policy.SetAttribute("AllowCreate", "true");
        return policy;
    }

    private static XmlElement BuildAuthnContext(XmlDocument document, PendingClaveRequest request)
    {
        var context = document.CreateElement("samlp", "RequestedAuthnContext", SamlProtocol);
        context.SetAttribute("Comparison", "minimum");

        var classRef = document.CreateElement("saml", "AuthnContextClassRef", SamlAssertion);
        classRef.InnerText = AuthAssuranceLevels.ToAcrValue(request.Level);
        context.AppendChild(classRef);
        return context;
    }

    private static void Sign(XmlDocument document, X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new AuthKitConfigurationException("The Cl@ve signing certificate must have an RSA private key.");

        var signedXml = new SignedXml(document)
        {
            SigningKey = rsa
        };
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference(string.Empty)
        {
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);
        signedXml.KeyInfo = new KeyInfo();
        signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate));

        signedXml.ComputeSignature();

        var signature = document.ImportNode(signedXml.GetXml(), true);
        var root = document.DocumentElement!;
        var issuer = root.GetElementsByTagName("Issuer", SamlAssertion).Item(0);
        if (issuer is null)
        {
            root.AppendChild(signature);
        }
        else
        {
            root.InsertAfter(signature, issuer);
        }
    }

    private static string GatewayDestination(ClaveOptions options) =>
        (options.GatewayEndpoint ?? ClaveEndpoints.For(options.Environment)).AbsoluteUri.TrimEnd('/');

    private static string BuildRedirectUrl(ClaveOptions options, string xml, string state)
    {
        var encoded = Deflate(xml);
        var samlRequest = Uri.EscapeDataString(encoded);

        // The signature covers the exact query string, in this order, before the Signature
        // parameter is appended. Reordering the parameters invalidates it.
        var payload = string.Concat(
            "SAMLRequest=", samlRequest,
            "&RelayState=", Uri.EscapeDataString(state),
            "&SigAlg=", Uri.EscapeDataString(RedirectSigningAlgorithm));

        using var rsa = options.SigningCertificate!.GetRSAPrivateKey()
            ?? throw new AuthKitConfigurationException("The Cl@ve signing certificate must have an RSA private key.");
        var signature = Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        var destination = (options.GatewayEndpoint ?? ClaveEndpoints.For(options.Environment)).AbsoluteUri.TrimEnd('/');
        return string.Concat(destination, "?", payload, "&Signature=", Uri.EscapeDataString(signature));
    }

    private static string Deflate(string value)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            deflate.Write(bytes, 0, bytes.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }
}
