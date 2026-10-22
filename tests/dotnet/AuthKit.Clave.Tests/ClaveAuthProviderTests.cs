using AuthKit.Clave;
using Xunit;

namespace AuthKit.Clave.Tests;

/// <summary>
/// The browser-facing provider: it builds a host URL and reads the host redirect. No SAML, no
/// tokens, no navigation, no password flows.
/// </summary>
public sealed class ClaveAuthProviderTests
{
    private static ClaveAuthProvider Provider() => new(
        new AuthFederationProvider
        {
            Id = "clave",
            Kind = "clave",
            DisplayName = "Cl@ve",
            Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High],
            Methods =
            [
                new AuthFederationMethod
                {
                    Id = ClaveMethods.Movil,
                    DisplayName = "Cl@ve Móvil",
                    Levels = [AuthAssuranceLevel.Substantial],
                    Recommended = true
                },
                new AuthFederationMethod
                {
                    Id = ClaveMethods.Certificate,
                    DisplayName = "Certificate",
                    Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High]
                }
            ]
        },
        "clientes/clave");

    [Fact]
    public void The_capabilities_declare_the_federation_and_nothing_else()
    {
        var capabilities = Provider().Capabilities;

        Assert.Single(capabilities.Federation);
        Assert.Equal("clave", capabilities.Federation[0].Kind);
        Assert.False(capabilities.Password);
        Assert.False(capabilities.MagicLink);
        Assert.False(capabilities.SignUp);
    }

    [Fact]
    public async Task BeginFederation_builds_the_host_login_url()
    {
        var url = await Provider().BeginFederationAsync(new AuthFederationRequest
        {
            ProviderRef = "clientes/clave",
            Level = AuthAssuranceLevel.High,
            Method = ClaveMethods.Certificate,
            ReturnUrl = "/expedientes"
        });

        Assert.StartsWith("/clave/login?", url, StringComparison.Ordinal);
        Assert.Contains("loa=high", url, StringComparison.Ordinal);
        Assert.Contains("method=certificate", url, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2Fexpedientes", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginFederation_refuses_a_level_or_method_the_gateway_cannot_serve()
    {
        var provider = Provider();

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() =>
            provider.BeginFederationAsync(new AuthFederationRequest
            {
                ProviderRef = "clientes/clave",
                Level = AuthAssuranceLevel.Low
            }));

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() =>
            provider.BeginFederationAsync(new AuthFederationRequest
            {
                ProviderRef = "clientes/clave",
                Method = "clave_sms"
            }));
    }

    [Fact]
    public async Task CompleteFederation_reads_the_host_error_without_leaking_it()
    {
        var provider = Provider();

        var error = await provider.CompleteFederationAsync("https://sede.example.es/clave/cb?error=federation_failed&error_reason=SignatureInvalid");
        Assert.False(error.Ok);
        Assert.Equal(AuthErrorCode.FederationFailed, error.Error!.Code);
        Assert.DoesNotContain("SignatureInvalid", error.Error.Message, StringComparison.Ordinal);

        var success = await provider.CompleteFederationAsync("https://sede.example.es/panel");
        Assert.True(success.Ok);
        Assert.Null(success.Session);
    }

    [Fact]
    public async Task An_unknown_error_code_maps_to_unknown()
    {
        var result = await Provider().CompleteFederationAsync("/cb?error=SomethingInternal");

        Assert.False(result.Ok);
        Assert.Equal(AuthErrorCode.Unknown, result.Error!.Code);
    }

    [Fact]
    public async Task Password_flows_are_disabled_rather_than_silently_ignored()
    {
        var result = await Provider().SignInWithPasswordAsync("ada@example.com", "secret");

        Assert.False(result.Ok);
        Assert.Equal(AuthErrorCode.ProviderDisabled, result.Error!.Code);
    }

    [Fact]
    public async Task OAuth_entry_points_are_rejected_with_guidance()
    {
        await Assert.ThrowsAsync<AuthKitConfigurationException>(() =>
            Provider().BeginOAuthAsync("clave"));
    }

    [Fact]
    public async Task There_is_never_a_browser_session()
    {
        var provider = Provider();
        Assert.Null(provider.CurrentSession);

        var refreshed = await provider.RefreshSessionAsync();
        Assert.True(refreshed.Ok);
        Assert.Null(refreshed.Session);

        await provider.SignOutAsync();
        Assert.Null(provider.CurrentSession);
    }
}
