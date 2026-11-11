using System;
using System.Collections;
using System.Reflection;
using FishNet.Component.Transforming;
using UnityEngine;

namespace Worktown.Game.Net
{
    /// <summary>
    /// 054 review, a client: forgets the goals a NetworkTransform still had queued when its item was picked up (an item picked up right
    /// after it landed, within about 100 ms, still has the landing's updates buffered). Without this, the release would first play those
    /// stale goals (the item slides back to where it lay) before the server's release pose. FishNet has no public call for it, and its
    /// sources are not edited (AGENTS rule 9), so its two private fields are cleared by reflection; a test pins that they exist and have
    /// the expected shape. 053 review: it never throws into the pick-up callback: the first failure logs one warning and turns the
    /// helper off for the rest of the run (the item then only loses this polish, never its pick-up).
    /// </summary>
    public static class TransformGoals
    {
        public const string QueueField = "_goalDataQueue";
        public const string CurrentField = "_currentGoalData";

        private static readonly FieldInfo Queue = typeof(NetworkTransform).GetField(QueueField, BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Current = typeof(NetworkTransform).GetField(CurrentField, BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo QueueClear = Queue != null ? Queue.FieldType.GetMethod("Clear", Type.EmptyTypes) : null;

        // 053 review: off after the first failure (one warning per run). The only mutable state: a latch, never reset in a run.
        private static bool _broken;

        /// <summary>Both fields were found with the expected shape (a collection, a reference) and the helper has not failed.</summary>
        public static bool Available => Queue != null && Current != null && QueueClear != null && !_broken
            && typeof(ICollection).IsAssignableFrom(Queue.FieldType) && !Current.FieldType.IsValueType;

        /// <summary>The queue field's type (tests).</summary>
        public static Type QueueType => Queue?.FieldType;
        /// <summary>The current goal field's type (tests).</summary>
        public static Type CurrentType => Current?.FieldType;

        /// <summary>Drops every queued and the current goal of <paramref name="nt"/>; false when nothing could be cleared. Never throws.</summary>
        public static bool Clear(NetworkTransform nt)
        {
            if (nt == null || !Available) return false;
            try
            {
                if (Queue.GetValue(nt) is ICollection queue && queue.Count > 0) QueueClear.Invoke(queue, null);
                Current.SetValue(nt, null);
                return true;
            }
            catch (Exception e)
            {
                _broken = true;
                Debug.LogWarning("[Net] clearing a NetworkTransform's stale goals failed (" + e.GetType().Name + ": " + e.Message
                    + "); the helper is off for this run");
                return false;
            }
        }
    }
}
