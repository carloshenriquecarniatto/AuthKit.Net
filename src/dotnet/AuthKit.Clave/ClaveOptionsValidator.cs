using System.Security.Cryptography.X509Certificates;

namespace AuthKit.Clave;

/// <summary>
/// Validates <see cref="ClaveOptions"/> before the application starts.
/// </summary>
/// <remarks>
/// Every message names the field and the fix. This is the component that replaces the trial-and-error
/// of a CAID registration: a wrong entity id or a certificate without a private key produces an
/// opaque gateway error that costs an afternoon to diagnose.
/// </remarks>
public static class ClaveOptionsValidator
{
    private const int MinimumSubjectKeyBytes = 32;
    private const int CertificateWarningDays = 30;

    /// <summary>Validates the options and returns every problem found.</summary>
    /// <param name="options">Options to validate.</param>
    /// <param name="timeProvider">Clock used for certificate expiry, for tests.</param>
    /// <returns>Human-readable messages; empty when the configuration is usable.</returns>
    public static IReadOnlyList<string> Validate(ClaveOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var errors = new List<string>();

        ValidateEntityId(options.EntityId, errors);
        ValidateAcsUrl(options.AcsUrl, errors);
        ValidateGateway(options, errors);
        ValidateSigningCertificate(options.SigningCertificate, now, errors);
        ValidatePasarelaCertificate(options.PasarelaSigningCertificate, errors);
        ValidateAttributes(options, errors);
        ValidateTenant(options.Tenant, errors);
        ValidateLevel(options.DefaultLevel, errors);
        ValidateDurations(options, errors);
        ValidateSubjectKey(options.SubjectKey, errors);

        return errors;
    }

    /// <summary>Validates the options and throws when anything is wrong.</summary>
    /// <param name="options">Options to validate.</param>
    /// <param name="timeProvider">Clock used for certificate expiry, for tests.</param>
    /// <exception cref="AuthKitConfigurationException">Thrown with every problem, one per line.</exception>
    public static void ValidateAndThrow(ClaveOptions options, TimeProvider? timeProvider = null)
    {
        var errors = Validate(options, timeProvider);
        if (errors.Count > 0)
        {
            throw new AuthKitConfigurationException(
                "Cl@ve configuration is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateEntityId(string? entityId, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            errors.Add("EntityId is required: use the exact value registered in the CAID.");
            return;
        }

        if (!Uri.TryCreate(entityId, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("EntityId must be an absolute https URL.");
            return;
        }

        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            errors.Add("EntityId must not contain a query string or fragment: the CAID compares it literally.");
        }
    }

    private static void ValidateAcsUrl(string? acsUrl, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(acsUrl))
        {
            errors.Add("AcsUrl is required: the gateway posts the assertion there.");
            return;
        }

        if (!Uri.TryCreate(acsUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("AcsUrl must be an absolute https URL.");
        }
    }

    private static void ValidateGateway(ClaveOptions options, List<string> errors)
    {
        if (options.GatewayEndpoint is not null && options.GatewayEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("GatewayEndpoint must be an absolute https URL: Cl@ve is never called over plain HTTP.");
        }

        if (options.ExpectedIssuer is not null
            && (!Uri.TryCreate(options.ExpectedIssuer, UriKind.Absolute, out var issuer) || issuer.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("ExpectedIssuer must be an absolute https URL when it is set.");
        }

        if (options.ProviderName is { Length: > 0 } name && name.Length > 128)
        {
            errors.Add("ProviderName must be at most 128 characters: the gateway displays it to the citizen.");
        }

        if (options.MaxResponseBytes is < 64 * 1024 or > 8 * 1024 * 1024)
        {
            errors.Add("MaxResponseBytes must be between 64 KB and 8 MB.");
        }
    }

    private static void ValidateSigningCertificate(X509Certificate2? certificate, DateTimeOffset now, List<string> errors)
    {
        if (certificate is null)
        {
            errors.Add("SigningCertificate is required: the AuthnRequest must be signed with the entity certificate.");
            return;
        }

        if (!certificate.HasPrivateKey)
        {
            errors.Add("SigningCertificate has no private key: a public-only certificate cannot sign the AuthnRequest.");
        }

        var remaining = certificate.NotAfter.ToUniversalTime() - now.UtcDateTime;
        if (remaining <= TimeSpan.Zero)
        {
            errors.Add($"SigningCertificate expired on {certificate.NotAfter:yyyy-MM-dd}.");
        }
        else if (remaining <= TimeSpan.FromDays(CertificateWarningDays))
        {
            errors.Add(
                $"SigningCertificate expires in {remaining.Days} days ({certificate.NotAfter:yyyy-MM-dd}); renew it in the CAID before deploying.");
        }

        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is not null && !keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            errors.Add("SigningCertificate key usage does not allow digital signatures: this certificate is not a signing certificate.");
        }
    }

    private static void ValidatePasarelaCertificate(X509Certificate2? certificate, List<string> errors)
    {
        if (certificate is null)
        {
            errors.Add("PasarelaSigningCertificate is required: responses are validated against a pinned key, never an ambient trust store.");
        }
    }

    private static void ValidateAttributes(ClaveOptions options, List<string> errors)
    {
        if (options.RequestedAttributes is null || options.RequestedAttributes.Count == 0)
        {
            errors.Add("RequestedAttributes is empty: at minimum PersonIdentifier must be requested.");
            return;
        }

        if (!options.RequestedAttributes.Contains(ClaveAttributes.PersonIdentifier, StringComparer.Ordinal))
        {
            errors.Add("RequestedAttributes must contain PersonIdentifier: without it there is no subject.");
        }

        if (options.RequiredAttributes is null)
        {
            errors.Add("RequiredAttributes must not be null: use an empty list to mark nothing as required.");
            return;
        }

        foreach (var required in options.RequiredAttributes)
        {
            if (!options.RequestedAttributes.Contains(required, StringComparer.Ordinal))
            {
                errors.Add($"RequiredAttributes contains '{required}', which is not in RequestedAttributes.");
            }
        }

        if (options.RequiredAttributes.Contains(ClaveAttributes.DateOfBirth, StringComparer.Ordinal))
        {
            errors.Add(
                "DateOfBirth must not be required: citizens whose Cl@ve profile has no birth date would be unable to log in.");
        }
    }

    private static void ValidateTenant(string? tenant, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            errors.Add("Tenant is required: it is part of the derived subject and of the audit trail.");
            return;
        }

        if (tenant.Length > 64 || !char.IsAsciiLetterOrDigit(tenant[0]) || char.IsAsciiLetterUpper(tenant[0]))
        {
            errors.Add("Tenant must start with a lowercase letter or a digit and be at most 64 characters.");
            return;
        }

        foreach (var c in tenant)
        {
            if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterLower(c) && c is not ('.' or '_' or '-'))
            {
                errors.Add("Tenant may only contain lowercase letters, digits, '.', '_' and '-' (pattern ^[a-z0-9][a-z0-9._-]{0,63}$).");
                return;
            }
        }
    }

    private static void ValidateLevel(AuthAssuranceLevel level, List<string> errors)
    {
        if (level == AuthAssuranceLevel.Low)
        {
            errors.Add("DefaultLevel 'Low' is not offered by the Cl@ve 2.0 node: use Substantial or High.");
        }
    }

    private static void ValidateDurations(ClaveOptions options, List<string> errors)
    {
        if (options.RequestLifetime <= TimeSpan.Zero || options.RequestLifetime > TimeSpan.FromHours(1))
        {
            errors.Add("RequestLifetime must be greater than zero and at most one hour.");
        }

        if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > TimeSpan.FromMinutes(10))
        {
            errors.Add("ClockSkew must be between zero and ten minutes.");
        }
    }

    private static void ValidateSubjectKey(byte[]? subjectKey, List<string> errors)
    {
        if (subjectKey is null)
        {
            errors.Add("SubjectKey is required: the opaque subject is an HMAC of the document.");
            return;
        }

        if (subjectKey.Length < MinimumSubjectKeyBytes)
        {
            errors.Add($"SubjectKey must be at least {MinimumSubjectKeyBytes} bytes.");
        }
    }
}
