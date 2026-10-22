namespace AuthKit.Clave;

/// <summary>
/// The two Cl@ve gateway environments. They are separate federations with separate entity
/// registrations and separate signing trust anchors, so a certificate valid in one is meaningless
/// in the other.
/// </summary>
public enum ClaveEnvironment
{
    /// <summary>Servicios Estables, also called preproduction.</summary>
    Preproduction,

    /// <summary>The production gateway.</summary>
    Production
}

/// <summary>
/// Gateway endpoints. The <c>Proxy2</c> path is the Cl@ve 2.0 node that speaks the eIDAS profile.
/// </summary>
/// <remarks>
/// URLs are part of the entity's CAID registration. Confirm them against the current integration
/// manual before a release: a wrong host produces a redirect that looks correct and fails at the
/// gateway with an opaque error.
/// </remarks>
public static class ClaveEndpoints
{
    /// <summary>Returns the gateway endpoint for an environment.</summary>
    /// <param name="environment">Target environment.</param>
    /// <returns>Absolute gateway URL.</returns>
    public static Uri For(ClaveEnvironment environment) => environment switch
    {
        ClaveEnvironment.Preproduction => new Uri("https://se-pasarela.clave.gob.es/Proxy2", UriKind.Absolute),
        ClaveEnvironment.Production => new Uri("https://pasarela.clave.gob.es/Proxy2", UriKind.Absolute),
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown Cl@ve environment.")
    };
}
