using System;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 049 (review), pure: runs one step of a shutdown so that a throw cannot end the steps after it. The host's stall close
    /// (NetBootstrap) stops the client and the server of a FishNet that may be in any state; each stop runs through here, so the
    /// error screen and the transport pump still happen when one of them throws.
    /// </summary>
    public static class GuardedCall
    {
        /// <summary>
        /// Runs <paramref name="step"/>. An exception is handed to <paramref name="warn"/> as "what: Type: message" and swallowed (a
        /// throwing <paramref name="warn"/> too). True when the step ran without one; a null step counts as done.
        /// </summary>
        public static bool Run(Action step, string what, Action<string> warn)
        {
            if (step == null) return true;
            try
            {
                step();
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    warn?.Invoke((string.IsNullOrEmpty(what) ? "a step" : what) + " failed: " + e.GetType().Name + ": " + e.Message);
                }
                catch (Exception)
                {
                    // the warning itself must not end the shutdown either
                }
                return false;
            }
        }
    }
}
