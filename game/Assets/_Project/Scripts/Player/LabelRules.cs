using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027d: pure rules of world text (docs/feel.md 3 and 7, docs/art-direction.md 7): what shows, how big, and which
    /// of two overlapping labels gives way. Text is never mirrored (a one-sided label seen from behind is hidden), never
    /// piles up (the lower priority one of two overlapping labels is hidden) and never sits across the first-person view.
    /// </summary>
    public static class LabelRules
    {
        /// <summary>dot(label front, direction to the viewer) must reach this for a one-sided label to show.</summary>
        public const float FacingMin = 0.2f;
        /// <summary>First person: no label within this angle of the view centre (the aimed item's own label excepted).</summary>
        public const float CentreFreeDegrees = 15f;
        public const float BubbleRange = 8f;
        public const float SignRange = 30f;
        public const float ItemRange = 6f;
        public const float RailRange = 14f;
        public const float ButtonRange = 4f;
        /// <summary>Queue ranks below this (the head and the next guest) show their order in full; the rest an icon.</summary>
        public const int FullTextRanks = 2;
        /// <summary>Signs grow with distance up to this factor (6 cm letters at 1 m, 12 cm from 2 m).</summary>
        public const float SignMaxScale = 2f;

        // Priorities, lowest number wins an overlap.
        public const int PriorityAimed = 0;
        public const int PriorityHeld = 1;
        /// <summary>The heat ring (spec 027c): below the aimed target, above every label.</summary>
        public const int PriorityRing = 2;
        public const int PriorityQueueHead = 3;
        public const int PriorityQueueNext = 4;
        public const int PrioritySign = 5;
        public const int PriorityButton = 6;
        public const int PriorityRail = 7;
        public const int PriorityRest = 8;

        /// <summary>True when a one-sided label is seen from its front side (not from behind or edge on).</summary>
        public static bool FacesViewer(float facingDot) => facingDot >= FacingMin;

        /// <summary>Whether labels of this kind are one-sided (a billboard always faces the viewer).</summary>
        public static bool IsOneSided(LabelKind kind) => kind == LabelKind.Sign || kind == LabelKind.Rail || kind == LabelKind.Button;

        /// <summary>Angle in degrees between the view direction and the direction to the label; 180 for a label at the eye.</summary>
        public static float AngleFromCentre(Vector3 viewForward, Vector3 toLabel)
        {
            if (toLabel.sqrMagnitude < 1e-8f || viewForward.sqrMagnitude < 1e-8f) return 180f;
            return Vector3.Angle(viewForward, toLabel);
        }

        public static float RangeOf(LabelKind kind)
        {
            switch (kind)
            {
                case LabelKind.Sign: return SignRange;
                case LabelKind.Item: return ItemRange;
                case LabelKind.Bubble: return BubbleRange;
                case LabelKind.Rail: return RailRange;
                default: return ButtonRange;
            }
        }

        /// <summary>
        /// Spec 048: whether a label of this kind shows under the settings' labels switch. Off (the spec 040 test: can you tell
        /// everything apart by its look?) hides the names: station and crate signs and item and cookware labels. Orders, the ticket
        /// rail and the stove buttons still show; they are information, not names.
        /// </summary>
        public static bool ShownWithLabels(LabelKind kind, bool labelsOn) =>
            labelsOn || (kind != LabelKind.Sign && kind != LabelKind.Item);

        /// <summary>
        /// Whether a label shows at all. <paramref name="facingDot"/> is only used by one-sided kinds;
        /// <paramref name="aimed"/> is the aimed item, <paramref name="held"/> an item the local player carries.
        /// </summary>
        public static bool IsVisible(LabelKind kind, float distance, float facingDot, float angleFromCentre, bool firstPerson,
            bool aimed, bool held)
        {
            if (float.IsNaN(distance) || distance > RangeOf(kind)) return false;
            if (IsOneSided(kind) && !FacesViewer(facingDot)) return false;
            if (kind == LabelKind.Item && !(aimed || (held && !firstPerson))) return false; // a carried item shows no label in first person
            if (firstPerson && !aimed && angleFromCentre < CentreFreeDegrees) return false;
            return true;
        }

        /// <summary>
        /// How much the label's own size is multiplied at this distance: 1 up close, growing so the text stays readable far
        /// away. Signs: 6 cm letters at 1 m, up to three times that. Bubbles: grow from 3 m. Items stay as they are.
        /// </summary>
        public static float DistanceScale(LabelKind kind, float distance)
        {
            if (float.IsNaN(distance)) return 1f;
            switch (kind)
            {
                case LabelKind.Sign: return Mathf.Clamp(distance, 1f, SignMaxScale);
                case LabelKind.Bubble: return Mathf.Clamp(distance / 3f, 1f, 3f);
                default: return 1f;
            }
        }

        /// <summary>Overlap priority of a label (lower wins). <paramref name="queueRank"/> is the guest's place in the queue, -1 for none.</summary>
        public static int Priority(LabelKind kind, bool aimed, bool held, int queueRank)
        {
            if (aimed) return PriorityAimed;
            if (held) return PriorityHeld;
            switch (kind)
            {
                case LabelKind.Bubble:
                    return queueRank == 0 ? PriorityQueueHead : queueRank == 1 ? PriorityQueueNext : PriorityRest;
                case LabelKind.Sign: return PrioritySign;
                case LabelKind.Button: return PriorityButton;
                case LabelKind.Rail: return PriorityRail;
                default: return PriorityRest;
            }
        }

        /// <summary>The queue head and the next guest show their full order, so does anyone not waiting (leaving with a remark).</summary>
        public static bool ShowsFullText(int queueRank, bool waiting) => !waiting || queueRank < FullTextRanks;

        /// <summary>The icon of a collapsed order bubble: the first letter of the text, upper case ("?" when there is none).</summary>
        public static string Icon(string text)
        {
            if (!string.IsNullOrEmpty(text))
                foreach (char c in text)
                {
                    if (!char.IsLetter(c)) continue;
                    char upper = char.ToUpperInvariant(c);
                    // Second review: the Latin letters come from a table (no string per collapsed bubble per frame).
                    return upper >= 'A' && upper <= 'Z' ? Letters[upper - 'A'] : upper.ToString();
                }
            return "?";
        }

        private static readonly string[] Letters = BuildLetters();

        private static string[] BuildLetters()
        {
            var letters = new string[26];
            for (int i = 0; i < 26; i++) letters[i] = ((char)('A' + i)).ToString();
            return letters;
        }

        // ---- Spec 031b: the calm view (docs/feel.md 3 and 7) ----

        /// <summary>Third person: at most this many of the nearest heat rings show (besides the alarms).</summary>
        public const int ThirdPersonRings = 4;

        /// <summary>031b: a ring or heat label that always shows: its food is about to overcook (the warning), overdone or burnt.</summary>
        public static bool IsAlarm(Worktown.Game.Cooking.HeatZone zone, bool warning) =>
            warning || zone == Worktown.Game.Cooking.HeatZone.Overdone || zone == Worktown.Game.Cooking.HeatZone.Burnt;

        /// <summary>
        /// 031b, the calm view: a heat ring (or heat label) shows only for the aimed spot or vessel, an alarm
        /// (<see cref="IsAlarm"/>), and in third person the <see cref="ThirdPersonRings"/> nearest (<paramref name="nearRank"/>:
        /// 0 for the nearest, -1 unknown). A full griddle in first person shows the aimed ring and the warnings, never six.
        /// </summary>
        public static bool ShowsRing(bool aimed, bool alarm, bool firstPerson, int nearRank) =>
            aimed || alarm || (!firstPerson && nearRank >= 0 && nearRank < ThirdPersonRings);

        /// <summary>
        /// 031b: the nearness rank of each distance (0 the nearest; ties by index), into <paramref name="ranks"/> (at least as long).
        /// NaN counts as farthest. No allocation (a handful of rings).
        /// </summary>
        public static void NearRanks(IReadOnlyList<float> distances, int[] ranks)
        {
            int count = distances.Count;
            for (int i = 0; i < count; i++)
            {
                int rank = 0;
                float di = float.IsNaN(distances[i]) ? float.MaxValue : distances[i];
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    float dj = float.IsNaN(distances[j]) ? float.MaxValue : distances[j];
                    if (dj < di || (dj == di && j < i)) rank++;
                }
                ranks[i] = rank;
            }
        }

        /// <summary>
        /// Greedy overlap culling: rectangles are taken in order of priority (then of distance, nearest first); a rectangle
        /// that overlaps one already kept is dropped. <paramref name="keep"/> receives one flag per rectangle.
        /// </summary>
        public static void Cull(IReadOnlyList<Rect> rects, IReadOnlyList<int> priorities, IReadOnlyList<float> distances, bool[] keep)
        {
            // The per-frame caller keeps its own LabelCull (no allocations); this one-off form is for tests and tools.
            new LabelCull().Run(rects, priorities, distances, keep);
        }
    }
}
