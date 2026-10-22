using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthKit.Blazor.Tests;

public sealed class AuthKitEmailTests
{
    private static AuthSession Session() => new()
    {
        AccessToken = "access",
        RefreshToken = "refresh",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        User = new AuthUser { Id = "user-1", Email = "ada@example.com" }
    };

    private static BunitContext CreateContext(FakeAuthProvider provider, AuthKitOptions? options = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IAuthProvider>(provider);
        context.Services.AddSingleton(options ?? new AuthKitOptions());
        return context;
    }

    [Fact]
    public void Renders_email_password_and_sign_in_by_default()
    {
        using var context = CreateContext(new FakeAuthProvider());
        var cut = context.Render<AuthKitEmail>();

        Assert.NotNull(cut.Find("#ak-email"));
        Assert.NotNull(cut.Find("#ak-password"));
        Assert.Contains("Sign In", cut.Find("button[type=submit]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Hides_the_sign_up_toggle_when_the_provider_does_not_support_it()
    {
        using var context = CreateContext(new FakeAuthProvider
        {
            Capabilities = new AuthCapabilities { Password = true, SignUp = false }
        });

        var cut = context.Render<AuthKitEmail>();

        Assert.DoesNotContain("Don't have an account", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Toggles_between_sign_in_and_sign_up()
    {
        using var context = CreateContext(new FakeAuthProvider());
        var cut = context.Render<AuthKitEmail>();

        cut.FindAll("button.ak-link")
            .First(button => button.TextContent.Contains("Don't have an account", StringComparison.Ordinal))
            .Click();

        Assert.Contains("Sign Up", cut.Find("button[type=submit]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_sign_in_invokes_the_callback_with_the_session()
    {
        var provider = new FakeAuthProvider();
        using var context = CreateContext(provider);
        AuthSession? received = null;

        var cut = context.Render<AuthKitEmail>(parameters => parameters
            .Add(component => component.OnSignInComplete, session => received = session));

        cut.Find("#ak-email").Input("ada@example.com");
        cut.Find("#ak-password").Input("correct horse battery");
        await cut.Find("form").SubmitAsync();

        Assert.Equal("ada@example.com", provider.LastEmail);
        Assert.NotNull(received);
        Assert.Equal("user-1", received!.User.Id);
    }

    [Fact]
    public async Task Invalid_email_never_calls_the_provider()
    {
        var provider = new FakeAuthProvider();
        using var context = CreateContext(provider);
        var cut = context.Render<AuthKitEmail>();

        cut.Find("#ak-email").Input("not-an-email");
        cut.Find("#ak-password").Input("correct horse battery");
        await cut.Find("form").SubmitAsync();

        Assert.Null(provider.LastEmail);
        Assert.Contains("valid email", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_failure_is_rendered_as_a_safe_message()
    {
        var provider = new FakeAuthProvider
        {
            SignInResult = AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Invalid email or password."))
        };
        using var context = CreateContext(provider);
        var cut = context.Render<AuthKitEmail>();

        cut.Find("#ak-email").Input("ada@example.com");
        cut.Find("#ak-password").Input("wrong-password");
        await cut.Find("form").SubmitAsync();

        Assert.Contains("Invalid email or password.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_tokens_are_rendered_as_css_variables()
    {
        using var context = CreateContext(
            new FakeAuthProvider(),
            new AuthKitOptions { Theme = AuthTheme.Default with { Primary = "#ff0000" } });

        var cut = context.Render<AuthKitEmail>();

        Assert.Contains("--ak-primary:#ff0000", cut.Find("div.ak-form").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void Prefilled_values_are_applied()
    {
        using var context = CreateContext(new FakeAuthProvider());
        var cut = context.Render<AuthKitEmail>(parameters => parameters
            .Add(component => component.PrefilledEmail, "prefilled@example.com"));

        Assert.Equal("prefilled@example.com", cut.Find("#ak-email").GetAttribute("value"));
    }

    [Fact]
    public void Metadata_fields_are_rendered_on_sign_up()
    {
        using var context = CreateContext(new FakeAuthProvider());
        var cut = context.Render<AuthKitEmail>(parameters => parameters
            .Add(component => component.MetadataFields, new[]
            {
                new AuthMetadataField { Key = "username", Label = "Username" },
                new AuthMetadataField { Key = "terms", Label = "I accept", Kind = AuthMetadataFieldKind.Boolean }
            }));

        cut.FindAll("button.ak-link")
            .First(button => button.TextContent.Contains("Don't have an account", StringComparison.Ordinal))
            .Click();

        Assert.NotNull(cut.Find("#ak-meta-username"));
        Assert.NotNull(cut.Find("#ak-meta-terms"));
    }
}

public sealed class AuthKitMagicLinkTests
{
    [Fact]
    public async Task Sends_the_link_and_shows_the_confirmation()
    {
        var provider = new FakeAuthProvider();
        using var context = new BunitContext();
        context.Services.AddSingleton<IAuthProvider>(provider);
        context.Services.AddSingleton(new AuthKitOptions());

        var cut = context.Render<AuthKitMagicLink>();
        cut.Find("#ak-magic-email").Input("ada@example.com");
        await cut.Find("form").SubmitAsync();

        Assert.Equal(1, provider.MagicLinkCalls);
        Assert.Contains("Check your email inbox!", cut.Markup, StringComparison.Ordinal);
    }
}

public sealed class AuthKitSocialsTests
{
    [Fact]
    public void Renders_configured_providers()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IAuthProvider>(new FakeAuthProvider());
        context.Services.AddSingleton(new AuthKitOptions { SocialProviders = ["google", "github"] });

        var cut = context.Render<AuthKitSocials>();

        Assert.Equal(2, cut.FindAll("button.ak-social").Count);
        Assert.Contains("Continue with Google", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Continue with Github", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emits_the_authorization_url_to_the_host()
    {
        var provider = new FakeAuthProvider { AuthorizationUrl = "https://project.supabase.co/auth/v1/authorize?provider=github" };
        using var context = new BunitContext();
        context.Services.AddSingleton<IAuthProvider>(provider);
        context.Services.AddSingleton(new AuthKitOptions { SocialProviders = ["github"] });

        string? url = null;
        var cut = context.Render<AuthKitSocials>(parameters => parameters
            .Add(component => component.OnOAuthRequested, value => url = value));

        await cut.Find("button.ak-social").ClickAsync(new());

        Assert.Equal(provider.AuthorizationUrl, url);
    }
}
