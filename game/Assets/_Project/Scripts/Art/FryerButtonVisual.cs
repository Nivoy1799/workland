using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029, client only: the fryer's front from its synced flags (<see cref="FryerRules.FryerFlags"/>): the power knob glows
    /// sage while on (dark brick when off), the oil pilot light glows terracotta while on, each basket's button glows mustard
    /// while its basket is in the oil. Display only; the server decides the flags.
    /// </summary>
    public sealed class FryerButtonVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color On = Palette.Sage;
        private static readonly Color Off = Palette.Shade(Palette.Brick, 0.6f);
        private static readonly Color Lowered = Palette.Mustard;
        private static readonly Color Raised = Palette.Charcoal; // dark on the steel front, so the button reads

        [SerializeField] private StationContents _contents;
        [SerializeField] private Renderer _power;
        [SerializeField] private Renderer _pilot;
        [Tooltip("Index = basket.")]
        [SerializeField] private Renderer[] _baskets = new Renderer[0];

        private MaterialPropertyBlock _block;
        private int _shown = -1;

        private void LateUpdate()
        {
            if (_contents == null) return;
            byte flags = _contents.StoveFlags.Value;
            if (flags == _shown) return;
            _shown = flags;
            bool on = FryerRules.IsOn(flags);
            Paint(_power, on ? On : Off, on);
            Paint(_pilot, on ? Palette.Terracotta : Palette.Charcoal, on);
            for (int b = 0; b < _baskets.Length; b++)
            {
                bool lowered = FryerRules.IsLowered(flags, b);
                Paint(_baskets[b], lowered ? Lowered : Raised, lowered);
            }
        }

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
