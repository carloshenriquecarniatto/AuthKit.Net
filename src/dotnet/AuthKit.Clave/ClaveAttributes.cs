namespace AuthKit.Clave;

/// <summary>
/// eIDAS natural-person attribute identifiers, as published by the Spanish node.
/// </summary>
/// <remarks>
/// These URIs are the <c>Name</c> of every <c>eidas:RequestedAttribute</c>. A typo does not fail at
/// the gateway: it silently returns fewer attributes, so the values are constants and never strings
/// the host types by hand.
/// </remarks>
public static class ClaveAttributes
{
    /// <summary>National identifier, returned as <c>ES/ES/12345678Z</c>.</summary>
    public const string PersonIdentifier = "http://eidas.europa.eu/attributes/naturalperson/PersonIdentifier";

    /// <summary>Given name.</summary>
    public const string GivenName = "http://eidas.europa.eu/attributes/naturalperson/CurrentGivenName";

    /// <summary>Family name.</summary>
    public const string FamilyName = "http://eidas.europa.eu/attributes/naturalperson/CurrentFamilyName";

    /// <summary>Date of birth.</summary>
    public const string DateOfBirth = "http://eidas.europa.eu/attributes/naturalperson/DateOfBirth";
}
