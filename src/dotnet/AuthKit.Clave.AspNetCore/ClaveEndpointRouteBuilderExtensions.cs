using AuthKit.Clave;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthKit.Clave.AspNetCore;

/// <summary>
/// Maps the three endpoints the gateway and CAID need. The host owns the routes; the library
/// supplies the handlers.
/// </summary>
public static class ClaveEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>{prefix}/metadata</c>, <c>{prefix}/login</c> and <c>{prefix}/acs</c>.
    /// </summary>
    /// <param name="endpoints">Endpoint builder.</param>
    /// <param name="onAuthenticated">
    /// Called after an assertion validates. This is where the host decides what a session is:
    /// <c>ctx.SignInAsync</c>, an Identity external login, or nothing at all. AuthKit never signs
    /// anyone in.
    /// </param>
    /// <param name="prefix">Route prefix. Defaults to <c>/clave</c>.</param>
    /// <returns>The route group, for adding metadata.</returns>
    public static RouteGroupBuilder MapClave(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, ClaveIdentity, Task<IResult>> onAuthenticated,
        string prefix = "/clave")
    {
        return MapClave(endpoints, prefix, onAuthenticated);
    }

    /// <summary>
    /// Maps the endpoints under an explicit prefix, matching the <c>app.MapClave("/saml", …)</c>
    /// form used in the specification.
    /// </summary>
    /// <param name="endpoints">Endpoint builder.</param>
    /// <param name="prefix">Route prefix, for example <c>/saml</c>.</param>
    /// <param name="onAuthenticated">Host callback invoked with the validated identity.</param>
    /// <returns>The route group, for adding metadata.</returns>
    public static RouteGroupBuilder MapClave(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        Func<HttpContext, ClaveIdentity, Task<IResult>> onAuthenticated)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(onAuthenticated);

        var normalized = string.IsNullOrWhiteSpace(prefix) ? "/clave" : prefix;
        if (!normalized.StartsWith('/'))
        {
            throw new AuthKitConfigurationException("The Cl@ve route prefix must start with '/'.");
        }

        normalized = normalized.TrimEnd('/');
        WarnAboutProcessLocalState(endpoints);

        var group = endpoints.MapGroup(normalized);
        group.MapGet("/metadata", HandleMetadataAsync);
        group.MapGet("/login", HandleLoginAsync);
        group.MapPost("/acs", (HttpContext context, ClaveLoginService service, ClaveAspNetCoreOptions http, ILoggerFactory loggers) =>
            HandleAcsAsync(context, service, http, loggers.CreateLogger("AuthKit.Clave.Acs"), onAuthenticated));

        return group;
    }

    private static MetadataResult HandleMetadataAsync(ClaveLoginService service, ClaveAspNetCoreOptions http)
    {
        var xml = service.BuildMetadata();
        var content = Results.Content(xml, ClaveMetadataBuilder.ContentType, System.Text.Encoding.UTF8, StatusCodes.Status200OK);
        return new MetadataResult(content, http.MetadataCacheDuration);
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpContext context,
        ClaveLoginService service,
        ClaveAspNetCoreOptions http,
        ClaveOptions options)
    {
        var query = context.Request.Query;
        var returnUrl = query["returnUrl"].ToString();

        if (!ClaveReturnUrlValidator.IsAllowed(returnUrl, http.ReturnUrlPolicy, context.Request, http.AllowedReturnUrlHosts))
        {
            return Results.BadRequest();
        }

        if (!TryParseLevel(query["loa"].ToString(), options.DefaultLevel, out var level))
        {
            return Results.BadRequest();
        }

        var method = query["method"].ToString();
        var forceAuthn = string.Equals(query["prompt"].ToString(), "login", StringComparison.OrdinalIgnoreCase);

        try
        {
            var request = await service.BeginAsync(
                new AuthFederationRequest
                {
                    ProviderRef = http.ProviderRef,
                    Level = level,
                    Method = string.IsNullOrWhiteSpace(method) ? null : method,
                    ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl
                },
                forceAuthn,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Redirect(request.RedirectUrl);
        }
        catch (AuthKitConfigurationException)
        {
            // A host-authored link is wrong, not the user's fault. Never echo the reason.
            return Results.BadRequest();
        }
    }

    private static async Task<IResult> HandleAcsAsync(
        HttpContext context,
        ClaveLoginService service,
        ClaveAspNetCoreOptions http,
        ILogger logger,
        Func<HttpContext, ClaveIdentity, Task<IResult>> onAuthenticated)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        if (context.Request.ContentLength.HasValue
            && context.Request.ContentLength.Value > http.MaxAcsBodyBytes)
        {
            return FailureRedirect(http, AuthErrorCode.FederationFailed);
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var samlResponse = form["SAMLResponse"].ToString();
        var relayState = form["RelayState"].ToString();

        ClaveValidationResult result;
        try
        {
            result = await service.CompleteAsync(samlResponse, relayState, context.RequestAborted).ConfigureAwait(false);
        }
        catch (ClaveStateUnavailableException exception)
        {
            ClaveLog.StateUnavailable(logger, exception);
            return FailureRedirect(http, AuthErrorCode.FederationFailed);
        }

        if (!result.Ok || result.Identity is null)
        {
            // The reason stays server-side: a browser that can distinguish "bad signature" from
            // "expired" is an oracle. See R-13.
            ClaveLog.AssertionRejected(logger, result.Reason, result.StatusCode ?? "<none>");
            return FailureRedirect(http, ClaveErrorMapper.ToErrorCode(result.Reason));
        }

        ClaveLog.AssertionAccepted(logger, result.Identity.Tenant, result.Identity.Level, result.Identity.Method);

        return await onAuthenticated(context, result.Identity).ConfigureAwait(false);
    }

    private static IResult FailureRedirect(ClaveAspNetCoreOptions http, AuthErrorCode code)
    {
        var reason = AuthContractValues.ErrorCodes[code];
        var location = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(http.FailurePath, "error", reason);
        return Results.Redirect(location);
    }

    private static bool TryParseLevel(string value, AuthAssuranceLevel fallback, out AuthAssuranceLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = fallback;
            return true;
        }

        if (AuthAssuranceLevels.TryParseAcrValue(value, out level))
        {
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out level))
        {
            return level != AuthAssuranceLevel.Low || fallback == AuthAssuranceLevel.Low;
        }

        return false;
    }

    private static void WarnAboutProcessLocalState(IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        var environment = services.GetService<IHostEnvironment>();
        if (environment is not null && !environment.IsProduction())
        {
            return;
        }

        var store = services.GetService<IClaveRequestStore>();
        if (store is null || store is InMemoryClaveRequestStore)
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger("AuthKit.Clave");
            if (logger is not null)
            {
                ClaveLog.ProcessLocalStateInProduction(logger);
            }
        }
    }

    /// <summary>Result that also sets cache headers on the metadata response.</summary>
    private sealed class MetadataResult : IResult
    {
        private readonly IResult _inner;
        private readonly TimeSpan _duration;

        public MetadataResult(IResult inner, TimeSpan duration)
        {
            _inner = inner;
            _duration = duration;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = $"public, max-age={(int)_duration.TotalSeconds}";
            return _inner.ExecuteAsync(httpContext);
        }
    }
}
