namespace Worktown.Game.Diagnostics
{
    /// <summary>
    /// Spec 049: where crash log entries go. Only <see cref="CrashFileSink"/> exists: the log stays on this PC. Sending crash reports
    /// anywhere (an opt-in upload) is an OPEN decision for the human (O6-like); such an uploader would be another sink, added only once
    /// decided, and only with the player's consent. Called from any thread: an implementation must be thread-safe and never throw.
    /// </summary>
    public interface ICrashSink
    {
        void Write(string entry);
    }
}
