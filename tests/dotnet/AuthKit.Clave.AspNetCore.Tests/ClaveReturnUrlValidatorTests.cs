using AuthKit.Clave.AspNetCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AuthKit.Clave.AspNetCore.Tests;

/// <summary>
/// Open redirect after a federation round trip is a phishing primitive (T-16). The validator is
/// the only place that decides, so every bypass shape is asserted here.
/// </summary>
public sealed class ClaveReturnUrlValidatorTests
{
    [Theory]
    [InlineData("/expedientes", true)]
    [InlineData("https://sede.example.es/expedientes", true)]
    [InlineData("https://SEDE.example.es/expedientes", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("/%5Cevil.example", false)]
    [InlineData("/%2F%2Fevil.example", false)]
    [InlineData("https://evil.example/expedientes", false)]
    [InlineData("http://sede.example.es.evil.example/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("expedientes", false)]
    public void Return_urls_are_validated_against_the_policy(string? returnUrl, bool expected)
    {
        var request = Request("sede.example.es");

        var allowed = ClaveReturnUrlValidator.IsAllowed(
            returnUrl,
            ClaveReturnUrlPolicy.SameOrigin,
            request);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public void Explicitly_allowed_hosts_are_accepted_only_when_the_policy_says_so()
    {
        var request = Request("sede.example.es");
        var hosts = new[] { "app.example.es" };

        Assert.True(ClaveReturnUrlValidator.IsAllowed(
            "https://app.example.es/panel",
            ClaveReturnUrlPolicy.AllowListedHosts,
            request,
            hosts));

        Assert.False(ClaveReturnUrlValidator.IsAllowed(
            "https://app.example.es/panel",
            ClaveReturnUrlPolicy.SameOrigin,
            request,
            hosts));
    }

    [Fact]
    public void A_relative_url_is_accepted_and_resolved_to_the_fallback_when_absent()
    {
        var request = Request("sede.example.es");

        Assert.True(ClaveReturnUrlValidator.IsAllowed("/panel", ClaveReturnUrlPolicy.SameOrigin, request));
        Assert.Equal("/", ClaveReturnUrlValidator.Resolve(null));
        Assert.Equal("/panel", ClaveReturnUrlValidator.Resolve("/panel"));
    }

    private static HttpRequest Request(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Scheme = "https";
        return context.Request;
    }
}
