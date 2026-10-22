using Microsoft.Extensions.Logging;

namespace AuthKit.Clave.AspNetCore;

/// <summary>
/// Source-generated log messages. Structured logging with generated delegates keeps the hot path
/// allocation-free and avoids interpolating values that may be PII.
/// </summary>
internal static partial class ClaveLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Cl@ve assertion rejected. Reason={Reason}, Status={Status}.")]
    public static partial void AssertionRejected(ILogger logger, ClaveFailureReason reason, string status);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Cl@ve assertion accepted for tenant {Tenant}, level {Level}, method {Method}.")]
    public static partial void AssertionAccepted(ILogger logger, string? tenant, AuthAssuranceLevel level, string method);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Cl@ve state store unavailable; failing closed.")]
    public static partial void StateUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Cl@ve is using process-local request and replay state in Production. With more than one "
            + "instance, logins fail intermittently and replay protection is per instance. Register a "
            + "distributed store.")]
    public static partial void ProcessLocalStateInProduction(ILogger logger);
}
