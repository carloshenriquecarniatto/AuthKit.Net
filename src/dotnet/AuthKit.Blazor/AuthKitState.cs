using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AuthKit.Blazor;

/// <summary>Where the browser keeps an AuthKit session.</summary>
public enum AuthSessionStorageMode
{
    /// <summary>Memory only. The session dies with the circuit or page reload.</summary>
    Memory,

    /// <summary><c>sessionStorage</c>. Survives reloads, dies with the tab.</summary>
    BrowserSession
}

/// <summary>Process/circuit-local session store. The safe default.</summary>
public sealed class InMemoryAuthSessionStore : IAuthSessionStore
{
    private AuthSession? _session;

    /// <inheritdoc />
    public ValueTask<AuthSession?> LoadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_session);

    /// <inheritdoc />
    public ValueTask SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        _session = session;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Stores the session in <c>sessionStorage</c> through a JavaScript module.
/// </summary>
/// <remarks>
/// Any script running on the origin can read this value, so treat XSS as equivalent to session
/// theft. Mitigations: short access-token lifetime, refresh-token rotation, a strict CSP, and
/// dependency scanning (docs/threat-model.md, T-05). On server-side Blazor use
/// <see cref="InMemoryAuthSessionStore"/> and keep the refresh token on the server.
/// </remarks>
public sealed class BrowserSessionStore : IAuthSessionStore, IAsyncDisposable, IDisposable
{
    private const string StorageKey = "authkit.session";

    private readonly IJSRuntime _js;
    private readonly InMemoryAuthSessionStore _fallback = new();
    private IJSObjectReference? _module;

    public BrowserSessionStore(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var raw = await InvokeAsync<string?>("getItem", StorageKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw))
        {
            return await _fallback.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var session = JsonSerializer.Deserialize<AuthSession>(raw);
            if (session is null || session.IsExpired(DateTimeOffset.UtcNow))
            {
                await ClearAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            return session;
        }
        catch (JsonException)
        {
            // A corrupt or tampered payload is treated as "signed out", never as an error.
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        await _fallback.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        await InvokeVoidAsync("setItem", StorageKey, JsonSerializer.Serialize(session)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fallback.ClearAsync(cancellationToken).ConfigureAwait(false);
        await InvokeVoidAsync("removeItem", StorageKey).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync().ConfigureAwait(false);
            _module = null;
        }
    }

    /// <summary>
    /// Synchronous disposal for DI containers that do not await. Releasing the module reference
    /// is enough: the JavaScript module itself is owned by the runtime.
    /// </summary>
    public void Dispose() => _module = null;

    private async ValueTask<T?> InvokeAsync<T>(string identifier, params object?[] args)
    {
        try
        {
            // ES module import needs no script tag in the host page.
            _module ??= await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/AuthKit.Blazor/authkit.js");

            return await _module.InvokeAsync<T>(identifier, args).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // Prerendering, JS disabled, or a disconnected circuit: degrade to memory.
            return default;
        }
    }

    private async ValueTask InvokeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/AuthKit.Blazor/authkit.js");

            await _module.InvokeVoidAsync(identifier, args).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // Keep the in-memory fallback as the source of truth for this circuit.
        }
    }
}

/// <summary>
/// Session facade for Blazor components. Holds no tokens of its own and survives the
/// Server-to-WebAssembly transition in Auto hosting by persisting only non-sensitive fields.
/// </summary>
public sealed class AuthKitState : IDisposable
{
    private readonly IAuthProvider _provider;
    private readonly PersistentComponentState? _persistent;
    private PersistingComponentStateSubscription _subscription;

    public AuthKitState(IAuthProvider provider, IServiceProvider services)
    {
        _provider = provider;
        _persistent = services.GetService<PersistentComponentState>();
        _provider.StateChanged += HandleStateChanged;

        if (_persistent is not null)
        {
            _subscription = _persistent.RegisterOnPersisting(PersistSnapshot);
        }
    }

    /// <summary>Current session, if any.</summary>
    public AuthSession? Session => _provider.CurrentSession;

    /// <summary>Whether a session exists.</summary>
    public bool IsAuthenticated => Session is not null;

    /// <summary>Email of the signed-in user, when available.</summary>
    public string? Email => Session?.User.Email;

    /// <summary>Raised whenever authentication state changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Asks the provider to restore its persisted session. Call once from the root component.
    /// </summary>
    public async Task<AuthResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_provider is IInitializableAuthProvider initializable)
        {
            var result = await initializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
            Changed?.Invoke();
            return result;
        }

        return AuthResult.Continue(AuthNextStep.None);
    }

    /// <summary>Refreshes the session.</summary>
    public Task<AuthResult> RefreshAsync(CancellationToken cancellationToken = default) =>
        _provider.RefreshSessionAsync(cancellationToken);

    /// <summary>Signs out.</summary>
    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        _provider.SignOutAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.StateChanged -= HandleStateChanged;
        _subscription.Dispose();
    }

    private void HandleStateChanged(object? sender, AuthStateChangedEventArgs args) => Changed?.Invoke();

    private Task PersistSnapshot()
    {
        _persistent?.PersistAsJson("authkit", new AuthKitSnapshot(IsAuthenticated, Email));
        return Task.CompletedTask;
    }

    /// <summary>Non-sensitive state carried from server prerender to the WebAssembly runtime.</summary>
    public sealed record AuthKitSnapshot(bool Authenticated, string? Email);
}

/// <summary>Base class for every AuthKit form component.</summary>
public abstract class AuthKitComponentBase : ComponentBase, IDisposable
{
    private AuthKitOptions? _options;

    [Inject]
    protected IAuthProvider Provider { get; set; } = default!;

    [Inject]
    protected IServiceProvider Services { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>Receives failures so the host can show a toast or redirect.</summary>
    [Parameter]
    public EventCallback<AuthError?> OnError { get; set; }

    /// <summary>Set to <see langword="false"/> to supply your own stylesheet.</summary>
    [Parameter]
    public bool IncludeStyles { get; set; } = true;

    protected AuthKitOptions Options => _options ??= Services.GetService<AuthKitOptions>() ?? new AuthKitOptions();

    protected IAuthMessages Messages => Options.Messages;

    /// <summary>Theme rendered as CSS custom properties on the component root.</summary>
    protected string ThemeVars => Options.Theme.ToCssVariables();

    /// <summary>Sanitised logo URL, or <see langword="null"/> when absent or unsafe.</summary>
    protected string? Logo => Options.Theme.SafeLogoUrl();

    protected AuthFlow Flow { get; } = new();

    protected AuthError? Error => Flow.Error;

    protected bool Busy => Flow.IsBusy;

    protected override void OnInitialized() => Flow.Changed += HandleFlowChanged;

    protected async Task<AuthResult> ExecuteAsync(Func<CancellationToken, Task<AuthResult>> operation)
    {
        var result = await Flow.RunAsync(operation);

        if (!result.Ok && OnError.HasDelegate)
        {
            await OnError.InvokeAsync(result.Error);
        }

        return result;
    }

    protected string Text(AuthMessageKey key, string? provider = null) => Messages.Resolve(key, provider);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Flow.Changed -= HandleFlowChanged;
        GC.SuppressFinalize(this);
    }

    private void HandleFlowChanged() => _ = InvokeAsync(StateHasChanged);
}
