using System.Text;

namespace AuthKit;

/// <summary>
/// Design tokens shared by every AuthKit renderer. The same token names are used in the Angular
/// client so both implementations can be themed from one source.
/// </summary>
/// <remarks>
/// Values are emitted as CSS custom properties on the web, which makes them a potential style
/// injection vector. <see cref="ToCssVariables"/> strips characters that could terminate a
/// declaration, and rejects <c>url()</c>; see docs/threat-model.md (T-09).
/// </remarks>
public sealed record AuthTheme
{
    /// <summary>Accent colour for primary buttons and links.</summary>
    public string Primary { get; init; } = "#4f46e5";

    /// <summary>Page background.</summary>
    public string Background { get; init; } = "#ffffff";

    /// <summary>Card or form surface.</summary>
    public string Surface { get; init; } = "#ffffff";

    /// <summary>Primary text colour.</summary>
    public string Text { get; init; } = "#111827";

    /// <summary>Secondary text and placeholder colour.</summary>
    public string TextMuted { get; init; } = "#6b7280";

    /// <summary>Input and separator border colour.</summary>
    public string Border { get; init; } = "#d1d5db";

    /// <summary>Error colour.</summary>
    public string Danger { get; init; } = "#dc2626";

    /// <summary>Corner radius, as a CSS length.</summary>
    public string Radius { get; init; } = "8px";

    /// <summary>Base spacing unit, as a CSS length.</summary>
    public string Spacing { get; init; } = "16px";

    /// <summary>Font stack.</summary>
    public string FontFamily { get; init; } = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";

    /// <summary>Optional logo URL rendered above the form. Only https and relative URLs are allowed.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>The default light theme.</summary>
    public static AuthTheme Default { get; } = new();

    /// <summary>
    /// Renders the theme as a CSS custom property block. Unsafe values are neutralised rather
    /// than throwing, so a bad theme can never break the page.
    /// </summary>
    public string ToCssVariables()
    {
        var builder = new StringBuilder(512);
        Append(builder, "--ak-primary", Primary);
        Append(builder, "--ak-background", Background);
        Append(builder, "--ak-surface", Surface);
        Append(builder, "--ak-text", Text);
        Append(builder, "--ak-text-muted", TextMuted);
        Append(builder, "--ak-border", Border);
        Append(builder, "--ak-danger", Danger);
        Append(builder, "--ak-radius", Radius);
        Append(builder, "--ak-spacing", Spacing);
        Append(builder, "--ak-font-family", FontFamily);
        return builder.ToString();
    }

    /// <summary>Returns the logo URL when it is safe to render, otherwise <see langword="null"/>.</summary>
    public string? SafeLogoUrl()
    {
        if (string.IsNullOrWhiteSpace(LogoUrl))
        {
            return null;
        }

        // A relative URL must not be able to become protocol-relative after browser
        // normalisation: "/\evil.example" is parsed as "//evil.example" by some browsers.
        if (LogoUrl.Contains('\\', StringComparison.Ordinal) ||
            LogoUrl.Any(char.IsControl))
        {
            return null;
        }

        if (Uri.TryCreate(LogoUrl, UriKind.Relative, out _) &&
            !LogoUrl.StartsWith("//", StringComparison.Ordinal) &&
            !LogoUrl.Contains(':', StringComparison.Ordinal))
        {
            return LogoUrl;
        }

        return Uri.TryCreate(LogoUrl, UriKind.Absolute, out var absolute) &&
               absolute.Scheme is "https" or "http"
            ? absolute.ToString()
            : null;
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append(':').Append(Sanitize(value)).Append(';');
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "inherit";
        }

        // Strip anything that could close the declaration, open a block, or introduce a
        // remote fetch. This is defence in depth: the attribute is HTML-encoded by Blazor too.
        var cleaned = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is ';' or '{' or '}' or '<' or '>' or '"' or '\'' or '\\')
            {
                continue;
            }

            cleaned.Append(character);
        }

        var result = cleaned.ToString();
        return result.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
               result.Contains("expression(", StringComparison.OrdinalIgnoreCase)
            ? "inherit"
            : result;
    }
}
