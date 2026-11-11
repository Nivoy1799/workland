using System.Text.Json.Serialization;

namespace Worktown.Gateway.Api;

/// <summary>
/// Error body: <c>{ "error": { "code": "...", "message": "..." } }</c>. The message is a short
/// fixed text per code and never contains upstream output.
/// </summary>
public sealed record ErrorResponse([property: JsonPropertyName("error")] ErrorResponse.ErrorBody Error)
{
    public const string InvalidRequest = "invalid_request";
    public const string Unauthorized = "unauthorized";
    public const string TreeMismatch = "tree_mismatch";
    public const string ContentRejected = "content_rejected";
    public const string RateLimited = "rate_limited";
    public const string UpstreamInvalidOutput = "upstream_invalid_output";
    public const string UpstreamError = "upstream_error";
    public const string UpstreamTimeout = "upstream_timeout";

    public sealed record ErrorBody(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);

    /// <summary>Builds the error body with the fixed message for <paramref name="code"/>.</summary>
    public static ErrorResponse For(string code) => new(new ErrorBody(code, MessageFor(code)));

    /// <summary>Fixed, short message per error code.</summary>
    public static string MessageFor(string code) => code switch
    {
        InvalidRequest => "The request is invalid.",
        Unauthorized => "Missing or invalid service token.",
        TreeMismatch => "The cached item for this hash has a different canonical tree.",
        ContentRejected => "Generated content was rejected by moderation.",
        RateLimited => "Rate limit exceeded.",
        UpstreamInvalidOutput => "The upstream model returned invalid output.",
        UpstreamError => "The upstream model request failed.",
        UpstreamTimeout => "The upstream model timed out.",
        _ => "Error.",
    };
}
