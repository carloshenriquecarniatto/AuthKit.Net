using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthKit.Core.Tests;

public sealed class DeferredMessagesTests
{
    [Fact]
    public void Before_loading_it_serves_the_fallback()
    {
        var messages = new DeferredAuthMessages(new FakeSource("""{"signIn":"Entrar"}"""));

        Assert.False(messages.IsLoaded);
        Assert.Equal("Sign In", messages.Resolve(AuthMessageKey.SignIn));
    }

    [Fact]
    public async Task EnsureLoaded_swaps_the_catalog_once()
    {
        var source = new FakeSource("""{"signIn":"Entrar"}""");
        var messages = new DeferredAuthMessages(source);

        await messages.EnsureLoadedAsync();
        await messages.EnsureLoadedAsync();

        Assert.True(messages.IsLoaded);
        Assert.Equal("Entrar", messages.Resolve(AuthMessageKey.SignIn));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_load()
    {
        var source = new FakeSource("""{"signIn":"Entrar"}""") { Delay = TimeSpan.FromMilliseconds(50) };
        var messages = new DeferredAuthMessages(source);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => messages.EnsureLoadedAsync()));

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task A_failed_load_can_be_retried()
    {
        var source = new FakeSource("""{"signIn":"Entrar"}""") { FailFirst = true };
        var messages = new DeferredAuthMessages(source);

        await Assert.ThrowsAsync<AuthKitConfigurationException>(() => messages.EnsureLoadedAsync());
        Assert.False(messages.IsLoaded);

        await messages.EnsureLoadedAsync();

        Assert.True(messages.IsLoaded);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Registration_resolves_the_catalog_from_the_container()
    {
        var services = new ServiceCollection();
        services.AddAuthKitMessages(new FakeSource("""{"signIn":"Entrar"}"""));

        await using var provider = services.BuildServiceProvider();
        var messages = provider.GetRequiredService<IAuthMessages>();

        Assert.Equal("Sign In", messages.Resolve(AuthMessageKey.SignIn));

        await provider.EnsureAuthKitMessagesAsync();

        Assert.Equal("Entrar", messages.Resolve(AuthMessageKey.SignIn));
    }

    [Fact]
    public async Task Ensure_is_a_no_op_without_a_source()
    {
        var services = new ServiceCollection();

        await using var provider = services.BuildServiceProvider();
        await provider.EnsureAuthKitMessagesAsync();
    }

    private sealed class FakeSource : IAuthMessagesSource
    {
        private readonly string _json;
        private int _calls;

        public FakeSource(string json) => _json = json;

        public int Calls => _calls;

        public TimeSpan Delay { get; init; }

        public bool FailFirst { get; init; }

        public async ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _calls);

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            if (FailFirst && attempt == 1)
            {
                throw new AuthKitConfigurationException("network down");
            }

            return AuthMessages.FromJson(_json);
        }
    }
}
