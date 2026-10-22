namespace AuthKit;

/// <summary>
/// Options shared by every AuthKit renderer. Provider-specific settings live in the provider's
/// own options type (for example <c>SupabaseAuthOptions</c>).
/// </summary>
public sealed class AuthKitOptions
{
    /// <summary>
    /// URL the provider should send the user back to after an email confirmation, magic link,
    /// recovery email or OAuth round trip. On the web this is normally an absolute URL; on a
    /// native host it is a deep link.
    /// </summary>
    public string? RedirectTo { get; set; }

    /// <summary>Design tokens applied to every component.</summary>
    public AuthTheme Theme { get; set; } = AuthTheme.Default;

    /// <summary>Message table. Replace to localise or reword the UI.</summary>
    public IAuthMessages Messages { get; set; } = AuthMessages.English;

    /// <summary>Minimum password length enforced by the forms.</summary>
    public int MinimumPasswordLength { get; set; } = AuthValidation.DefaultMinimumPasswordLength;

    /// <summary>
    /// When <see langword="true"/>, pressing Enter submits the form. Mirrors the
    /// <c>enableAutomaticFormSubmission</c> flag of supabase_auth_ui.
    /// </summary>
    public bool EnableAutomaticFormSubmission { get; set; } = true;

    /// <summary>Social providers offered when none is passed explicitly to the component.</summary>
    public IReadOnlyList<string> SocialProviders { get; set; } = [];
}
