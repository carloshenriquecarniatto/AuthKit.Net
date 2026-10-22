namespace AuthKit.Clave.AspNetCore;

/// <summary>How a post-login <c>returnUrl</c> is validated.</summary>
public enum ClaveReturnUrlPolicy
{
    /// <summary>Only the request's own origin. The default, and the only safe choice for most hosts.</summary>
    SameOrigin,

    /// <summary>Same origin plus the hosts in <see cref="ClaveAspNetCoreOptions.AllowedReturnUrlHosts"/>.</summary>
    AllowListedHosts
}

/// <summary>
/// Route and browser-facing settings. Kept separate from <see cref="ClaveOptions"/> because they
/// describe the host's HTTP surface, not the CAID registration.
/// </summary>
public sealed class ClaveAspNetCoreOptions
{
    /// <summary>Prefix every endpoint is mapped under. Defaults to <c>/clave</c>.</summary>
    public string RoutePrefix { get; set; } = "/clave";

    /// <summary>Where the UI sends the user to start a login. Used by descriptors, not by routing.</summary>
    public string LoginPath { get; set; } = "/clave/login";

    /// <summary>
    /// Canonical provider reference this instance serves, for example <c>clientes/clave</c>.
    /// Carried into the pending request so logs and audit records identify the right provider.
    /// </summary>
    public string ProviderRef { get; set; } = "default/clave";

    /// <summary>Where a failed login lands. One path for every failure reason (R-13).</summary>
    public string FailurePath { get; set; } = "/";

    /// <summary>Policy applied to <c>returnUrl</c>. Defaults to same origin.</summary>
    public ClaveReturnUrlPolicy ReturnUrlPolicy { get; set; } = ClaveReturnUrlPolicy.SameOrigin;

    /// <summary>Additional hosts accepted when <see cref="ReturnUrlPolicy"/> allows them.</summary>
    public IReadOnlyList<string> AllowedReturnUrlHosts { get; set; } = [];

    /// <summary>Maximum accepted ACS form body, in bytes. Defaults to 1 MB.</summary>
    public long MaxAcsBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// How long the browser may cache the metadata document. Metadata changes only when the
    /// certificate or the ACS URL changes, so a short cache is enough.
    /// </summary>
    public TimeSpan MetadataCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}
