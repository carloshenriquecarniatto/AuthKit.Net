namespace AuthKit.Clave;

/// <summary>
/// A login attempt that has been started and is waiting for the gateway to post back.
/// </summary>
/// <remarks>
/// This record is what binds <c>InResponseTo</c> to a request this host actually issued. It holds no
/// document data: nothing sensitive exists yet at this point in the flow.
/// </remarks>
public sealed record PendingClaveRequest
{
    /// <summary>SAML request id sent as <c>ID</c> on the <c>AuthnRequest</c>.</summary>
    public required string RequestId { get; init; }

    /// <summary>Opaque state value, returned by the gateway and echoed to the host callback.</summary>
    public required string State { get; init; }

    /// <summary>Where the user goes after a successful login. Validated against the return URL policy.</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>Level the user asked for.</summary>
    public required AuthAssuranceLevel Level { get; init; }

    /// <summary>Requested method, when the host narrowed the journey.</summary>
    public string? Method { get; init; }

    /// <summary>Tenant that issued the request.</summary>
    public required string Tenant { get; init; }

    /// <summary>When the request was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the request stops being acceptable.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
