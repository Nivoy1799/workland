using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Upstream;

namespace Worktown.Gateway.Tests;

/// <summary>
/// Hosts the gateway in memory with test settings (via UseSetting, the same keys as the env
/// vars), the stub upstream as the typed client's primary handler, a test moderator and a
/// capturing logger at Trace level. No network, no real key.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const string ServiceToken = "test-service-token-not-a-secret-0001";
    public const string ApiKey = "test-upstream-key-not-a-secret-0001";
    public const string BaseUrl = "https://upstream.test/v1";
    public const string Model = "qwen/test-model";
    public const string ServerId = "server-1";
    public const string FriesHash = "2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3";
    public const string FriesTree = "fry(temp=hot,time=medium;cut(shape=sticks;potato))";

    public GatewayFactory(IDictionary<string, string>? overrides = null)
    {
        Settings = new Dictionary<string, string>
        {
            [GatewayOptions.UpstreamBaseUrlKey] = BaseUrl,
            [GatewayOptions.UpstreamApiKeyKey] = ApiKey,
            [GatewayOptions.ModelKey] = Model,
            [GatewayOptions.ServiceTokenKey] = ServiceToken,
            [GatewayOptions.UpstreamFlavorKey] = GatewayOptions.FlavorOpenRouter,
            [GatewayOptions.OpenRouterProvidersKey] = "",
            [GatewayOptions.ReasoningEffortKey] = "none",
            [GatewayOptions.TemperatureKey] = "0.7",
            [GatewayOptions.UpstreamTimeoutSecondsKey] = "20",
            [GatewayOptions.RateLimitPerMinuteKey] = "6000",
            [GatewayOptions.RateLimitBurstKey] = "1000",
            [GatewayOptions.CacheMaxEntriesKey] = "1000",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides) Settings[key] = value;
        }
    }

    public Dictionary<string, string> Settings { get; }

    public StubUpstreamHandler Stub { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public IModerator Moderator { get; set; } = new PlaceholderDenyListModerator(new[] { "forbiddenword" });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Every key is set explicitly (empty means missing), so machine env vars never leak in.
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(OpenAiCompatibleClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Stub);
            services.AddSingleton(Moderator);
        });
    }

    /// <summary>A client with the service token and a server id header.</summary>
    public HttpClient CreateAuthedClient(string serverId = ServerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken);
        client.DefaultRequestHeaders.Add("X-Worktown-Server-Id", serverId);
        return client;
    }

    /// <summary>The spec 005 fries fixture as a request body.</summary>
    public static object FriesBody(string? tree = null) => new
    {
        itemHash = FriesHash,
        canonicalTree = tree ?? FriesTree,
        fallbackName = "crispy fried potato sticks",
        tags = new[] { "fried" },
    };

    public static Task<HttpResponseMessage> PostItemNameAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/v1/content/item-name", body);
}
