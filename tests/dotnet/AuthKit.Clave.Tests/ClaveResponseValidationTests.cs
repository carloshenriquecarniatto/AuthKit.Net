using AuthKit.Clave;
using Xunit;

namespace AuthKit.Clave.Tests;

/// <summary>
/// The validation chain of docs/spec-clave.md §5.5, case by case. Every test starts from the
/// committed fixture and mutates exactly one thing, so a failure names the control that broke.
/// </summary>
public sealed class ClaveResponseValidationTests
{
    [Fact]
    public async Task A_valid_response_produces_an_identity()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending).Base64,
            pending.State);

        Assert.True(result.Ok, result.Reason.ToString());
        var identity = Assert.IsType<ClaveIdentity>(result.Identity);
        Assert.Equal(AuthDocumentType.Dni, identity.DocumentType);
        Assert.Equal("12345678Z", identity.DocumentId);
        Assert.Equal("MARÍA", identity.GivenName);
        Assert.Equal("GARCÍA", identity.FamilyName);
        Assert.Equal(new DateOnly(1980, 5, 17), identity.DateOfBirth);
        Assert.Equal(AuthAssuranceLevel.Substantial, identity.Level);
        Assert.Equal("clave_movil", identity.Method);
        Assert.Equal("caib", identity.Tenant);
        Assert.Equal("/expedientes", identity.ReturnUrl);
    }

    [Fact]
    public async Task The_subject_is_opaque_and_stable_per_tenant()
    {
        using var env = new ClaveTestEnvironment();
        var first = await StartAsync(env);
        var second = await StartAsync(env);

        var one = await env.Validator.ValidateAsync(env.Sign(first).Base64, first.State);
        var two = await env.Validator.ValidateAsync(env.Sign(second).Base64, second.State);

        Assert.True(one.Ok);
        Assert.True(two.Ok);
        Assert.Equal(one.Identity!.Subject, two.Identity!.Subject);
        Assert.DoesNotContain("12345678Z", one.Identity.Subject, StringComparison.Ordinal);

        var otherTenant = ClaveIdentity.DeriveSubject("12345678Z", "palma", env.Options.SubjectKey!);
        Assert.NotEqual(one.Identity.Subject, otherTenant);
    }

    [Fact]
    public async Task An_unsigned_assertion_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);
        var assertion = env.Sign(pending);

        var document = new System.Xml.XmlDocument { PreserveWhitespace = false };
        document.LoadXml(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(assertion.Base64)));

        var signatures = document.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        while (signatures.Count > 0)
        {
            signatures[0]!.ParentNode!.RemoveChild(signatures[0]!);
        }

        var result = await env.Validator.ValidateAsync(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(document.OuterXml)),
            assertion.RelayState);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task A_signature_from_another_key_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, signingCertificate: env.Attacker).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task A_tampered_attribute_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);
        var assertion = env.Sign(pending);

        var tampered = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(assertion.Base64))
            .Replace("MARÍA", "ATACANTE", StringComparison.Ordinal);

        var result = await env.Validator.ValidateAsync(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tampered)),
            assertion.RelayState);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.SignatureInvalid, result.Reason);
    }

    [Fact]
    public async Task A_dtd_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        const string dtd = "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>";
        var xml = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(env.Sign(pending).Base64));
        var withDtd = xml.Replace("<samlp:Response", dtd + "<samlp:Response", StringComparison.Ordinal);

        var result = await env.Validator.ValidateAsync(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(withDtd)),
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.UnsafeXml, result.Reason);
    }

    [Fact]
    public async Task An_oversized_response_is_rejected_before_parsing()
    {
        using var env = new ClaveTestEnvironment();
        env.Options.MaxResponseBytes = 1024;
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            Convert.ToBase64String(new byte[2048]),
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.MalformedResponse, result.Reason);
    }

    [Fact]
    public async Task A_response_for_another_audience_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, audience: "https://otra-sede.example.es/metadata").Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.AudienceMismatch, result.Reason);
    }

    [Fact]
    public async Task A_response_for_another_acs_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, destination: "https://otra-sede.example.es/clave/acs").Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.DestinationMismatch, result.Reason);
    }

    [Fact]
    public async Task A_response_from_another_issuer_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, issuer: "https://pasarela-clave.example.net/Proxy2").Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.IssuerMismatch, result.Reason);
    }

    [Fact]
    public async Task A_gateway_error_status_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, statusCode: "urn:oasis:names:tc:SAML:2.0:status:Requester").Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.StatusNotSuccess, result.Reason);
        Assert.Equal("urn:oasis:names:tc:SAML:2.0:status:Requester", result.StatusCode);
    }

    [Fact]
    public async Task An_expired_assertion_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(
                pending,
                notBefore: env.Clock.Now.AddMinutes(-30),
                notOnOrAfter: env.Clock.Now.AddMinutes(-10)).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.AssertionExpired, result.Reason);
    }

    [Fact]
    public async Task A_response_that_has_not_started_yet_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(
                pending,
                notBefore: env.Clock.Now.AddMinutes(20),
                notOnOrAfter: env.Clock.Now.AddMinutes(40)).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.AssertionExpired, result.Reason);
    }

    [Fact]
    public async Task A_replayed_assertion_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var assertionId = "_a-replay";

        var first = await StartAsync(env);
        var firstResult = await env.Validator.ValidateAsync(
            env.Sign(first, assertionId: assertionId).Base64,
            first.State);
        Assert.True(firstResult.Ok);

        // A second, independently started login carrying the same assertion: the replay cache is
        // the backstop that catches it even when the request store was consumed twice.
        var second = await StartAsync(env);
        var secondResult = await env.Validator.ValidateAsync(
            env.Sign(second, assertionId: assertionId).Base64,
            second.State);

        Assert.False(secondResult.Ok);
        Assert.Equal(ClaveFailureReason.ReplayDetected, secondResult.Reason);
    }

    [Fact]
    public async Task A_level_below_the_requested_one_fails()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env, AuthAssuranceLevel.High);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, granted: AuthAssuranceLevel.Substantial).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.LevelBelowRequested, result.Reason);
        Assert.Equal(AuthErrorCode.LoaNotMet, ClaveErrorMapper.ToErrorCode(result.Reason));
    }

    [Fact]
    public async Task A_malformed_document_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, documentId: "12345678A").Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.DocumentInvalid, result.Reason);
    }

    [Fact]
    public async Task A_missing_person_identifier_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending, documentId: null).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.SubjectMissing, result.Reason);
    }

    [Fact]
    public async Task A_response_to_an_unknown_request_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = env.Pending();

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.RequestUnknown, result.Reason);
    }

    [Fact]
    public async Task An_expired_request_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env, expiresAt: env.Clock.Now.AddMinutes(-1));

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending).Base64,
            pending.State);

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.RequestExpired, result.Reason);
    }

    [Fact]
    public async Task A_mismatched_relay_state_is_rejected()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);

        var result = await env.Validator.ValidateAsync(
            env.Sign(pending).Base64,
            "state-from-another-login");

        Assert.False(result.Ok);
        Assert.Equal(ClaveFailureReason.RelayStateMismatch, result.Reason);
    }

    [Fact]
    public void Every_internal_reason_maps_to_a_generic_outward_error()
    {
        var reasons = Enum.GetValues<ClaveFailureReason>()
            .Where(reason => reason is not (ClaveFailureReason.LevelBelowRequested
                or ClaveFailureReason.SubjectMissing
                or ClaveFailureReason.DocumentInvalid
                or ClaveFailureReason.CertificateUnknown
                or ClaveFailureReason.TenantMismatch
                or ClaveFailureReason.None));

        foreach (var reason in reasons)
        {
            Assert.Equal(AuthErrorCode.FederationFailed, ClaveErrorMapper.ToErrorCode(reason));
        }

        Assert.Equal(AuthErrorCode.LoaNotMet, ClaveErrorMapper.ToErrorCode(ClaveFailureReason.LevelBelowRequested));
    }

    [Fact]
    public async Task The_identity_never_prints_the_document()
    {
        using var env = new ClaveTestEnvironment();
        var pending = await StartAsync(env);
        var result = await env.Validator.ValidateAsync(env.Sign(pending).Base64, pending.State);

        var text = result.Identity!.ToString();

        Assert.DoesNotContain("12345678Z", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
    }

    private static async Task<PendingClaveRequest> StartAsync(
        ClaveTestEnvironment env,
        AuthAssuranceLevel level = AuthAssuranceLevel.Substantial,
        DateTimeOffset? expiresAt = null)
    {
        var pending = env.Pending(level: level, expiresAt: expiresAt);
        await env.Requests.SaveAsync(pending);
        return pending;
    }
}
