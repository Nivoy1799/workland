using System;

namespace Worktown.Game.UI
{
    /// <summary>Root of <c>Resources/WhatsNew.json</c> (spec 011).</summary>
    [Serializable]
    public sealed class WhatsNewFile
    {
        public WhatsNewEntry[] entries = Array.Empty<WhatsNewEntry>();
    }
}
