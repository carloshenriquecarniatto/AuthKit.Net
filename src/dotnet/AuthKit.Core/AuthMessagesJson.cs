using System.Text.Json;

namespace AuthKit;

/// <summary>
/// Adapter: a message catalog decoded from a JSON object.
/// </summary>
/// <remarks>
/// Shape: <c>{ "signIn": "Entrar", "passwordTooShort": "… {min} …" }</c>. Keys are matched
/// case-insensitively against <see cref="AuthMessageKey"/>; unknown keys are ignored and missing
/// keys fall back. A bundle is a plain data file, so it can live in the app bundle, on a CDN or be
/// served by your own API.
/// </remarks>
public sealed class JsonAuthMessages : DictionaryAuthMessages
{
    /// <summary>Largest accepted payload. A message catalog is a few kilobytes.</summary>
    public const int MaximumPayloadBytes = 1024 * 1024;

    /// <summary>Decodes a JSON catalog.</summary>
    /// <exception cref="AuthKitConfigurationException">The JSON is invalid or too large.</exception>
    public JsonAuthMessages(string json, IAuthMessages? fallback = null)
        : base(Parse(json), fallback)
    {
    }

    /// <summary>Decodes a JSON catalog from a stream.</summary>
    public static async Task<IAuthMessages> FromStreamAsync(
        Stream stream,
        IAuthMessages? fallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumPayloadBytes)
            {
                throw new AuthKitConfigurationException(
                    $"The message catalog exceeds {MaximumPayloadBytes} bytes. Serve only the strings the UI needs.");
            }

            buffer.Write(chunk, 0, read);
        }

        return new JsonAuthMessages(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), fallback);
    }

    /// <summary>Parses the JSON object into typed keys. Exposed for tooling and tests.</summary>
    public static IReadOnlyDictionary<AuthMessageKey, string> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
        {
            throw new AuthKitConfigurationException(
                $"The message catalog exceeds {MaximumPayloadBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new AuthKitConfigurationException("The message catalog is not valid JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AuthKitConfigurationException(
                    "The message catalog must be a JSON object mapping keys to strings.");
            }

            var result = new Dictionary<AuthMessageKey, string>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse<AuthMessageKey>(property.Name, ignoreCase: true, out var key))
                {
                    continue;
                }

                result[key] = property.Value.GetString() ?? string.Empty;
            }

            return result;
        }
    }
}

/// <summary>
/// Port for loading a catalog from somewhere: memory, the bundle, the file system, an API.
/// Implement it to plug your own localization backend (database, CDN, feature flags).
/// </summary>
public interface IAuthMessagesSource
{
    /// <summary>Returns the catalog, falling back to English for missing keys.</summary>
    ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adapter: loads a catalog from a file on disk (app bundle, mounted volume).</summary>
public sealed class FileAuthMessagesSource : IAuthMessagesSource
{
    private readonly string _path;
    private readonly IAuthMessages _fallback;

    public FileAuthMessagesSource(string path, IAuthMessages? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _fallback = fallback ?? AuthMessages.English;
    }

    /// <inheritdoc />
    public async ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonAuthMessages
                .FromStreamAsync(stream, _fallback, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new AuthKitConfigurationException(
                $"Could not read the message catalog at '{_path}'.", exception);
        }
    }
}

/// <summary>Adapter: fetches a catalog from an HTTP(S) URL.</summary>
public sealed class HttpAuthMessagesSource : IAuthMessagesSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _url;
    private readonly IAuthMessages _fallback;

    public HttpAuthMessagesSource(HttpClient httpClient, Uri url, IAuthMessages? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(url);

        if (url.Scheme is not ("http" or "https"))
        {
            throw new AuthKitConfigurationException(
                "The message catalog URL must use http or https.");
        }

        _httpClient = httpClient;
        _url = url;
        _fallback = fallback ?? AuthMessages.English;
    }

    /// <inheritdoc />
    public async ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new AuthKitConfigurationException(
                $"The message catalog at '{_url}' returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JsonAuthMessages
            .FromStreamAsync(stream, _fallback, cancellationToken)
            .ConfigureAwait(false);
    }
}
