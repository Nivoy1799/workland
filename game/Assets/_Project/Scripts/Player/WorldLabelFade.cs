using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 020, owner only: in first person the stations' world labels are half their size and fade out within
    /// <see cref="FadeStart"/> of the camera (gone at <see cref="FadeEnd"/>), where the prompt under the dot says
    /// everything. Third person shows them as before. Local display only; it only touches the label's scale,
    /// colour alpha and renderer, and only when they change.
    /// </summary>
    public sealed class WorldLabelFade : MonoBehaviour
    {
        public const float FirstPersonScale = 0.5f;
        public const float FadeStart = 1.5f;
        public const float FadeEnd = 1.0f;
        private const float AlphaStep = 0.05f;

        private sealed class Entry
        {
            public TextMesh Text;
            public Renderer Renderer;
            public Vector3 Scale;
            public float Alpha = 1f;
            public bool Small;
        }

        private readonly List<Entry> _labels = new List<Entry>();
        private PlayerCameraRig _rig;
        private bool _active;

        /// <summary>Alpha of a label <paramref name="distance"/> metres from the camera in first person: 1 from
        /// <see cref="FadeStart"/> on, 0 at <see cref="FadeEnd"/> and closer, linear in between.</summary>
        public static float Alpha(float distance)
        {
            if (float.IsNaN(distance)) return 1f;
            return Mathf.Clamp01((distance - FadeEnd) / (FadeStart - FadeEnd));
        }

        public void Activate()
        {
            if (_active) return;
            _active = true;
            foreach (Station station in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                Transform label = station.transform.Find("Label");
                if (label == null || !label.TryGetComponent(out TextMesh text)) continue;
                _labels.Add(new Entry { Text = text, Renderer = label.GetComponent<Renderer>(), Scale = label.localScale });
            }
        }

        private void LateUpdate()
        {
            if (!_active) return;
            if (_rig == null) _rig = FindFirstObjectByType<PlayerCameraRig>();
            Camera cam = Camera.main;
            bool firstPerson = _rig != null && _rig.IsFirstPerson && cam != null;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (Entry entry in _labels)
            {
                if (entry.Text == null) continue;
                if (entry.Small != firstPerson)
                {
                    entry.Small = firstPerson;
                    entry.Text.transform.localScale = firstPerson ? entry.Scale * FirstPersonScale : entry.Scale;
                }
                float alpha = firstPerson ? Alpha(Vector3.Distance(eye, entry.Text.transform.position)) : 1f;
                alpha = Mathf.Round(alpha / AlphaStep) * AlphaStep; // only touch the mesh when it visibly changes
                if (Mathf.Approximately(alpha, entry.Alpha)) continue;
                entry.Alpha = alpha;
                Color color = entry.Text.color;
                color.a = alpha;
                entry.Text.color = color;
                if (entry.Renderer != null) entry.Renderer.enabled = alpha > 0f;
            }
        }
    }
}
