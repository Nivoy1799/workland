namespace Worktown.Gateway.Upstream;

/// <summary>Outcome of one upstream chat-completions attempt.</summary>
public abstract record UpstreamResult
{
    private UpstreamResult() { }

    /// <summary>2xx response. Content or finish reason is null when the envelope was malformed.</summary>
    public sealed record Ok(string? Content, string? FinishReason) : UpstreamResult;

    public sealed record Timeout : UpstreamResult;

    public sealed record HttpError(int Status) : UpstreamResult
    {
        /// <summary>5xx and 429 are worth one more attempt; other 4xx are not.</summary>
        public bool IsRetryable => Status >= 500 || Status == 429;
    }

    public sealed record NetworkError : UpstreamResult;
}
