using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Cache;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Pipeline;
using Worktown.Gateway.Upstream;
using Worktown.Gateway.Validation;

namespace Worktown.Gateway;

/// <summary>
/// Spec 056b: the spec 006 item-name pipeline's services, shared by the web host (<c>Program.cs</c>) and the offline
/// name bake (<c>tools/NameBake</c>): options from GATEWAY_* keys with the pipeline rules (the service-token rule is the
/// web host's), the schema validator, the in-memory cache, the moderator, the pipeline and the typed upstream client.
/// </summary>
public static class GatewayServices
{
    /// <summary>
    /// Adds the pipeline. Options are read from the container's <see cref="IConfiguration"/>; <paramref name="configuration"/>
    /// is registered as that only when none is (a web host already has its own).
    /// </summary>
    public static IServiceCollection AddItemNamePipeline(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(configuration);
        services.AddLogging();

        // Configuration: GATEWAY_* keys only (env vars in production, UseSetting in tests).
        services.AddOptions<GatewayOptions>()
            .Configure<IConfiguration>((options, config) => options.LoadFrom(config));
        services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptions.PipelineValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => SchemaValidator.ForItemNameV1());
        services.AddSingleton<IContentCache>(sp => new InMemoryContentCache(
            sp.GetRequiredService<IOptions<GatewayOptions>>().Value.CacheMaxEntries,
            sp.GetRequiredService<ILogger<InMemoryContentCache>>()));
        services.AddSingleton<IModerator>(_ => new PlaceholderDenyListModerator());
        services.AddScoped<ItemNamePipeline>();

        // Typed upstream client. Its built-in request logging is removed, so nothing about upstream
        // traffic reaches the logs. Timeouts are per attempt inside the client. The response body is
        // capped (a larger one is an error) and redirects are not followed, so the configured host is
        // the only one ever called; a 3xx is a final upstream error.
        services.AddHttpClient<IUpstreamClient, OpenAiCompatibleClient>(OpenAiCompatibleClient.HttpClientName)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = OpenAiCompatibleClient.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(OpenAiCompatibleClient.CreatePrimaryHandler)
            .RemoveAllLoggers();
        return services;
    }
}
