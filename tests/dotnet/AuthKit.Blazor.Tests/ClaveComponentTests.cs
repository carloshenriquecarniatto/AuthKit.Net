using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace AuthKit.Blazor.Tests;

/// <summary>
/// The Cl@ve UI is descriptor-driven: one control per declared method, a selector when more than
/// one level applies, and no switch on a provider or method id anywhere (R-16 of spec-clave.md).
/// </summary>
public sealed class AuthKitClaveTests
{
    private static AuthFederationProvider Descriptor(params AuthFederationMethod[] methods) => new()
    {
        Id = "clave",
        Kind = "clave",
        DisplayName = "Cl@ve",
        Description = "Authenticate with Cl@ve",
        Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High],
        Methods = methods
    };

    private static AuthFederationMethod Movil() => new()
    {
        Id = "clave_movil",
        DisplayName = "Cl@ve Móvil",
        Levels = [AuthAssuranceLevel.Substantial],
        Recommended = true
    };

    private static AuthFederationMethod Certificate() => new()
    {
        Id = "certificate",
        DisplayName = "Certificate",
        Description = "DNIe or a recognised certificate",
        Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High]
    };

    [Fact]
    public void Renders_one_link_per_declared_method()
    {
        using var context = new BunitContext();
        var cut = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Movil(), Certificate())));

        var methods = cut.FindAll(".ak-federation-method");
        Assert.Equal(2, methods.Count);
        Assert.Contains("Cl@ve Móvil", methods[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Certificate", methods[1].TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Renders_a_level_selector_only_when_more_than_one_level_applies()
    {
        using var context = new BunitContext();

        var withTwo = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Certificate())));
        Assert.Single(withTwo.FindAll("select"));
        Assert.Equal(2, withTwo.FindAll("select option").Count);

        var withOne = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Movil())));
        Assert.Empty(withOne.FindAll("select"));
    }

    [Fact]
    public void Builds_the_host_login_url_from_the_descriptor_and_the_selected_level()
    {
        using var context = new BunitContext();
        var cut = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Movil(), Certificate()))
            .Add(component => component.ReturnUrl, "/expedientes")
            .Add(component => component.LoginPath, "/clave/login"));

        var link = cut.FindAll(".ak-federation-method")[0];
        var href = link.GetAttribute("href")!;

        Assert.Contains("loa=substantial", href, StringComparison.Ordinal);
        Assert.Contains("method=clave_movil", href, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2Fexpedientes", href, StringComparison.Ordinal);

        cut.Find("select").Change(AuthAssuranceLevel.High);
        var highLink = cut.FindAll(".ak-federation-method")[1].GetAttribute("href");

        Assert.Contains("loa=high", highLink, StringComparison.Ordinal);
        Assert.Contains("method=certificate", highLink, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forced_level_removes_the_selector()
    {
        using var context = new BunitContext();
        var cut = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Certificate()))
            .Add(component => component.Level, AuthAssuranceLevel.High));

        Assert.Empty(cut.FindAll("select"));
        Assert.Contains("loa=high", cut.Find(".ak-federation-method").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_is_raised_to_the_host_when_it_opts_in()
    {
        using var context = new BunitContext();
        string? navigated = null;

        var cut = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor(Movil(), Certificate()))
            .Add(component => component.OnNavigate, EventCallback.Factory.Create<string>(this, url => navigated = url)));

        Assert.Equal(2, cut.FindAll("button").Count);
        Assert.Empty(cut.FindAll("a"));

        cut.FindAll("button")[0].Click();

        Assert.NotNull(navigated);
        Assert.Contains("method=clave_movil", navigated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_descriptor_reports_it_instead_of_rendering_nothing()
    {
        using var context = new BunitContext();
        var cut = context.Render<AuthKitClave>(parameters => parameters
            .Add(component => component.Descriptor, Descriptor()));

        Assert.Contains("No login method", cut.Markup, StringComparison.Ordinal);
    }
}

/// <summary>
/// The callback component only reads the redirect. It performs no network call, no token exchange
/// and no navigation unless the host opts in.
/// </summary>
public sealed class AuthKitClaveCallbackTests
{
    [Fact]
    public void A_successful_callback_carries_no_error_and_keeps_the_return_url()
    {
        var outcome = AuthKitClaveCallback.Inspect("https://sede.example.es/panel?returnUrl=%2Fexpedientes&page=2");

        Assert.True(outcome.Ok);
        Assert.Null(outcome.Error);
        Assert.Equal("/expedientes", outcome.ReturnUrl);
        Assert.Equal("https://sede.example.es/panel?returnUrl=%2Fexpedientes&page=2", outcome.CleanUrl);
    }

    [Fact]
    public void An_error_callback_is_mapped_to_the_contract_taxonomy()
    {
        var outcome = AuthKitClaveCallback.Inspect("https://sede.example.es/panel?error=federation_failed&error_reason=SignatureInvalid");

        Assert.False(outcome.Ok);
        Assert.Equal(AuthErrorCode.FederationFailed, outcome.Error!.Code);
        Assert.Equal("https://sede.example.es/panel", outcome.CleanUrl);
    }

    [Fact]
    public void An_unknown_error_code_does_not_leak_internal_detail()
    {
        var outcome = AuthKitClaveCallback.Inspect("https://sede.example.es/panel?error=SignatureInvalid");

        Assert.False(outcome.Ok);
        Assert.Equal(AuthErrorCode.Unknown, outcome.Error!.Code);
        Assert.DoesNotContain("SignatureInvalid", outcome.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_raises_the_outcome_without_navigating()
    {
        using var context = new BunitContext();
        AuthKitFederationOutcome? captured = null;

        context.Render<AuthKitClaveCallback>(parameters => parameters
            .Add(component => component.CallbackUrl, "https://sede.example.es/panel?error=loa_not_met")
            .Add(component => component.OnCompleted, EventCallback.Factory.Create<AuthKitFederationOutcome>(
                this,
                outcome => captured = outcome)));

        Assert.NotNull(captured);
        Assert.False(captured!.Ok);
        Assert.Equal(AuthErrorCode.LoaNotMet, captured.Error!.Code);
    }
}
