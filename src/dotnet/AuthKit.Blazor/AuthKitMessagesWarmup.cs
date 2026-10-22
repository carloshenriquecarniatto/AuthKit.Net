using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuthKit.Blazor;

/// <summary>
/// Loads a deferred message catalog during host startup, before the first component renders.
/// It is a no-op when no catalog source was registered.
/// </summary>
internal sealed class AuthKitMessagesWarmup : IHostedService
{
    private readonly IServiceProvider _services;

    public AuthKitMessagesWarmup(IServiceProvider services) => _services = services;

    public Task StartAsync(CancellationToken cancellationToken) =>
        _services.GetService<DeferredAuthMessages>()?.EnsureLoadedAsync(cancellationToken)
        ?? Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
