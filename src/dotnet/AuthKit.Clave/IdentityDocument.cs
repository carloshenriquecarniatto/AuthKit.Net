using System.Globalization;

namespace AuthKit.Clave;

/// <summary>
/// A normalised identity document.
/// </summary>
/// <remarks>
/// The raw value the gateway sends looks like <c>ES/ES/12345678Z</c> and is preserved for audit.
/// The normalised value is what the host compares against its own records. Neither is ever used as a
/// primary key: see <see cref="ClaveIdentity.Subject"/>.
/// </remarks>
public sealed record IdentityDocument
{
    /// <summary>Detected document type.</summary>
    public required AuthDocumentType Type { get; init; }

    /// <summary>Normalised value: uppercase, no separators, no country prefix.</summary>
    public required string Value { get; init; }

    /// <summary>Value exactly as the gateway sent it.</summary>
    public required string Raw { get; init; }
}

/// <summary>
/// Parses and normalises the <c>PersonIdentifier</c> attribute.
/// </summary>
/// <remarks>
/// This is the single most error-prone part of a Cl@ve integration: the prefix varies
/// (<c>ESP/ES/</c> in older deployments, <c>ES/ES/</c> in current ones), case is not guaranteed and
/// the checksum is the only thing that distinguishes a real DNI from a plausible string.
/// </remarks>
public static class IdentityDocumentParser
{
    private const string LetterMap = "TRWAGMYFPDXBNJZSQVHLCKE";

    private static readonly string[] KnownPrefixes = ["ESP/ES/", "ES/ES/"];

    /// <summary>
    /// Parses a <c>PersonIdentifier</c> value.
    /// </summary>
    /// <param name="value">Raw attribute value, optionally with a country prefix.</param>
    /// <param name="document">Normalised document when parsing succeeds.</param>
    /// <param name="validateChecksum">
    /// Whether DNI/NIE check letters must match. Hosts that must accept legacy records can turn
    /// this off, but the control letter is the only thing separating a real document from a
    /// plausible string, so the default is on.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a recognisable document with a valid checksum.
    /// DNI and NIE are checksum-validated; CIF is accepted unvalidated; anything else fails.
    /// </returns>
    public static bool TryParse(string? value, out IdentityDocument document, bool validateChecksum = true)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        var normalised = StripPrefix(raw);
        normalised = RemoveSeparators(normalised).ToUpperInvariant();

        if (normalised.Length == 0)
        {
            return false;
        }

        var type = Detect(normalised);
        if (type == AuthDocumentType.Unknown)
        {
            return false;
        }

        if (validateChecksum && type is AuthDocumentType.Dni or AuthDocumentType.Nie && !ChecksumMatches(normalised))
        {
            return false;
        }

        document = new IdentityDocument { Type = type, Value = normalised, Raw = raw };
        return true;
    }

    /// <summary>Returns the check letter for a DNI or NIE numeric body.</summary>
    /// <param name="body">Eight digits, or a NIE prefix letter followed by seven digits.</param>
    /// <returns>The expected letter, or <see langword="null"/> when the body is malformed.</returns>
    public static char? CheckLetter(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length != 8)
        {
            return null;
        }

        var digits = body;
        if (char.IsAsciiLetter(body[0]))
        {
            var replacement = char.ToUpperInvariant(body[0]) switch
            {
                'X' => '0',
                'Y' => '1',
                'Z' => '2',
                _ => '\0'
            };

            if (replacement == '\0')
            {
                return null;
            }

            digits = replacement + body[1..];
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return LetterMap[number % 23];
    }

    private static string StripPrefix(string value)
    {
        foreach (var prefix in KnownPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..];
            }
        }

        return value;
    }

    private static string RemoveSeparators(string value)
    {
        if (value.AsSpan().IndexOfAny(' ', '-', '_') < 0)
        {
            return value;
        }

        return string.Concat(value.Where(static c => c is not (' ' or '-' or '_')));
    }

    private static AuthDocumentType Detect(string value)
    {
        // DNI: 8 digits + letter.
        if (value.Length == 9 && char.IsAsciiDigit(value[0]) && IsAllDigits(value.AsSpan(0, 8)) && char.IsAsciiLetter(value[8]))
        {
            return AuthDocumentType.Dni;
        }

        // NIE: X/Y/Z + 7 digits + letter.
        if (value.Length == 9
            && (char.ToUpperInvariant(value[0]) is 'X' or 'Y' or 'Z')
            && IsAllDigits(value.AsSpan(1, 7))
            && char.IsAsciiLetter(value[8]))
        {
            return AuthDocumentType.Nie;
        }

        // CIF: letter + 7 digits + digit or letter.
        if (value.Length == 9
            && char.IsAsciiLetter(value[0])
            && IsAllDigits(value.AsSpan(1, 7))
            && char.IsAsciiLetterOrDigit(value[8]))
        {
            return AuthDocumentType.Cif;
        }

        return AuthDocumentType.Unknown;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChecksumMatches(string value)
    {
        var body = value[..8];
        var letter = char.ToUpperInvariant(value[8]);
        var expected = CheckLetter(body);
        return expected is not null && expected == letter;
    }
}
