namespace AuthKit.Supabase;

/// <summary>Configuration for <see cref="SupabaseAuthProvider"/>.</summary>
public sealed class SupabaseAuthOptions
{
    /// <summary>Project URL, for example <c>https://abcdefgh.supabase.co</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The public <c>anon</c> key. This key is not a secret, but it must never be replaced by the
    /// service role key in a client application.
    /// </summary>
    public string AnonKey { get; set; } = string.Empty;

    /// <summary>Default redirect URL for emails and OAuth. Components can override it per call.</summary>
    public string? RedirectTo { get; set; }

    /// <summary>Keep the access token refreshed in the background.</summary>
    public bool AutoRefreshToken { get; set; } = true;

    /// <summary>Social providers advertised through <see cref="IAuthProvider.Capabilities"/>.</summary>
    public IReadOnlyList<string> SocialProviders { get; set; } = [];

    /// <summary>
    /// Session persistence. When <see langword="null"/>, sessions live only for the lifetime of
    /// the provider instance.
    /// </summary>
    public IAuthSessionStore? SessionStore { get; set; }

    /// <summary>Optional HTTP client, used by tests and by hosts that pool connections.</summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Validates the configuration and throws a single, actionable exception.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new AuthKitConfigurationException(
                "SupabaseAuthOptions.Url is required. Set it to your project URL, for example https://abcdefgh.supabase.co.");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
        {
            throw new AuthKitConfigurationException(
                $"SupabaseAuthOptions.Url '{Url}' is not an absolute URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new AuthKitConfigurationException(
                "SupabaseAuthOptions.Url must not contain credentials. Remove the user:password part.");
        }

        var isLoopback = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            throw new AuthKitConfigurationException(
                "SupabaseAuthOptions.Url must use https. Plain http is only accepted for loopback hosts in local development.");
        }

        if (string.IsNullOrWhiteSpace(AnonKey))
        {
            throw new AuthKitConfigurationException(
                "SupabaseAuthOptions.AnonKey is required. Use the anon (public) key, never the service role key.");
        }

        // A JWT with the service_role claim would bypass row level security if shipped to a client.
        if (LooksLikeServiceRoleKey(AnonKey))
        {
            throw new AuthKitConfigurationException(
                "SupabaseAuthOptions.AnonKey appears to be a service role key. Shipping it to a client would bypass row level security.");
        }
    }

    private static bool LooksLikeServiceRoleKey(string key)
    {
        var parts = key.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

            using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));

            return document.RootElement.TryGetProperty("role", out var role) &&
                   role.ValueKind == System.Text.Json.JsonValueKind.String &&
                   string.Equals(role.GetString()?.Trim(), "service_role", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
