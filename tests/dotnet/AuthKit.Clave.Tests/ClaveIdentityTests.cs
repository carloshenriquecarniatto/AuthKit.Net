using System.Security.Claims;
using Xunit;

namespace AuthKit.Clave.Tests;

public sealed class ClaveIdentityTests
{
    private static readonly byte[] Key = Enumerable.Repeat((byte)7, 32).ToArray();

    [Fact]
    public void Subject_is_stable_for_the_same_document_and_tenant()
    {
        var first = ClaveIdentity.DeriveSubject("12345678Z", "caib", Key);
        var second = ClaveIdentity.DeriveSubject("12345678Z", "caib", Key);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Subject_differs_across_tenants_so_entities_cannot_correlate()
    {
        var caib = ClaveIdentity.DeriveSubject("12345678Z", "caib", Key);
        var palma = ClaveIdentity.DeriveSubject("12345678Z", "palma", Key);

        Assert.NotEqual(caib, palma);
    }

    [Fact]
    public void Subject_does_not_contain_the_document()
    {
        var subject = ClaveIdentity.DeriveSubject("12345678Z", "caib", Key);

        Assert.DoesNotContain("12345678Z", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_requires_a_key_of_at_least_thirty_two_bytes()
    {
        Assert.Throws<ArgumentException>(() => ClaveIdentity.DeriveSubject("12345678Z", "caib", new byte[16]));
    }

    [Fact]
    public void Claims_expose_the_standard_identity_fields()
    {
        var principal = Sample().ToClaimsPrincipal("clientes/clave");

        Assert.Equal("subject-abc", principal.FindFirst("sub")?.Value);
        Assert.Equal("MARÍA GARCÍA", principal.FindFirst("name")?.Value);
        Assert.Equal("http://eidas.europa.eu/LoA/substantial", principal.FindFirst("acr")?.Value);
        Assert.Equal("clave_movil", principal.FindFirst("amr")?.Value);
        Assert.Equal("clientes/clave", principal.FindFirst("authkit:provider")?.Value);
        Assert.Equal("clientes", principal.FindFirst("authkit:audience")?.Value);
        Assert.Equal("caib", principal.FindFirst("authkit:tenant")?.Value);
    }

    [Fact]
    public void The_document_is_not_a_claim_unless_explicitly_requested()
    {
        var minimal = Sample().ToClaimsPrincipal("clientes/clave");
        Assert.Null(minimal.FindFirst("authkit:doc_id"));

        var withDocument = Sample().ToClaimsPrincipal("clientes/clave", includeDocumentId: true);
        Assert.Equal("12345678Z", withDocument.FindFirst("authkit:doc_id")?.Value);
    }

    [Fact]
    public void Representation_is_exposed_as_the_actor_claim()
    {
        var principal = Sample() with { RepresentedDocumentId = "B12345678" };

        var claims = principal.ToClaimsPrincipal("clientes/clave");

        Assert.Equal("B12345678", claims.FindFirst("act")?.Value);
    }

    [Fact]
    public void To_string_redacts_the_document()
    {
        var text = Sample().ToString();

        Assert.DoesNotContain("12345678Z", text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
    }

    private static ClaveIdentity Sample() => new()
    {
        Subject = "subject-abc",
        DocumentType = AuthDocumentType.Dni,
        DocumentId = "12345678Z",
        DocumentIdRaw = "ES/ES/12345678Z",
        GivenName = "MARÍA",
        FamilyName = "GARCÍA",
        Level = AuthAssuranceLevel.Substantial,
        Method = "clave_movil",
        Tenant = "caib",
        AuthenticatedAt = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero)
    };
}
