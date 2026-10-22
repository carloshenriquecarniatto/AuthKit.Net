using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace AuthKit.Clave.Tests;

public sealed class ClaveOptionsValidatorTests : IDisposable
{
    private static readonly byte[] ValidSubjectKey = new byte[32];

    private readonly X509Certificate2 _signing = CertificateFactory.WithPrivateKey();
    private readonly X509Certificate2 _pasarela = CertificateFactory.PublicOnly();

    /// <inheritdoc />
    public void Dispose()
    {
        _signing.Dispose();
        _pasarela.Dispose();
    }

    [Fact]
    public void Accepts_a_complete_configuration()
    {
        var errors = ClaveOptionsValidator.Validate(Valid());

        Assert.Empty(errors);
    }

    [Fact]
    public void Requires_an_entity_id()
    {
        var options = Valid();
        options.EntityId = null;

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("EntityId is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_non_https_entity_id()
    {
        var options = Valid();
        options.EntityId = "http://sede.example.es/clave/metadata";

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("absolute https URL", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_entity_id_with_a_query_string()
    {
        var options = Valid();
        options.EntityId = "https://sede.example.es/clave/metadata?x=1";

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("query string", StringComparison.Ordinal));
    }

    [Fact]
    public void Requires_a_signing_certificate_with_a_private_key()
    {
        var options = Valid();
        options.SigningCertificate = null;

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("SigningCertificate is required", StringComparison.Ordinal));

        using var publicOnly = CertificateFactory.PublicOnly();
        options.SigningCertificate = publicOnly;

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("no private key", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_expired_signing_certificate()
    {
        using var expired = CertificateFactory.WithPrivateKey(expired: true);

        var errors = ClaveOptionsValidator.Validate(Valid(expired));

        Assert.Contains(errors, e => e.Contains("expired on", StringComparison.Ordinal));
    }

    [Fact]
    public void Warns_before_the_signing_certificate_expires()
    {
        using var soon = CertificateFactory.WithPrivateKey(daysValid: 10);

        var errors = ClaveOptionsValidator.Validate(Valid(soon));

        Assert.Contains(errors, e => e.Contains("expires in", StringComparison.Ordinal));
    }

    [Fact]
    public void Requires_a_pinned_pasarela_certificate()
    {
        var options = Valid();
        options.PasarelaSigningCertificate = null;

        var errors = ClaveOptionsValidator.Validate(options);

        Assert.Contains(errors, e => e.Contains("pinned key", StringComparison.Ordinal));
    }

    [Fact]
    public void Requires_person_identifier_to_be_requested()
    {
        var options = Valid();
        options.RequestedAttributes = [ClaveAttributes.GivenName];

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("must contain PersonIdentifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_required_attribute_that_was_not_requested()
    {
        var options = Valid();
        options.RequiredAttributes = ["http://eidas.europa.eu/attributes/naturalperson/BirthName"];

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("not in RequestedAttributes", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_required_date_of_birth()
    {
        var options = Valid();
        options.RequiredAttributes = [ClaveAttributes.PersonIdentifier, ClaveAttributes.DateOfBirth];

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("DateOfBirth must not be required", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_the_low_level()
    {
        var options = Valid();
        options.DefaultLevel = AuthAssuranceLevel.Low;

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("not offered", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_invalid_tenant()
    {
        var options = Valid();
        options.Tenant = "CAIB Madrid";

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("Tenant", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_short_subject_key()
    {
        var options = Valid();
        options.SubjectKey = new byte[16];

        Assert.Contains(ClaveOptionsValidator.Validate(options), e => e.Contains("at least 32 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Throws_with_every_problem_on_its_own_line()
    {
        var options = new ClaveOptions { Tenant = null };

        var exception = Assert.Throws<AuthKitConfigurationException>(() => ClaveOptionsValidator.ValidateAndThrow(options));

        Assert.Contains("EntityId is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SubjectKey is required", exception.Message, StringComparison.Ordinal);
    }

    private ClaveOptions Valid(X509Certificate2? signing = null) => new()
    {
        Environment = ClaveEnvironment.Preproduction,
        EntityId = "https://sede.example.es/clave/metadata",
        AcsUrl = "https://sede.example.es/clave/acs",
        SigningCertificate = signing ?? _signing,
        PasarelaSigningCertificate = _pasarela,
        Tenant = "caib",
        SubjectKey = ValidSubjectKey
    };
}

internal static class CertificateFactory
{
    public static X509Certificate2 WithPrivateKey(bool expired = false, int daysValid = 365)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=authkit-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var notBefore = expired ? DateTimeOffset.UtcNow.AddDays(-30) : DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = expired ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(daysValid);

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public static X509Certificate2 PublicOnly()
    {
        using var full = WithPrivateKey();
        return X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert));
    }
}
