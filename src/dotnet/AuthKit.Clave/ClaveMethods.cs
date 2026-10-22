namespace AuthKit.Clave;

/// <summary>
/// Whether the entity is a public administration or a private company. Sent in the eIDAS extension
/// and shown by the gateway, so it is never guessed.
/// </summary>
public enum ClaveServiceProviderType
{
    /// <summary>Public administration.</summary>
    Public,

    /// <summary>Private entity.</summary>
    Private
}

/// <summary>
/// The authentication methods Cl@ve offers. Methods are a request parameter, not a provider:
/// one Cl@ve registration offers all of them.
/// </summary>
/// <remarks>
/// These identifiers match the <c>amr</c> values the gateway reports and the ids the UI uses in its
/// descriptors. The UI never switches on them; see docs/spec-clave.md §8.
/// </remarks>
public static class ClaveMethods
{
    /// <summary>Cl@ve Móvil: a push notification to the citizen's app.</summary>
    public const string Movil = "clave_movil";

    /// <summary>Cl@ve Permanente: a permanent username and password.</summary>
    public const string Permanente = "clave_permanente";

    /// <summary>Cl@ve PIN: a short-lived code.</summary>
    public const string Pin = "clave_pin";

    /// <summary>A recognised electronic certificate, including DNIe.</summary>
    public const string Certificate = "certificate";

    /// <summary>Every method this library knows how to request.</summary>
    public static IReadOnlyList<string> All { get; } = [Movil, Permanente, Pin, Certificate];

    /// <summary>Levels a method can grant.</summary>
    /// <param name="method">Method identifier.</param>
    /// <returns>
    /// The levels the method can satisfy. Certificate authentication can reach high; the password
    /// and PIN journeys stop at substantial.
    /// </returns>
    public static IReadOnlyList<AuthAssuranceLevel> LevelsFor(string method) => method switch
    {
        Certificate => [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High],
        Movil or Permanente or Pin => [AuthAssuranceLevel.Substantial],
        _ => []
    };
}
