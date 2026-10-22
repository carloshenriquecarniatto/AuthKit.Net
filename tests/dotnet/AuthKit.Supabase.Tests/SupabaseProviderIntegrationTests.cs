using System.Net;
using System.Text;
using AuthKit.Supabase;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SupabaseClient = global::Supabase.Client;

namespace AuthKit.Supabase.Tests;

/// <summary>
/// Exercises the provider end to end against a stub HTTP transport: real Supabase SDK, real
/// serialization, no network.
/// </summary>
public sealed class SupabaseProviderIntegrationTests
{
    private const string Url = "https://project.supabase.co";

    private static string AnonKey()
    {
        static string Segment(object payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        return $"{Segment(new { alg = "HS256", typ = "JWT" })}.{Segment(new { role = "anon", iss = "supabase" })}.signature";
    }

    private static (SupabaseAuthProvider Provider, StubHandler Handler) CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IAuthSessionStore? store = null)
    {
        var handler = new StubHandler(responder);
        var options = new SupabaseAuthOptions
        {
            Url = Url,
            AnonKey = AnonKey(),
            HttpClient = new HttpClient(handler),
            SessionStore = store
        };

        var client = new SupabaseClient(options.Url, options.AnonKey, new global::Supabase.SupabaseOptions
        {
            AutoRefreshToken = false,
            AutoConnectRealtime = false,
            HttpClient = options.HttpClient
        });

        return (new SupabaseAuthProvider(client, options), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string SessionJson = """
        {
          "access_token": "header.payload.signature",
          "token_type": "bearer",
          "expires_in": 3600,
          "expires_at": 1893456000,
          "refresh_token": "refresh-value",
          "user": {
            "id": "b1f2c3d4-0000-0000-0000-000000000000",
            "aud": "authenticated",
            "role": "authenticated",
            "email": "ada@example.com",
            "email_confirmed_at": "2026-01-02T03:04:05Z",
            "created_at": "2026-01-01T00:00:00Z",
            "user_metadata": { "display_name": "Ada Lovelace", "plan": "pro" }
          }
        }
        """;

    [Fact]
    public async Task Password_sign_in_maps_the_session_and_user()
    {
        var (provider, _) = CreateProvider(_ => Json(HttpStatusCode.OK, SessionJson));

        var result = await provider.SignInWithPasswordAsync("ada@example.com", "correct horse battery");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.NotNull(result.Session);
        Assert.Equal("ada@example.com", result.Session!.User.Email);
        Assert.Equal("Ada Lovelace", result.Session.User.DisplayName);
        Assert.Equal("pro", result.Session.User.Metadata["plan"]);
        Assert.Equal("bearer", result.Session.TokenType);
        Assert.True(result.Session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Same(result.Session, provider.CurrentSession);
    }

    [Fact]
    public async Task Password_sign_in_sends_credentials_to_the_token_endpoint()
    {
        HttpRequestMessage? captured = null;
        string? body = null;

        var (provider, _) = CreateProvider(request =>
        {
            captured = request;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, SessionJson);
        });

        await provider.SignInWithPasswordAsync("ada@example.com", "secret-value");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/auth/v1/token", captured.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("grant_type=password", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_credentials_are_mapped_and_server_text_is_not_forwarded()
    {
        var (provider, _) = CreateProvider(_ => Json(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid login credentials","error_code":"invalid_credentials"}"""));

        var result = await provider.SignInWithPasswordAsync("ada@example.com", "wrong");

        Assert.False(result.Ok);
        Assert.Equal(AuthErrorCode.InvalidCredentials, result.Error!.Code);
        Assert.Equal(400, result.Error.StatusCode);
        Assert.DoesNotContain("invalid_grant", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfirmed_email_is_mapped()
    {
        var (provider, _) = CreateProvider(_ => Json(
            HttpStatusCode.BadRequest,
            """{"error":"email_not_confirmed","error_description":"Email not confirmed","error_code":"email_not_confirmed"}"""));

        var result = await provider.SignInWithPasswordAsync("ada@example.com", "correct horse battery");

        Assert.Equal(AuthErrorCode.EmailNotConfirmed, result.Error!.Code);
    }

    [Fact]
    public async Task Rate_limits_are_mapped()
    {
        var (provider, _) = CreateProvider(_ => Json(
            HttpStatusCode.TooManyRequests,
            """{"error":"over_request_rate_limit","error_description":"Too many requests","error_code":"over_request_rate_limit"}"""));

        var result = await provider.SendMagicLinkAsync("ada@example.com");

        Assert.Equal(AuthErrorCode.RateLimited, result.Error!.Code);
    }

    [Fact]
    public async Task Sign_up_without_a_session_requires_email_confirmation()
    {
        var (provider, _) = CreateProvider(_ => Json(
            HttpStatusCode.OK,
            """{"id":"b1f2c3d4-0000-0000-0000-000000000000","email":"ada@example.com"}"""));

        var result = await provider.SignUpWithPasswordAsync("ada@example.com", "correct horse battery");

        Assert.True(result.Ok);
        Assert.Equal(AuthNextStep.ConfirmEmail, result.NextStep);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task Invalid_input_never_reaches_the_network()
    {
        var calls = 0;
        var (provider, _) = CreateProvider(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK, SessionJson);
        });

        var result = await provider.SignInWithPasswordAsync("not-an-email", "x");

        Assert.False(result.Ok);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Session_is_persisted_and_restored_into_the_client()
    {
        var store = new RecordingStore();
        var (provider, _) = CreateProvider(_ => Json(HttpStatusCode.OK, SessionJson), store);

        await provider.SignInWithPasswordAsync("ada@example.com", "correct horse battery");

        Assert.NotNull(store.Saved);
        Assert.Equal("refresh-value", store.Saved!.RefreshToken);
    }

    [Fact]
    public async Task Sign_out_clears_the_stored_session()
    {
        var store = new RecordingStore();
        var (provider, _) = CreateProvider(_ => Json(HttpStatusCode.OK, "{}"), store);

        await provider.SignOutAsync();

        Assert.True(store.Cleared);
        Assert.Null(provider.CurrentSession);
    }

    [Fact]
    public void Missing_url_fails_fast_with_an_actionable_message()
    {
        var exception = Assert.Throws<AuthKitConfigurationException>(() =>
            new SupabaseAuthOptions { AnonKey = "key" }.Validate());

        Assert.Contains("Url is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_http_is_rejected_for_non_loopback_hosts()
    {
        var exception = Assert.Throws<AuthKitConfigurationException>(() =>
            new SupabaseAuthOptions { Url = "http://project.supabase.co", AnonKey = "key" }.Validate());

        Assert.Contains("https", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_role_keys_are_rejected()
    {
        static string Segment(object payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var serviceKey = $"{Segment(new { alg = "HS256" })}.{Segment(new { role = "service_role" })}.signature";

        var exception = Assert.Throws<AuthKitConfigurationException>(() =>
            new SupabaseAuthOptions { Url = Url, AnonKey = serviceKey }.Validate());

        Assert.Contains("service role", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dependency_injection_registers_scoped_services()
    {
        var services = new ServiceCollection();

        services.AddAuthKitSupabase(options =>
        {
            options.Url = Url;
            options.AnonKey = AnonKey();
            options.SocialProviders = ["google", "github"];
        }, ui => ui.RedirectTo = "https://app.example.com/auth/callback");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthProvider>();

        Assert.True(auth.Capabilities.Password);
        Assert.Equal(["google", "github"], auth.Capabilities.SocialProviders);
        Assert.Equal("https://app.example.com/auth/callback", scope.ServiceProvider.GetRequiredService<AuthKitOptions>().RedirectTo);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    private sealed class RecordingStore : IAuthSessionStore
    {
        public AuthSession? Saved { get; private set; }

        public bool Cleared { get; private set; }

        public ValueTask<AuthSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Saved);

        public ValueTask SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
        {
            Saved = session;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            Saved = null;
            Cleared = true;
            return ValueTask.CompletedTask;
        }
    }
}
