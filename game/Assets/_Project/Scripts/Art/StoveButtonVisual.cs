using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 019/021: colours a stove's knobs, buttons and burner rings from the synced
    /// <see cref="StationContents.StoveFlags"/>, per burner: the knob glows green (sage) when on and is dark red (brick) when off,
    /// the burner ring glows orange while on, the mode button shows "fry" or "sauté". Display only, client side; the
    /// server decides the flags. Index in each array = burner.
    /// </summary>
    public sealed class StoveButtonVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Spec 026: palette colours (a sage knob when on, dark brick when off, a glowing terracotta ring).
        private static readonly Color On = Palette.Ground;
        private static readonly Color Off = Palette.Shade(Palette.WoodDark, 0.6f);
        private static readonly Color Fry = Palette.Terracotta;
        private static readonly Color Saute = Palette.Mustard;
        private static readonly Color RingOn = Palette.Terracotta;
        private static readonly Color RingOff = Palette.Charcoal;

        [SerializeField] private StationContents _contents;
        [SerializeField] private Renderer[] _switches = new Renderer[0];
        [SerializeField] private Renderer[] _modeButtons = new Renderer[0];
        [SerializeField] private TextMesh[] _modeLabels = new TextMesh[0];
        [Tooltip("Spec 021: a flat ring on each burner that glows while it is on.")]
        [SerializeField] private Renderer[] _rings = new Renderer[0];

        private MaterialPropertyBlock _block;
        private int _shown = -1;

        private void LateUpdate()
        {
            if (_contents == null) return;
            byte flags = _contents.StoveFlags.Value;
            if (flags == _shown) return;
            _shown = flags;
            for (int burner = 0; burner < StationRules.BurnersPerStove; burner++)
            {
                bool on = StationRules.IsBurnerOn(flags, burner);
                bool saute = StationRules.IsBurnerSaute(flags, burner);
                Paint(At(_switches, burner), on ? On : Off, on);
                Paint(At(_modeButtons, burner), saute ? Saute : Fry, false);
                Paint(At(_rings, burner), on ? RingOn : RingOff, on);
                TextMesh label = burner < _modeLabels.Length ? _modeLabels[burner] : null;
                if (label != null) label.text = saute ? "sauté" : "fry";
            }
        }

        private static Renderer At(Renderer[] renderers, int i) => renderers != null && i < renderers.Length ? renderers[i] : null;

        private void Paint(Renderer target, Color color, bool glow)
        {
            if (target == null) return;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(EmissionColorId, glow ? color * 1.5f : Color.black);
            target.SetPropertyBlock(_block);
        }
    }
}
