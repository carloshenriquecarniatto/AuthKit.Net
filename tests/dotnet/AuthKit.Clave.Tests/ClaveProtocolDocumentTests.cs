using System.Security.Cryptography.X509Certificates;
using System.Xml;
using AuthKit.Clave;
using Xunit;

namespace AuthKit.Clave.Tests;

/// <summary>
/// The request the gateway receives, and the metadata CAID registers. A wrong extension or a
/// globally-required attribute causes failures that look like gateway outages, so both documents
/// are asserted structurally.
/// </summary>
public sealed class ClaveProtocolDocumentTests
{
    [Fact]
    public async Task The_authn_request_carries_the_eidas_extensions()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave",
            Level = AuthAssuranceLevel.High,
            Method = ClaveMethods.Certificate,
            ReturnUrl = "/expedientes"
        });

        var document = Load(request.Xml);
        Assert.Equal("AuthnRequest", document.DocumentElement!.LocalName);
        Assert.Equal(ClaveTestEnvironment.AcsUrl, document.DocumentElement.GetAttribute("AssertionConsumerServiceURL"));
        Assert.Equal(ClaveTestEnvironment.Gateway, document.DocumentElement.GetAttribute("Destination"));

        var spType = document.GetElementsByTagName("SPType", "http://eidas.europa.eu/saml-extensions")[0];
        Assert.Equal("public", spType!.InnerText);

        var attributes = document.GetElementsByTagName("RequestedAttribute", "http://eidas.europa.eu/saml-extensions");
        var required = attributes.Cast<XmlElement>()
            .ToDictionary(
                element => element.GetAttribute("Name"),
                element => element.GetAttribute("isRequired"),
                StringComparer.Ordinal);

        Assert.Equal("true", required[ClaveAttributes.PersonIdentifier]);
        Assert.Equal("false", required[ClaveAttributes.DateOfBirth]);
    }

    [Fact]
    public async Task The_authn_request_asks_for_the_requested_level()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave",
            Level = AuthAssuranceLevel.High
        });

        var document = Load(request.Xml);
        var classRef = document.GetElementsByTagName(
            "AuthnContextClassRef",
            "urn:oasis:names:tc:SAML:2.0:assertion")[0];

        Assert.Equal("http://eidas.europa.eu/LoA/high", classRef!.InnerText);

        var context = (XmlElement)classRef.ParentNode!;
        Assert.Equal("minimum", context.GetAttribute("Comparison"));
    }

    [Fact]
    public async Task The_authn_request_is_signed_with_the_entity_certificate()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave"
        });

        var document = Load(request.Xml);
        var signatures = document.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        Assert.Equal(1, signatures.Count);

        var signedXml = new System.Security.Cryptography.Xml.SignedXml(document);
        signedXml.LoadXml((XmlElement)signatures[0]!);
        using var publicKey = env.Signing.GetRSAPublicKey();
        Assert.True(signedXml.CheckSignature(publicKey!));
    }

    [Fact]
    public async Task The_redirect_url_is_a_signed_redirect_binding()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave",
            Level = AuthAssuranceLevel.Substantial
        });

        Assert.StartsWith(ClaveTestEnvironment.Gateway + "?", request.RedirectUrl, StringComparison.Ordinal);
        Assert.Contains("SAMLRequest=", request.RedirectUrl, StringComparison.Ordinal);
        Assert.Contains("RelayState=", request.RedirectUrl, StringComparison.Ordinal);
        Assert.Contains("SigAlg=", request.RedirectUrl, StringComparison.Ordinal);
        Assert.Contains("Signature=", request.RedirectUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceAuthn_is_emitted_when_the_host_asks_for_it()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(
            new AuthFederationRequest { ProviderRef = "clientes/clave" },
            forceAuthn: true);

        var document = Load(request.Xml);
        Assert.Equal("true", document.DocumentElement!.GetAttribute("ForceAuthn"));
    }

    [Fact]
    public async Task The_low_level_is_refused_before_talking_to_the_gateway()
    {
        using var env = new ClaveTestEnvironment();

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() =>
            env.Service.BeginAsync(new AuthFederationRequest
            {
                ProviderRef = "clientes/clave",
                Level = AuthAssuranceLevel.Low
            }));
    }

    [Fact]
    public async Task An_unknown_method_is_refused_before_talking_to_the_gateway()
    {
        using var env = new ClaveTestEnvironment();

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() =>
            env.Service.BeginAsync(new AuthFederationRequest
            {
                ProviderRef = "clientes/clave",
                Method = "sms"
            }));
    }

    [Fact]
    public async Task Every_started_login_is_stored_for_in_response_to_binding()
    {
        using var env = new ClaveTestEnvironment();

        var request = await env.Service.BeginAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave",
            ReturnUrl = "/expedientes"
        });

        var pending = await env.Requests.TakeAsync(request.RequestId);
        Assert.NotNull(pending);
        Assert.Equal(request.State, pending!.State);
        Assert.Equal("/expedientes", pending.ReturnUrl);
        Assert.Equal(ClaveTestEnvironment.Tenant, pending.Tenant);

        // At-most-once: a second take cannot succeed, so an assertion cannot be accepted twice.
        Assert.Null(await env.Requests.TakeAsync(request.RequestId));
    }

    [Fact]
    public void The_metadata_describes_the_registered_entity()
    {
        using var env = new ClaveTestEnvironment();
        env.Options.ProviderName = "Sede Electrónica";

        var metadata = ClaveMetadataBuilder.Build(env.Options);
        var document = Load(metadata);

        Assert.Equal("EntityDescriptor", document.DocumentElement!.LocalName);
        Assert.Equal(ClaveTestEnvironment.EntityId, document.DocumentElement.GetAttribute("entityID"));

        var acs = document.GetElementsByTagName("AssertionConsumerService", "urn:oasis:names:tc:SAML:2.0:metadata")[0];
        Assert.Equal(ClaveTestEnvironment.AcsUrl, ((XmlElement)acs!).GetAttribute("Location"));
        Assert.Equal("urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST", ((XmlElement)acs).GetAttribute("Binding"));

        var certificate = document.GetElementsByTagName("X509Certificate", "http://www.w3.org/2000/09/xmldsig#")[0];
        Assert.Equal(Convert.ToBase64String(env.Signing.RawData), certificate!.InnerText);

        var requested = document.GetElementsByTagName("RequestedAttribute", "urn:oasis:names:tc:SAML:2.0:metadata");
        Assert.Contains(
            requested.Cast<XmlElement>(),
            element => element.GetAttribute("Name") == ClaveAttributes.PersonIdentifier
                && element.GetAttribute("isRequired") == "true");
    }

    [Fact]
    public void Metadata_and_configuration_cannot_drift()
    {
        using var env = new ClaveTestEnvironment();

        var metadata = ClaveMetadataBuilder.Build(env.Options);

        Assert.Contains(ClaveTestEnvironment.EntityId, metadata, StringComparison.Ordinal);
        Assert.Contains(ClaveTestEnvironment.AcsUrl, metadata, StringComparison.Ordinal);
    }

    private static XmlDocument Load(string xml)
    {
        var document = new XmlDocument { XmlResolver = null, PreserveWhitespace = false };
        document.LoadXml(xml);
        return document;
    }
}
