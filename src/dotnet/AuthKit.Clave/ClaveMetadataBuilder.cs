using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace AuthKit.Clave;

/// <summary>
/// Generates the SP metadata XML the entity hands to CAID. The metadata is derived from
/// <see cref="ClaveOptions"/>, so it cannot drift from the configuration the gateway will validate.
/// </summary>
public static class ClaveMetadataBuilder
{
    private const string Metadata = "urn:oasis:names:tc:SAML:2.0:metadata";
    private const string Assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string Protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string PostBinding = "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST";
    private const string Ds = "http://www.w3.org/2000/09/xmldsig#";

    /// <summary>Builds the metadata document.</summary>
    /// <param name="options">Validated configuration.</param>
    /// <returns>UTF-8 XML, ready to be served with <c>application/samlmetadata+xml</c>.</returns>
    public static string Build(ClaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.EntityId) || string.IsNullOrWhiteSpace(options.AcsUrl))
        {
            throw new AuthKitConfigurationException("Metadata needs both EntityId and AcsUrl.");
        }

        var document = new XmlDocument();
        var declaration = document.CreateXmlDeclaration("1.0", "UTF-8", null);
        document.AppendChild(declaration);

        var descriptor = document.CreateElement("md", "EntityDescriptor", Metadata);
        descriptor.SetAttribute("entityID", options.EntityId);
        descriptor.SetAttribute("ID", "_" + Guid.NewGuid().ToString("N"));
        document.AppendChild(descriptor);

        var sp = document.CreateElement("md", "SPSSODescriptor", Metadata);
        sp.SetAttribute("AuthnRequestsSigned", "true");
        sp.SetAttribute("WantAssertionsSigned", "true");
        sp.SetAttribute("protocolSupportEnumeration", Protocol);
        descriptor.AppendChild(sp);

        if (options.SigningCertificate is not null)
        {
            sp.AppendChild(BuildKeyDescriptor(document, options.SigningCertificate));
        }

        var nameIdFormat = document.CreateElement("md", "NameIDFormat", Metadata);
        nameIdFormat.InnerText = "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent";
        sp.AppendChild(nameIdFormat);

        var acs = document.CreateElement("md", "AssertionConsumerService", Metadata);
        acs.SetAttribute("Binding", PostBinding);
        acs.SetAttribute("Location", options.AcsUrl);
        acs.SetAttribute("index", "0");
        acs.SetAttribute("isDefault", "true");
        sp.AppendChild(acs);

        var consuming = document.CreateElement("md", "AttributeConsumingService", Metadata);
        consuming.SetAttribute("index", "0");
        var serviceName = document.CreateElement("md", "ServiceName", Metadata);
        serviceName.SetAttribute("xml", "lang", "es");
        serviceName.InnerText = options.ProviderName ?? options.Tenant ?? options.EntityId;
        consuming.AppendChild(serviceName);

        foreach (var attribute in options.RequestedAttributes)
        {
            var requested = document.CreateElement("md", "RequestedAttribute", Metadata);
            requested.SetAttribute("Name", attribute);
            requested.SetAttribute("NameFormat", "urn:oasis:names:tc:SAML:2.0:attrname-format:uri");
            if (options.RequiredAttributes.Contains(attribute, StringComparer.Ordinal))
            {
                requested.SetAttribute("isRequired", "true");
            }

            consuming.AppendChild(requested);
        }

        sp.AppendChild(consuming);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        };

        using var writer = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(writer, settings))
        {
            document.Save(xmlWriter);
        }

        return writer.ToString();
    }

    /// <summary>Mime type the metadata endpoint should return.</summary>
    public const string ContentType = "application/samlmetadata+xml";

    private static XmlElement BuildKeyDescriptor(XmlDocument document, X509Certificate2 certificate)
    {
        var keyDescriptor = document.CreateElement("md", "KeyDescriptor", Metadata);
        keyDescriptor.SetAttribute("use", "signing");

        var keyInfo = document.CreateElement("ds", "KeyInfo", Ds);
        var x509Data = document.CreateElement("ds", "X509Data", Ds);
        var x509Certificate = document.CreateElement("ds", "X509Certificate", Ds);
        x509Certificate.InnerText = Convert.ToBase64String(certificate.RawData);

        x509Data.AppendChild(x509Certificate);
        keyInfo.AppendChild(x509Data);
        keyDescriptor.AppendChild(keyInfo);
        return keyDescriptor;
    }
}
