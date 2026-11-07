using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 019: colours a stove's buttons from the synced <see cref="StationContents.StoveFlags"/>: the switch
    /// glows green when on and dark red when off; the pan's mode button shows "fry" or "sauté". Display only,
    /// client side; the server decides the flags.
    /// </summary>
    public sealed class StoveButtonVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color On = new Color(0.2f, 0.95f, 0.3f);
        private static readonly Color Off = new Color(0.4f, 0.05f, 0.05f);
        private static readonly Color Fry = new Color(0.95f, 0.55f, 0.15f);
        private static readonly Color Saute = new Color(0.95f, 0.85f, 0.3f);

        [SerializeField] private StationContents _contents;
        [SerializeField] private Renderer _switch;
        [Tooltip("Pan only.")]
        [SerializeField] private Renderer _modeButton;
        [Tooltip("Pan only: shows the current mode.")]
        [SerializeField] private TextMesh _modeLabel;

        private MaterialPropertyBlock _block;
        private int _shown = -1;

        private void LateUpdate()
        {
            if (_contents == null) return;
            byte flags = _contents.StoveFlags.Value;
            if (flags == _shown) return;
            _shown = flags;
            bool on = StationRules.IsStoveOn(flags);
            bool saute = StationRules.IsSaute(flags);
            Paint(_switch, on ? On : Off, on);
            Paint(_modeButton, saute ? Saute : Fry, false);
            if (_modeLabel != null) _modeLabel.text = saute ? "sauté" : "fry";
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
