using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthKit.Supabase.Tests;

public sealed class MessageRegistrationTests
{
    private const string Json = """{"signIn":"Entrar"}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Catalog_registration_order_does_not_matter(bool messagesFirst)
    {
        var services = new ServiceCollection();

        if (messagesFirst)
        {
            services.AddAuthKitMessages(new InlineSource());
            services.AddAuthKitSupabase(Configure);
        }
        else
        {
            services.AddAuthKitSupabase(Configure);
            services.AddAuthKitMessages(new InlineSource());
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<AuthKitOptions>();

        Assert.IsType<DeferredAuthMessages>(options.Messages);
        Assert.Equal("Sign In", options.Messages.Resolve(AuthMessageKey.SignIn));
    }

    [Fact]
    public async Task A_registered_catalog_replaces_the_default_after_warmup()
    {
        var services = new ServiceCollection();
        services.AddAuthKitSupabase(Configure);
        services.AddAuthKitMessages(new InlineSource());

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.EnsureAuthKitMessagesAsync();

        var options = scope.ServiceProvider.GetRequiredService<AuthKitOptions>();
        Assert.Equal("Entrar", options.Messages.Resolve(AuthMessageKey.SignIn));
    }

    [Fact]
    public void An_explicit_ui_message_table_still_wins_without_a_source()
    {
        var services = new ServiceCollection();
        services.AddAuthKitSupabase(Configure, ui => ui.Messages = AuthMessages.FromJson("""{"signIn":"Custom"}"""));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<AuthKitOptions>();
        Assert.Equal("Custom", options.Messages.Resolve(AuthMessageKey.SignIn));
    }

    private static void Configure(SupabaseAuthOptions options)
    {
        options.Url = "https://project.supabase.co";
        options.AnonKey = "anon-key-for-tests";
    }

    private sealed class InlineSource : IAuthMessagesSource
    {
        public ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuthMessages.FromJson(Json));
    }
}
