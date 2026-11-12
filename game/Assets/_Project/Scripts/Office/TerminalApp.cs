namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059: the apps of the office terminal, in tab order. New apps are appended (062c furniture, 060 brand and marketing): the
    /// number is part of a terminal press (<see cref="TerminalOptions"/>).
    /// </summary>
    public enum TerminalApp : byte
    {
        /// <summary>The dishes on sale: price, on or off, the special of the day.</summary>
        Menu = 0,
        /// <summary>The services (052 channels): the counter, and what comes with stage 3.</summary>
        Services = 1,
        /// <summary>How the last services went, in words and bars; the change log.</summary>
        Results = 2
    }
}
