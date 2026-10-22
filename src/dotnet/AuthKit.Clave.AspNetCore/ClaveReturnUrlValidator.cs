namespace AuthKit.Clave.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Validates post-login return URLs. An open redirect after a federation round trip is a phishing
/// primitive, so anything that is not provably same-origin (or explicitly allow-listed) is rejected.
/// </summary>
public static class ClaveReturnUrlValidator
{
    /// <summary>Checks a return URL against the policy.</summary>
    /// <param name="returnUrl">Value supplied by the caller.</param>
    /// <param name="policy">Configured policy.</param>
    /// <param name="request">Current request, used for the same-origin comparison.</param>
    /// <param name="allowedHosts">Additional hosts, when the policy allows them.</param>
    /// <returns><see langword="true"/> when the host may redirect there.</returns>
    public static bool IsAllowed(
        string? returnUrl,
        ClaveReturnUrlPolicy policy,
        HttpRequest request,
        IReadOnlyList<string>? allowedHosts = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        // Protocol-relative and backslash forms are classic bypasses: "//evil", "/\evil", and
        // their percent-encoded variants. Decode first so "%5C" and "%2F%2F" cannot slip through.
        var decoded = returnUrl;
        try
        {
            decoded = Uri.UnescapeDataString(returnUrl);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.StartsWith("//", StringComparison.Ordinal)
            || decoded.StartsWith("/\\", StringComparison.Ordinal)
            || decoded.Contains('\\', StringComparison.Ordinal)
            || decoded.Any(char.IsControl))
        {
            return false;
        }

        if (returnUrl.StartsWith('/'))
        {
            return true;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (policy == ClaveReturnUrlPolicy.AllowListedHosts && allowedHosts is not null)
        {
            foreach (var host in allowedHosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Normalises an accepted return URL to a value safe to use in a redirect.</summary>
    /// <param name="returnUrl">Accepted return URL, or <see langword="null"/>.</param>
    /// <param name="fallback">Value used when no return URL was supplied.</param>
    /// <returns>A local path or absolute URL that passed <see cref="IsAllowed"/>.</returns>
    public static string Resolve(string? returnUrl, string fallback = "/") =>
        string.IsNullOrWhiteSpace(returnUrl) ? fallback : returnUrl;
}
