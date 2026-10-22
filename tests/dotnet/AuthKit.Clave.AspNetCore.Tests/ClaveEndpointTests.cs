using System.Net;
using System.Net.Http;
using AuthKit.Clave;
using AuthKit.Clave.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthKit.Clave.AspNetCore.Tests;

/// <summary>
/// The HTTP surface end to end, on a real pipeline: metadata, login redirect and ACS. The host
/// callback stands in for <c>SignInAsync</c>, which the library must never call itself (R-14).
/// </summary>
public sealed class ClaveEndpointTests
{
    [Fact]
    public async Task The_metadata_endpoint_serves_the_registered_entity()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(new Uri("/clave/metadata", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/samlmetadata+xml", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ClaveTestEnvironment.EntityId, body, StringComparison.Ordinal);
        Assert.Contains(ClaveTestEnvironment.AcsUrl, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_login_endpoint_redirects_to_the_gateway()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(
            new Uri("/clave/login?loa=substantial&method=clave_movil&returnUrl=%2Fexpedientes", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith(ClaveTestEnvironment.Gateway + "?", location, StringComparison.Ordinal);
        Assert.Contains("SAMLRequest=", location, StringComparison.Ordinal);
        Assert.Contains("Signature=", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_login_endpoint_refuses_a_foreign_return_url()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(
            new Uri("/clave/login?returnUrl=https%3A%2F%2Fevil.example%2Fpanel", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_login_endpoint_refuses_a_level_the_gateway_does_not_offer()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(
            new Uri("/clave/login?loa=low", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_assertion_reaches_the_host_callback()
    {
        await using var host = await TestHost.StartAsync();
        var (requestId, state) = await host.StartLoginAsync("/expedientes", AuthAssuranceLevel.Substantial);
        var pending = host.Pending(requestId, state, "/expedientes", AuthAssuranceLevel.Substantial);
        var assertion = host.Environment.Sign(pending);

        var response = await host.PostAcsAsync(assertion);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/expedientes", response.Headers.Location!.OriginalString);
        Assert.NotNull(host.LastIdentity);
        Assert.Equal("12345678Z", host.LastIdentity!.DocumentId);
        Assert.Equal("MARÍA", host.LastIdentity.GivenName);
    }

    [Fact]
    public async Task An_assertion_with_a_bad_signature_never_reaches_the_callback()
    {
        await using var host = await TestHost.StartAsync();
        var (requestId, state) = await host.StartLoginAsync("/expedientes", AuthAssuranceLevel.Substantial);
        var pending = host.Pending(requestId, state, "/expedientes", AuthAssuranceLevel.Substantial);
        var assertion = host.Environment.Sign(pending, signingCertificate: host.Environment.Attacker);

        var response = await host.PostAcsAsync(assertion);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(host.LastIdentity);
        Assert.Contains("error=federation_failed", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Distinct_failures_share_one_outward_response()
    {
        await using var host = await TestHost.StartAsync();

        // Two completely different internal reasons: a malformed body and an assertion for an
        // audience this entity does not serve.
        var malformed = await host.PostAcsAsync(new SignedAssertion("not-base64", "state"));

        var (requestId, state) = await host.StartLoginAsync("/expedientes", AuthAssuranceLevel.Substantial);
        var pending = host.Pending(requestId, state, "/expedientes", AuthAssuranceLevel.Substantial);
        var wrongAudience = await host.PostAcsAsync(host.Environment.Sign(pending, audience: "https://other.example/metadata"));

        Assert.Equal(malformed.StatusCode, wrongAudience.StatusCode);
        Assert.Equal(
            malformed.Headers.Location!.OriginalString,
            wrongAudience.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_acs_rejects_a_non_form_post()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(new Uri("/clave/acs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task AddClave_uses_the_distributed_store_when_one_is_registered()
    {
        using var environment = new ClaveTestEnvironment();
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddClave(
            options => CopyOptions(environment.Options, options),
            http => http.RoutePrefix = "/saml");

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<DistributedClaveRequestStore>(provider.GetRequiredService<IClaveRequestStore>());
        Assert.IsType<DistributedClaveReplayCache>(provider.GetRequiredService<IClaveReplayCache>());
    }

    [Fact]
    public async Task AddClave_falls_back_to_process_local_state()
    {
        using var environment = new ClaveTestEnvironment();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClave(options => CopyOptions(environment.Options, options));

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryClaveRequestStore>(provider.GetRequiredService<IClaveRequestStore>());
        Assert.IsType<InMemoryClaveReplayCache>(provider.GetRequiredService<IClaveReplayCache>());
    }

    internal static void CopyOptions(ClaveOptions source, ClaveOptions target)
    {
        target.Environment = source.Environment;
        target.EntityId = source.EntityId;
        target.AcsUrl = source.AcsUrl;
        target.SigningCertificate = source.SigningCertificate;
        target.PasarelaSigningCertificate = source.PasarelaSigningCertificate;
        target.RequestedAttributes = source.RequestedAttributes;
        target.RequiredAttributes = source.RequiredAttributes;
        target.DefaultLevel = source.DefaultLevel;
        target.Tenant = source.Tenant;
        target.RequestLifetime = source.RequestLifetime;
        target.ClockSkew = source.ClockSkew;
        target.SubjectKey = source.SubjectKey;
        target.MaxResponseBytes = source.MaxResponseBytes;
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestHost(WebApplication app, ClaveTestEnvironment environment, HttpClient client)
        {
            _app = app;
            Environment = environment;
            Client = client;
        }

        public ClaveTestEnvironment Environment { get; }

        public HttpClient Client { get; }

        public ClaveIdentity? LastIdentity { get; private set; }

        public static async Task<TestHost> StartAsync()
        {
            var environment = new ClaveTestEnvironment();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<TimeProvider>(environment.Clock);
            builder.Services.AddClave(
                options => CopyOptions(environment.Options, options),
                http =>
                {
                    http.RoutePrefix = "/clave";
                    http.FailurePath = "/login-failed";
                });

            var app = builder.Build();
            TestHost? host = null;
            app.MapClave((context, identity) =>
            {
                host!.LastIdentity = identity;
                return Task.FromResult(Results.Redirect(identity.ReturnUrl ?? "/"));
            });

            await app.StartAsync();
            host = new TestHost(app, environment, app.GetTestClient());
            return host;
        }

        public async Task<(string RequestId, string State)> StartLoginAsync(string returnUrl, AuthAssuranceLevel level)
        {
            var service = _app.Services.GetRequiredService<ClaveLoginService>();
            var request = await service.BeginAsync(new AuthFederationRequest
            {
                ProviderRef = "clientes/clave",
                Level = level,
                Method = ClaveMethods.Movil,
                ReturnUrl = returnUrl
            });

            return (request.RequestId, request.State);
        }

        public PendingClaveRequest Pending(
            string requestId,
            string state,
            string? returnUrl,
            AuthAssuranceLevel level) => new()
            {
                RequestId = requestId,
                State = state,
                ReturnUrl = returnUrl,
                Level = level,
                Method = ClaveMethods.Movil,
                Tenant = ClaveTestEnvironment.Tenant,
                CreatedAt = Environment.Clock.Now,
                ExpiresAt = Environment.Clock.Now.AddMinutes(15)
            };

        public Task<HttpResponseMessage> PostAcsAsync(SignedAssertion assertion)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["SAMLResponse"] = assertion.Base64,
                ["RelayState"] = assertion.RelayState
            });

            return Client.PostAsync(new Uri("/clave/acs", UriKind.Relative), form);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            Client.Dispose();
            Environment.Dispose();
        }
    }
}
