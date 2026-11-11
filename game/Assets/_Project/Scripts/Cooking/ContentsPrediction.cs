using System.Collections.Generic;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 044 (036 feels off: "the scoop is not predicted"; the 029b tip): the contents a vessel shows at the owner's click, before the
    /// server's answer: a tip moves the basket's (or the griddle spot's) food onto the plate in the hands, a scoop takes one portion off the
    /// spit's tray. Pure; <see cref="Vessel"/> draws <see cref="Shown"/> instead of its synced looks until those change (the answer is
    /// there, right or wrong) or the confirm timeout passes (no answer: the look goes back). Look only; the server decides.
    /// </summary>
    public sealed class ContentsPrediction
    {
        private readonly List<FoodLook> _looks = new List<FoodLook>();
        private float _until = float.NegativeInfinity;
        private bool _active;

        public bool IsActive => _active;

        /// <summary>Shows <paramref name="looks"/> until <paramref name="until"/> (unscaled seconds) or the synced looks change.</summary>
        public void Begin(IReadOnlyList<FoodLook> looks, float until)
        {
            _looks.Clear();
            if (looks != null) for (int i = 0; i < looks.Count; i++) _looks.Add(looks[i]);
            _until = until;
            _active = true;
        }

        /// <summary>The synced looks changed: the server answered; the real contents show again. True if a prediction ended.</summary>
        public bool SyncedChanged()
        {
            if (!_active) return false;
            _active = false;
            return true;
        }

        /// <summary>
        /// 044 follow-up: the server refused the request this look belongs to: the real contents show again at once (not after the
        /// confirm timeout). True if a prediction ended.
        /// </summary>
        public bool Refused()
        {
            if (!_active) return false;
            _active = false;
            return true;
        }

        /// <summary>
        /// 044 follow-up (spec 045 request ids): whether the answer to request <paramref name="answered"/> (0: not one of ours) ends the
        /// tip or scoop look sent as request <paramref name="request"/> at once: only a refusal of that very request does. A yes leaves
        /// it to the synced contents (they change with the answer).
        /// </summary>
        public static bool RefusalEnds(int answered, int request, bool ok) => answered != 0 && answered == request && !ok;

        /// <summary>True if the prediction ran out at <paramref name="now"/> (it ends then).</summary>
        public bool Expired(float now)
        {
            if (!_active || now < _until) return false;
            _active = false;
            return true;
        }

        /// <summary>What to draw: the predicted looks while active, else <paramref name="synced"/>.</summary>
        public IReadOnlyList<FoodLook> Shown(IReadOnlyList<FoodLook> synced) => _active ? _looks : synced;

        /// <summary>A tip: the plate's looks followed by the source's (at most <see cref="FoodLook.MaxParts"/>).</summary>
        public static List<FoodLook> Tipped(IReadOnlyList<FoodLook> plate, IReadOnlyList<FoodLook> source)
        {
            var result = new List<FoodLook>();
            if (plate != null) for (int i = 0; i < plate.Count && result.Count < FoodLook.MaxParts; i++) result.Add(plate[i]);
            if (source != null) for (int i = 0; i < source.Count && result.Count < FoodLook.MaxParts; i++) result.Add(source[i]);
            return result;
        }

        /// <summary>A scoop: the tray without its last portion (the one shaved last, as the server takes it).</summary>
        public static List<FoodLook> Scooped(IReadOnlyList<FoodLook> tray)
        {
            var result = new List<FoodLook>();
            if (tray != null) for (int i = 0; i < tray.Count - 1; i++) result.Add(tray[i]);
            return result;
        }
    }
}
