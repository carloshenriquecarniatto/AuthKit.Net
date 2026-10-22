using Microsoft.Extensions.DependencyInjection;
using SupabaseClient = global::Supabase.Client;

namespace AuthKit.Supabase;

/// <summary>Dependency injection helpers for the Supabase provider.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Supabase Auth as the AuthKit provider.
    /// </summary>
    /// <remarks>
    /// Everything is scoped on purpose. In Blazor Server a scope maps to a circuit, so two users
    /// can never share a client or a session. In WebAssembly scoped behaves like singleton, which
    /// is correct because there is a single user per browser tab.
    /// </remarks>
    /// <param name="services">Service collection to extend.</param>
    /// <param name="configure">Supabase connection settings.</param>
    /// <param name="configureUi">Optional AuthKit UI settings (theme, messages, redirect URL).</param>
    public static IServiceCollection AddAuthKitSupabase(
        this IServiceCollection services,
        Action<SupabaseAuthOptions> configure,
        Action<AuthKitOptions>? configureUi = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var supabaseOptions = new SupabaseAuthOptions();
        configure(supabaseOptions);
        supabaseOptions.Validate();

        var uiOptions = new AuthKitOptions
        {
            RedirectTo = supabaseOptions.RedirectTo,
            SocialProviders = supabaseOptions.SocialProviders
        };
        configureUi?.Invoke(uiOptions);

        services.AddSingleton(supabaseOptions);

        // The catalog can come from AddAuthKitMessages(...) or from the UI callback. Resolving it
        // here keeps registration order irrelevant.
        services.AddSingleton(provider =>
        {
            var messages = provider.GetService<IAuthMessages>() ?? uiOptions.Messages;

            return new AuthKitOptions
            {
                RedirectTo = uiOptions.RedirectTo,
                Theme = uiOptions.Theme,
                Messages = messages,
                MinimumPasswordLength = uiOptions.MinimumPasswordLength,
                EnableAutomaticFormSubmission = uiOptions.EnableAutomaticFormSubmission,
                SocialProviders = uiOptions.SocialProviders
            };
        });

        services.AddScoped(_ => new SupabaseClient(
            supabaseOptions.Url,
            supabaseOptions.AnonKey,
            new global::Supabase.SupabaseOptions
            {
                AutoRefreshToken = supabaseOptions.AutoRefreshToken,
                AutoConnectRealtime = false,
                HttpClient = supabaseOptions.HttpClient
            }));

        services.AddScoped<IOAuthStateStore, InMemoryOAuthStateStore>();
        services.AddScoped<SupabaseAuthProvider>(provider => new SupabaseAuthProvider(
            provider.GetRequiredService<SupabaseClient>(),
            provider.GetRequiredService<SupabaseAuthOptions>(),
            provider.GetRequiredService<IOAuthStateStore>(),
            provider.GetService<IAuthSessionStore>()));
        services.AddScoped<IAuthProvider>(provider => provider.GetRequiredService<SupabaseAuthProvider>());

        return services;
    }
}
