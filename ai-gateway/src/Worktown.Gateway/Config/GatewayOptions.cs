using System.Globalization;
using Microsoft.Extensions.Options;

namespace Worktown.Gateway.Config;

/// <summary>
/// Gateway configuration. Read only from flat env-var style keys (GATEWAY_*), never from
/// appsettings.json. Validation errors name the variable, never its value (hard rule 8).
/// </summary>
public sealed class GatewayOptions
{
    public const string UpstreamBaseUrlKey = "GATEWAY_UPSTREAM_BASE_URL";
    public const string UpstreamApiKeyKey = "GATEWAY_UPSTREAM_API_KEY";
    public const string ModelKey = "GATEWAY_MODEL";
    public const string ServiceTokenKey = "GATEWAY_SERVICE_TOKEN";
    public const string UpstreamFlavorKey = "GATEWAY_UPSTREAM_FLAVOR";
    public const string OpenRouterProvidersKey = "GATEWAY_OPENROUTER_PROVIDERS";
    public const string ReasoningEffortKey = "GATEWAY_REASONING_EFFORT";
    public const string TemperatureKey = "GATEWAY_TEMPERATURE";
    public const string UpstreamTimeoutSecondsKey = "GATEWAY_UPSTREAM_TIMEOUT_SECONDS";
    public const string RateLimitPerMinuteKey = "GATEWAY_RATE_LIMIT_PER_MINUTE";
    public const string RateLimitBurstKey = "GATEWAY_RATE_LIMIT_BURST";
    public const string CacheMaxEntriesKey = "GATEWAY_CACHE_MAX_ENTRIES";

    public const string FlavorOpenRouter = "openrouter";
    public const string FlavorOpenAi = "openai";

    public string UpstreamBaseUrl { get; set; } = "";
    public string UpstreamApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string ServiceToken { get; set; } = "";
    public string UpstreamFlavor { get; set; } = FlavorOpenRouter;
    public IReadOnlyList<string> OpenRouterProviders { get; set; } = Array.Empty<string>();
    public string ReasoningEffort { get; set; } = "none";
    public double Temperature { get; set; } = 0.7;
    public int UpstreamTimeoutSeconds { get; set; } = 20;
    public int RateLimitPerMinute { get; set; } = 120;
    public int RateLimitBurst { get; set; } = 30;
    public int CacheMaxEntries { get; set; } = 100_000;

    /// <summary>Parse problems found while loading; reported by <see cref="Validator"/>.</summary>
    internal List<string> LoadErrors { get; } = new();

    /// <summary>Loads values from configuration. Missing optional values keep their defaults.</summary>
    internal void LoadFrom(IConfiguration config)
    {
        UpstreamBaseUrl = config[UpstreamBaseUrlKey] ?? "";
        UpstreamApiKey = config[UpstreamApiKeyKey] ?? "";
        Model = config[ModelKey] ?? "";
        ServiceToken = config[ServiceTokenKey] ?? "";
        UpstreamFlavor = config[UpstreamFlavorKey] is { Length: > 0 } flavor ? flavor : FlavorOpenRouter;
        OpenRouterProviders = (config[OpenRouterProvidersKey] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Present but empty means "omit reasoning"; absent means the default.
        ReasoningEffort = config[ReasoningEffortKey] ?? "none";
        Temperature = ParseDouble(config, TemperatureKey, Temperature);
        UpstreamTimeoutSeconds = ParseInt(config, UpstreamTimeoutSecondsKey, UpstreamTimeoutSeconds);
        RateLimitPerMinute = ParseInt(config, RateLimitPerMinuteKey, RateLimitPerMinute);
        RateLimitBurst = ParseInt(config, RateLimitBurstKey, RateLimitBurst);
        CacheMaxEntries = ParseInt(config, CacheMaxEntriesKey, CacheMaxEntries);
    }

    /// <summary>All validation errors. Messages name variables only, never values.</summary>
    internal IReadOnlyList<string> Validate()
    {
        var errors = ValidatePipeline();
        errors.AddRange(ValidateServiceToken());
        return errors;
    }

    /// <summary>
    /// Spec 056b: the rules of the item-name pipeline (upstream, model, limits); everything but the service token, which
    /// only the web host needs (the offline bake runs the pipeline without it).
    /// </summary>
    internal List<string> ValidatePipeline()
    {
        var errors = new List<string>(LoadErrors);
        if (UpstreamBaseUrl.Length == 0)
            errors.Add($"{UpstreamBaseUrlKey} is required.");
        else if (!Uri.TryCreate(UpstreamBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            errors.Add($"{UpstreamBaseUrlKey} must be an absolute https URL.");
        if (UpstreamApiKey.Length == 0)
            errors.Add($"{UpstreamApiKeyKey} is required.");
        if (Model.Length == 0)
            errors.Add($"{ModelKey} is required.");
        else if (Model.Contains('|') || Model.Any(char.IsWhiteSpace))
            errors.Add($"{ModelKey} must not contain '|' or whitespace.");
        if (UpstreamFlavor != FlavorOpenRouter && UpstreamFlavor != FlavorOpenAi)
            errors.Add($"{UpstreamFlavorKey} must be '{FlavorOpenRouter}' or '{FlavorOpenAi}'.");
        if (double.IsNaN(Temperature) || Temperature < 0 || Temperature > 2)
            errors.Add($"{TemperatureKey} must be between 0 and 2.");
        if (UpstreamTimeoutSeconds < 1 || UpstreamTimeoutSeconds > 120)
            errors.Add($"{UpstreamTimeoutSecondsKey} must be between 1 and 120.");
        if (RateLimitPerMinute < 1)
            errors.Add($"{RateLimitPerMinuteKey} must be at least 1.");
        if (RateLimitBurst < 1)
            errors.Add($"{RateLimitBurstKey} must be at least 1.");
        if (CacheMaxEntries < 1)
            errors.Add($"{CacheMaxEntriesKey} must be at least 1.");
        return errors;
    }

    /// <summary>The web host's service-token rule (spec 006 AC12).</summary>
    internal List<string> ValidateServiceToken()
    {
        var errors = new List<string>();
        if (ServiceToken.Length == 0)
            errors.Add($"{ServiceTokenKey} is required.");
        else if (ServiceToken.Length < 32)
            errors.Add($"{ServiceTokenKey} must be at least 32 characters.");
        return errors;
    }

    private int ParseInt(IConfiguration config, string key, int fallback)
    {
        var raw = config[key];
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        LoadErrors.Add($"{key} must be an integer.");
        return fallback;
    }

    private double ParseDouble(IConfiguration config, string key, double fallback)
    {
        var raw = config[key];
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
        LoadErrors.Add($"{key} must be a number.");
        return fallback;
    }

    /// <summary>Hooks <see cref="ValidatePipeline"/> into the options pipeline (registered by <c>AddItemNamePipeline</c>).</summary>
    internal sealed class PipelineValidator : IValidateOptions<GatewayOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayOptions options)
        {
            var errors = options.ValidatePipeline();
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }

    /// <summary>Hooks <see cref="ValidateServiceToken"/> into the options pipeline (the web host only, with ValidateOnStart).</summary>
    internal sealed class ServiceTokenValidator : IValidateOptions<GatewayOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayOptions options)
        {
            var errors = options.ValidateServiceToken();
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
