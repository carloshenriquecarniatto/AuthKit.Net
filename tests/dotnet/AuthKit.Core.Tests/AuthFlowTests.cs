using Xunit;

namespace AuthKit.Core.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task Successful_sign_in_ends_authenticated()
    {
        var flow = new AuthFlow();
        var session = TestSession();

        var result = await flow.RunAsync(_ => Task.FromResult(AuthResult.Success(session)));

        Assert.True(result.Ok);
        Assert.Equal(AuthState.Authenticated, flow.State);
        Assert.Same(session, flow.Session);
        Assert.Null(flow.Error);
    }

    [Fact]
    public async Task Failure_ends_in_error_state_without_leaking_exception_text()
    {
        var flow = new AuthFlow();

        var result = await flow.RunAsync(_ =>
            Task.FromResult(AuthResult.Failure(AuthError.From(AuthErrorCode.InvalidCredentials, "Invalid email or password."))));

        Assert.False(result.Ok);
        Assert.Equal(AuthState.Error, flow.State);
        Assert.Equal(AuthErrorCode.InvalidCredentials, flow.Error!.Code);
    }

    [Fact]
    public async Task Thrown_exception_is_converted_to_a_generic_error()
    {
        var flow = new AuthFlow();

        var result = await flow.RunAsync(_ => throw new InvalidOperationException("connection string secret"));

        Assert.False(result.Ok);
        Assert.Equal(AuthErrorCode.Unknown, result.Error!.Code);
        Assert.DoesNotContain("connection string secret", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmation_step_moves_to_awaiting_email()
    {
        var flow = new AuthFlow();

        await flow.RunAsync(_ => Task.FromResult(AuthResult.Continue(AuthNextStep.ConfirmEmail)));

        Assert.Equal(AuthState.AwaitingEmailConfirmation, flow.State);
        Assert.Null(flow.Session);
    }

    [Fact]
    public async Task Otp_step_moves_to_awaiting_otp()
    {
        var flow = new AuthFlow();

        await flow.RunAsync(_ => Task.FromResult(AuthResult.Continue(AuthNextStep.VerifyOtp)));

        Assert.Equal(AuthState.AwaitingOtp, flow.State);
    }

    [Fact]
    public async Task Concurrent_submissions_are_rejected_and_only_one_reaches_the_provider()
    {
        var flow = new AuthFlow();
        var gate = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var first = flow.RunAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        });

        var second = await flow.RunAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(AuthResult.Success(TestSession()));
        });

        gate.SetResult(AuthResult.Success(TestSession()));
        var firstResult = await first;

        Assert.True(firstResult.Ok);
        Assert.False(second.Ok);
        Assert.Equal(1, calls);
    }

    internal static AuthSession TestSession() => new()
    {
        AccessToken = "access-token-value",
        RefreshToken = "refresh-token-value",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        User = new AuthUser { Id = "user-1", Email = "user@example.com" }
    };
}

public sealed class AuthSessionTests
{
    [Fact]
    public void To_string_redacts_tokens()
    {
        var text = AuthFlowTests.TestSession().ToString();

        Assert.DoesNotContain("access-token-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-value", text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Record_deconstruction_does_not_print_tokens()
    {
        var text = AuthFlowTests.TestSession().ToString();

        Assert.DoesNotContain("Token", text.Replace("Tokens", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Expiry_uses_a_thirty_second_skew()
    {
        var now = DateTimeOffset.UtcNow;
        var session = AuthFlowTests.TestSession() with { ExpiresAt = now.AddSeconds(10) };

        Assert.True(session.IsExpired(now));
        Assert.False((session with { ExpiresAt = now.AddSeconds(60) }).IsExpired(now));
    }
}

public sealed class ValidationTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user.name+tag@sub.example.co.uk", true)]
    [InlineData("user@example", false)]
    [InlineData("user @example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Email_validation_is_strict(string? email, bool expected) =>
        Assert.Equal(expected, AuthValidation.IsValidEmail(email));

    [Fact]
    public void Email_validation_rejects_hostile_input_quickly()
    {
        var hostile = new string('a', 200_000) + "@example.com";

        var started = DateTimeOffset.UtcNow;
        var valid = AuthValidation.IsValidEmail(hostile);

        Assert.False(valid);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData(null, false)]
    public void Password_policy_defaults_to_eight_characters(string? password, bool expected) =>
        Assert.Equal(expected, AuthValidation.IsValidPassword(password));

    [Fact]
    public void Password_confirmation_is_ordinal()
    {
        Assert.True(AuthValidation.PasswordsMatch("Senha123", "Senha123"));
        Assert.False(AuthValidation.PasswordsMatch("Senha123", "senha123"));
    }
}

public sealed class ThemeTests
{
    [Fact]
    public void Css_variables_are_emitted_with_the_expected_names()
    {
        var css = AuthTheme.Default.ToCssVariables();

        Assert.Contains("--ak-primary:", css, StringComparison.Ordinal);
        Assert.Contains("--ak-font-family:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Style_injection_attempts_are_neutralised()
    {
        var css = (AuthTheme.Default with { Primary = "red;}</style><script>alert(1)</script>" }).ToCssVariables();

        // The payload must not be able to close the declaration or the style block.
        Assert.DoesNotContain("<", css, StringComparison.Ordinal);
        Assert.DoesNotContain("}", css, StringComparison.Ordinal);
        Assert.Equal(10, css.Count(character => character == ';'));
    }

    [Fact]
    public void Remote_fetches_are_rejected_in_tokens()
    {
        var css = (AuthTheme.Default with { Background = "url(https://evil.example/x.png)" }).ToCssVariables();

        Assert.DoesNotContain("url(", css, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.png", true)]
    [InlineData("/logo.png", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//evil.example/logo.png", false)]
    public void Logo_url_only_accepts_relative_or_http_urls(string url, bool accepted)
    {
        var theme = AuthTheme.Default with { LogoUrl = url };

        Assert.Equal(accepted, theme.SafeLogoUrl() is not null);
    }
}
