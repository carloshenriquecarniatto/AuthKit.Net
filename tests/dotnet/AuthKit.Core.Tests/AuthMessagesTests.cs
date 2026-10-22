using System.Net;
using System.Text;
using Xunit;

namespace AuthKit.Core.Tests;

public sealed class AuthMessagesTests
{
    private static string MessageFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var attempt = 0; attempt < 12 && directory is not null; attempt++)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "messages", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate samples/messages/{name}.");
    }

    [Fact]
    public void English_resolves_every_key()
    {
        foreach (var key in Enum.GetValues<AuthMessageKey>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AuthMessages.English.Resolve(key)));
        }
    }

    [Fact]
    public void Provider_placeholder_is_replaced_only_when_a_provider_is_supplied()
    {
        Assert.Equal("Continue with Github", AuthMessages.English.Resolve(AuthMessageKey.ContinueWithProvider, "Github"));
        Assert.Equal("Continue with {provider}", AuthMessages.English.Resolve(AuthMessageKey.ContinueWithProvider));
    }

    [Fact]
    public void Dictionary_falls_back_per_key()
    {
        var partial = AuthMessages.FromDictionary(new Dictionary<AuthMessageKey, string>
        {
            [AuthMessageKey.SignIn] = "Entrar"
        });

        Assert.Equal("Entrar", partial.Resolve(AuthMessageKey.SignIn));
        Assert.Equal("Enter your email", partial.Resolve(AuthMessageKey.Email));
    }

    [Fact]
    public void Json_maps_camel_case_keys_and_ignores_unknown_ones()
    {
        var messages = AuthMessages.FromJson("""
            {
              "signIn": "Entrar",
              "passwordTooShort": "Mínimo de {min} caracteres",
              "notARealKey": "ignored"
            }
            """);

        Assert.Equal("Entrar", messages.Resolve(AuthMessageKey.SignIn));
        Assert.Equal("Mínimo de {min} caracteres", messages.Resolve(AuthMessageKey.PasswordTooShort));
        Assert.Equal("Enter your email", messages.Resolve(AuthMessageKey.Email));
    }

    [Fact]
    public void Json_accepts_keys_in_any_case()
    {
        var messages = AuthMessages.FromJson("""{ "SIGNIN": "Entrar", "Email": "Correo" }""");

        Assert.Equal("Entrar", messages.Resolve(AuthMessageKey.SignIn));
        Assert.Equal("Correo", messages.Resolve(AuthMessageKey.Email));
    }

    [Fact]
    public void Json_rejects_invalid_documents()
    {
        Assert.Throws<AuthKitConfigurationException>(() => AuthMessages.FromJson("{ not json"));
        Assert.Throws<AuthKitConfigurationException>(() => AuthMessages.FromJson("[]"));
    }

    [Fact]
    public void Json_rejects_payloads_above_the_limit()
    {
        var huge = $$"""{"signIn":"{{new string('a', JsonAuthMessages.MaximumPayloadBytes)}}"}""";

        Assert.Throws<AuthKitConfigurationException>(() => AuthMessages.FromJson(huge));
    }

    [Theory]
    [InlineData("authkit.pt-BR.json", "Entrar")]
    [InlineData("authkit.es.json", "Entrar")]
    public async Task Bundled_catalogs_cover_every_key(string file, string signIn)
    {
        var messages = await AuthMessages.FromFileAsync(MessageFile(file));

        Assert.Equal(signIn, messages.Resolve(AuthMessageKey.SignIn));

        foreach (var key in Enum.GetValues<AuthMessageKey>())
        {
            Assert.NotEqual(key.ToString(), messages.Resolve(key));
        }
    }

    [Fact]
    public async Task File_source_reports_a_missing_file()
    {
        var source = new FileAuthMessagesSource(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() => source.LoadAsync().AsTask());
    }

    [Fact]
    public async Task Http_source_fetches_and_decodes_the_catalog()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"signIn":"Entrar"}"""));
        var source = new HttpAuthMessagesSource(new HttpClient(handler), new Uri("https://cdn.example.com/authkit.pt-BR.json"));

        var messages = await source.LoadAsync();

        Assert.Equal("Entrar", messages.Resolve(AuthMessageKey.SignIn));
        Assert.Equal("Enter your email", messages.Resolve(AuthMessageKey.Email));
        Assert.Equal("https://cdn.example.com/authkit.pt-BR.json", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task Http_source_surfaces_the_status_code()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var source = new HttpAuthMessagesSource(new HttpClient(handler), new Uri("https://cdn.example.com/missing.json"));

        var exception = await Assert.ThrowsAsync<AuthKitConfigurationException>(() => source.LoadAsync().AsTask());

        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_source_only_accepts_http_schemes()
    {
        Assert.Throws<AuthKitConfigurationException>(() =>
            new HttpAuthMessagesSource(new HttpClient(), new Uri("file:///etc/passwd")));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }
}
