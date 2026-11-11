using System.Collections;
using System.Reflection;
using FishNet.Component.Transforming;

namespace Worktown.Game.Net
{
    /// <summary>
    /// 054 review, a client: forgets the goals a NetworkTransform still had queued when its item was picked up (an item picked up right
    /// after it landed, within about 100 ms, still has the landing's updates buffered). Without this, the release would first play those
    /// stale goals (the item slides back to where it lay) before the server's release pose. FishNet has no public call for it, and its
    /// sources are not edited (AGENTS rule 9), so its two private fields are cleared by reflection; a test pins that they exist, so a
    /// FishNet upgrade that renames them fails loudly instead of silently doing nothing.
    /// </summary>
    public static class TransformGoals
    {
        public const string QueueField = "_goalDataQueue";
        public const string CurrentField = "_currentGoalData";

        private static readonly FieldInfo Queue = typeof(NetworkTransform).GetField(QueueField, BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Current = typeof(NetworkTransform).GetField(CurrentField, BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Both fields were found (tests).</summary>
        public static bool Available => Queue != null && Current != null;

        /// <summary>Drops every queued and the current goal of <paramref name="nt"/>; false when nothing could be cleared.</summary>
        public static bool Clear(NetworkTransform nt)
        {
            if (nt == null || !Available) return false;
            if (Queue.GetValue(nt) is ICollection queue && queue.Count > 0)
                queue.GetType().GetMethod("Clear")?.Invoke(queue, null);
            Current.SetValue(nt, null);
            return true;
        }
    }
}
