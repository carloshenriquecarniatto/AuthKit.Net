namespace AuthKit.Blazor;

/// <summary>Kind of input rendered for an extra sign-up metadata field.</summary>
public enum AuthMetadataFieldKind
{
    /// <summary>Single-line text input.</summary>
    Text,

    /// <summary>Checkbox that produces <c>true</c> or <c>false</c>.</summary>
    Boolean
}

/// <summary>
/// Describes an additional sign-up field, mirroring <c>MetaDataField</c> and
/// <c>BooleanMetaDataField</c> from supabase_auth_ui.
/// </summary>
public sealed record AuthMetadataField
{
    /// <summary>Metadata key sent to the provider.</summary>
    public required string Key { get; init; }

    /// <summary>Label rendered next to the input.</summary>
    public string? Label { get; init; }

    /// <summary>Input kind.</summary>
    public AuthMetadataFieldKind Kind { get; init; } = AuthMetadataFieldKind.Text;

    /// <summary>Whether the field must be filled before submitting.</summary>
    public bool Required { get; init; }

    /// <summary>Placeholder text for text inputs.</summary>
    public string? Placeholder { get; init; }
}
