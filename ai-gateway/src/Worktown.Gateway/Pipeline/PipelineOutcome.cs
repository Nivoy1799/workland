using Worktown.Gateway.Api;

namespace Worktown.Gateway.Pipeline;

/// <summary>Result of <see cref="ItemNamePipeline.RunAsync"/>: a response or an error code with its HTTP status.</summary>
public sealed record PipelineOutcome(int StatusCode, ItemNameResponse? Response, string? ErrorCode)
{
    /// <summary>Outcome code used in logs: "ok" or the error code.</summary>
    public string Code => ErrorCode ?? "ok";

    public static PipelineOutcome Success(ItemNameResponse response) => new(StatusCodes.Status200OK, response, null);

    public static PipelineOutcome Error(string code) => new(StatusFor(code), null, code);

    /// <summary>HTTP status per error code (spec 006 table).</summary>
    public static int StatusFor(string code) => code switch
    {
        ErrorResponse.InvalidRequest => StatusCodes.Status400BadRequest,
        ErrorResponse.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorResponse.TreeMismatch => StatusCodes.Status409Conflict,
        ErrorResponse.ContentRejected => StatusCodes.Status422UnprocessableEntity,
        ErrorResponse.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorResponse.UpstreamInvalidOutput => StatusCodes.Status502BadGateway,
        ErrorResponse.UpstreamError => StatusCodes.Status502BadGateway,
        ErrorResponse.UpstreamTimeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError,
    };
}
