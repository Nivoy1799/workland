namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053, pure: when the host's game is saved, and which day a save belongs to. Only the server (the host) saves, and only a run
    /// with a save slot (a New shop or a Continue; never the open kitchen, never a scripted run without <c>-save-dir</c>). Every trigger
    /// saves then, but <see cref="SaveTrigger.Periodic"/> only while a service is open and <see cref="PeriodicSeconds"/> after the last save.
    /// </summary>
    public static class AutosaveRules
    {
        /// <summary>The periodic save during a service: every 5 minutes (open question 6).</summary>
        public const float PeriodicSeconds = 300f;

        public static bool ShouldSave(SaveTrigger trigger, bool isServer, bool hasSlot, float secondsSinceLastSave, bool serviceOpen)
        {
            if (!isServer || !hasSlot) return false;
            if (trigger == SaveTrigger.Periodic) return serviceOpen && secondsSinceLastSave >= PeriodicSeconds;
            return true;
        }

        /// <summary>
        /// The day a save written now belongs to (the next day to play): the open service's day (a load restarts that day's service); after
        /// a service closed, the day after it; otherwise the clock's day.
        /// </summary>
        public static long NextDay(bool serviceOpen, long serviceDay, bool hasLast, long lastDay, long clockDay)
        {
            long day;
            if (serviceOpen) day = serviceDay;
            else if (hasLast && lastDay >= clockDay) day = lastDay + 1;
            else day = clockDay;
            return day < 1 ? 1 : day;
        }
    }
}
