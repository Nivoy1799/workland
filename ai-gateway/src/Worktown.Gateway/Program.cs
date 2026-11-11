using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Worktown.Gateway.Api;
using Worktown.Gateway.Auth;
using Worktown.Gateway.Cache;
using Worktown.Gateway.Config;
using Worktown.Gateway.Moderation;
using Worktown.Gateway.Pipeline;
using Worktown.Gateway.Upstream;
using Worktown.Gateway.Validation;

const string ServerIdHeader = "X-Worktown-Server-Id";
const string ItemNamePolicy = "per-server";

var builder = WebApplication.CreateBuilder(args);

// Configuration: GATEWAY_* keys only (env vars in production, UseSetting in tests).
builder.Services.AddOptions<GatewayOptions>()
    .Configure<IConfiguration>((options, config) => options.LoadFrom(config))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptions.Validator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => SchemaValidator.ForItemNameV1());
builder.Services.AddSingleton<IContentCache>(sp => new InMemoryContentCache(
    sp.GetRequiredService<IOptions<GatewayOptions>>().Value.CacheMaxEntries,
    sp.GetRequiredService<ILogger<InMemoryContentCache>>()));
builder.Services.AddSingleton<IModerator>(_ => new PlaceholderDenyListModerator());
builder.Services.AddScoped<ItemNamePipeline>();

// Typed upstream client. Its built-in request logging is removed, so nothing about upstream
// traffic reaches the logs. Timeouts are per attempt inside the client. The response body is
// capped (a larger one is an error) and redirects are not followed, so the configured host is
// the only one ever called; a 3xx is a final upstream error.
builder.Services.AddHttpClient<IUpstreamClient, OpenAiCompatibleClient>(OpenAiCompatibleClient.HttpClientName)
    .ConfigureHttpClient(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.MaxResponseContentBufferSize = OpenAiCompatibleClient.MaxResponseBytes;
    })
    .ConfigurePrimaryHttpMessageHandler(OpenAiCompatibleClient.CreatePrimaryHandler)
    .RemoveAllLoggers();

// Token bucket per server id. Requests without a valid token or server id are not counted
// (no limiter): the endpoint answers them with 401/400, so they cannot use up a server's quota.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.AddPolicy(ItemNamePolicy, http =>
    {
        var options = http.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
        var serverId = http.Request.Headers[ServerIdHeader].ToString();
        if (!ServiceTokenFilter.IsAuthorized(http.Request, options.ServiceToken) || !RequestValidator.IsValidServerId(serverId))
            return RateLimitPartition.GetNoLimiter("unlimited");
        return RateLimitPartition.GetTokenBucketLimiter(serverId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.RateLimitBurst,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1.0 / options.RateLimitPerMinute),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    limiter.OnRejected = async (context, ct) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : 60;
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        await response.WriteAsJsonAsync(ErrorResponse.For(ErrorResponse.RateLimited), ct).ConfigureAwait(false);
    };
});

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));

app.MapPost("/v1/content/item-name", async (HttpContext http, ItemNamePipeline pipeline, CancellationToken ct) =>
    {
        var body = await ReadBodyAsync(http.Request, RequestValidator.MaxBodyBytes, ct).ConfigureAwait(false);
        var request = body is null ? null : RequestValidator.TryDeserialize(body);
        if (request is null)
            return Results.Json(ErrorResponse.For(ErrorResponse.InvalidRequest), statusCode: StatusCodes.Status400BadRequest);
        var serverId = http.Request.Headers[ServerIdHeader].ToString();
        var outcome = await pipeline.RunAsync(request, serverId, ct).ConfigureAwait(false);
        return outcome.Response is { } ok
            ? Results.Json(ok)
            : Results.Json(ErrorResponse.For(outcome.Code), statusCode: outcome.StatusCode);
    })
    .AddEndpointFilter<ServiceTokenFilter>()
    .RequireRateLimiting(ItemNamePolicy);

app.Run();

// Reads at most maxBytes; returns null when the body is larger.
static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken ct)
{
    if (request.ContentLength > maxBytes) return null;
    var buffer = new byte[maxBytes + 1];
    var total = 0;
    int read;
    while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false)) > 0)
        total += read;
    return total > maxBytes ? null : buffer[..total];
}

/// <summary>Entry point type, public so WebApplicationFactory can host it in tests.</summary>
public partial class Program {}
