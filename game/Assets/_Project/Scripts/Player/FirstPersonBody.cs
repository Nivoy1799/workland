using UnityEngine;
using UnityEngine.Rendering;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 018, owner only: in first person the local player's own body and nose only cast shadows (you
    /// see your shadow, not the inside of the capsule) and the labels above the head (emote bubble) are not
    /// drawn. Third person shows everything again. Other players always see the full body; this component
    /// stays inactive on their copies because only the owner calls <see cref="Activate"/>.
    /// </summary>
    public sealed class FirstPersonBody : MonoBehaviour
    {
        [Tooltip("Body and nose: shadows only in first person.")]
        [SerializeField] private Renderer[] _body = new Renderer[0];
        [Tooltip("Labels above the head (emote bubble): hidden in first person.")]
        [SerializeField] private Renderer[] _overhead = new Renderer[0];

        private PlayerCameraRig _rig;
        private ShadowCastingMode[] _original;

        public void Activate(PlayerCameraRig rig)
        {
            if (_rig != null || rig == null) return;
            _rig = rig;
            _original = new ShadowCastingMode[_body.Length];
            for (int i = 0; i < _body.Length; i++)
                _original[i] = _body[i] != null ? _body[i].shadowCastingMode : ShadowCastingMode.On;
            rig.ModeChanged += Apply;
            Apply(rig.Mode);
        }

        private void Apply(ViewMode mode)
        {
            bool firstPerson = mode == ViewMode.FirstPerson;
            for (int i = 0; i < _body.Length; i++)
                if (_body[i] != null)
                    _body[i].shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : _original[i];
            foreach (Renderer label in _overhead)
                if (label != null) label.enabled = !firstPerson;
        }

        private void OnDestroy()
        {
            if (_rig != null) _rig.ModeChanged -= Apply;
        }
    }
}
