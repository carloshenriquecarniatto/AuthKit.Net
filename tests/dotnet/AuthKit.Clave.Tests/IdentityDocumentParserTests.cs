using Xunit;

namespace AuthKit.Clave.Tests;

public sealed class IdentityDocumentParserTests
{
    [Fact]
    public void Parses_a_dni_with_a_country_prefix()
    {
        Assert.True(IdentityDocumentParser.TryParse("ES/ES/12345678Z", out var document));

        Assert.Equal(AuthDocumentType.Dni, document.Type);
        Assert.Equal("12345678Z", document.Value);
        Assert.Equal("ES/ES/12345678Z", document.Raw);
    }

    [Fact]
    public void Parses_the_legacy_esp_prefix()
    {
        Assert.True(IdentityDocumentParser.TryParse("ESP/ES/12345678Z", out var document));

        Assert.Equal("12345678Z", document.Value);
    }

    [Fact]
    public void Normalises_case_and_separators()
    {
        Assert.True(IdentityDocumentParser.TryParse("  es/es/1234 5678-z ", out var document));

        Assert.Equal("12345678Z", document.Value);
    }

    [Fact]
    public void Parses_a_nie_starting_with_x()
    {
        Assert.True(IdentityDocumentParser.TryParse("X1234567L", out var document));

        Assert.Equal(AuthDocumentType.Nie, document.Type);
        Assert.Equal("X1234567L", document.Value);
    }

    [Fact]
    public void Parses_a_nie_starting_with_y_or_z()
    {
        Assert.True(IdentityDocumentParser.TryParse("Y1234567X", out var ya));
        Assert.True(IdentityDocumentParser.TryParse("Z1234567R", out var zeta));

        Assert.Equal(AuthDocumentType.Nie, ya.Type);
        Assert.Equal(AuthDocumentType.Nie, zeta.Type);
    }

    [Fact]
    public void Rejects_a_dni_with_a_wrong_check_letter()
    {
        Assert.False(IdentityDocumentParser.TryParse("12345678A", out _));
    }

    [Fact]
    public void Rejects_a_nie_with_a_wrong_check_letter()
    {
        Assert.False(IdentityDocumentParser.TryParse("X1234567A", out _));
    }

    [Fact]
    public void Accepts_a_cif_without_checksum_validation()
    {
        Assert.True(IdentityDocumentParser.TryParse("B12345678", out var document));

        Assert.Equal(AuthDocumentType.Cif, document.Type);
    }

    [Fact]
    public void Rejects_empty_and_unrecognisable_values()
    {
        Assert.False(IdentityDocumentParser.TryParse(null, out _));
        Assert.False(IdentityDocumentParser.TryParse("   ", out _));
        Assert.False(IdentityDocumentParser.TryParse("not-a-document", out _));
        Assert.False(IdentityDocumentParser.TryParse("ES/ES/", out _));
    }

    [Theory]
    [InlineData("12345678", 'Z')]
    [InlineData("00000000", 'T')]
    [InlineData("X1234567", 'L')]
    [InlineData("Y1234567", 'X')]
    [InlineData("Z1234567", 'R')]
    public void Computes_the_expected_check_letter(string body, char expected)
    {
        Assert.Equal(expected, IdentityDocumentParser.CheckLetter(body));
    }

    [Fact]
    public void Returns_null_for_a_malformed_body()
    {
        Assert.Null(IdentityDocumentParser.CheckLetter("1234"));
        Assert.Null(IdentityDocumentParser.CheckLetter("Q1234567"));
        Assert.Null(IdentityDocumentParser.CheckLetter(null));
    }
}
