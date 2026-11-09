using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the bin outside the staff gap: where a carried trash bag goes in (<see cref="Opening"/>, the bag's predicted arc), and its
    /// lid flipping open when the owner throws a bag in (<see cref="PredictBag"/>; the rustle plays at the click). Look only.
    /// </summary>
    public sealed class BinView : MonoBehaviour
    {
        [SerializeField] private Transform _opening;
        [SerializeField] private Transform _lid;

        private Quaternion _lidRest = Quaternion.identity;
        private float _at = float.NegativeInfinity;

        public Vector3 Opening => _opening != null ? _opening.position : transform.position + Vector3.up * 1.1f;

        private void Awake()
        {
            if (_lid != null) _lidRest = _lid.localRotation;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) enabled = false;
        }

        public void PredictBag() => _at = Time.unscaledTime;

        private void LateUpdate()
        {
            if (_lid == null) return;
            _lid.localRotation = _lidRest * Quaternion.Euler(-CleaningArtRules.LidAngle(Time.unscaledTime - _at) * 1.3f, 0f, 0f);
        }
    }
}
