namespace Worktown.Gateway.Moderation;

/// <summary>Moderation verdict. <see cref="ReasonCode"/> is a short code, never the matched text.</summary>
public sealed record ModerationResult(bool Allowed, string? ReasonCode)
{
    public static ModerationResult Allow { get; } = new(true, null);

    public static ModerationResult Reject(string reasonCode) => new(false, reasonCode);
}
