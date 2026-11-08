using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Social
{
    /// <summary>
    /// A world ping (spec 012): a thin pillar with a label, local to each client, fading after
    /// <see cref="SocialRules.PingSeconds"/>. Spawned from a prefab by SocialInteractor.
    /// </summary>
    public sealed class PingMarker : MonoBehaviour
    {
        private static readonly Color LookColor = Palette.Mustard; // spec 026
        private static readonly Color DangerColor = Palette.Terracotta;

        [SerializeField] private Renderer _pillar;
        [SerializeField] private TextMesh _label;

        private float _born;
        private Vector3 _baseScale;

        public void Init(PingKind kind, string text)
        {
            _born = Time.time;
            _baseScale = transform.localScale;
            if (_label != null)
            {
                _label.text = text;
                _label.color = kind == PingKind.Danger ? DangerColor : LookColor;
            }
            if (_pillar != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", kind == PingKind.Danger ? DangerColor : LookColor);
                _pillar.SetPropertyBlock(block);
            }
        }

        private void Update()
        {
            float age = Time.time - _born;
            if (age >= SocialRules.PingSeconds)
            {
                Destroy(gameObject);
                return;
            }
            // Shrink during the last second.
            float remaining = SocialRules.PingSeconds - age;
            transform.localScale = _baseScale * Mathf.Clamp01(remaining);
            Camera cam = Camera.main;
            if (_label != null && cam != null)
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }
    }
}
