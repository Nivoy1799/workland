using UnityEngine;

namespace Worktown.Game.Social
{
    /// <summary>
    /// Text bubble above a player's head for emotes (spec 012), plus a small wobble of the body so the
    /// emote reads even from behind. Purely visual and local to each client.
    /// </summary>
    public sealed class EmoteBubble : MonoBehaviour
    {
        [SerializeField] private TextMesh _label;
        [SerializeField] private Transform _body;

        private float _until;
        private float _started;

        public void Show(string text)
        {
            if (_label == null) return;
            _label.text = text;
            _label.gameObject.SetActive(true);
            _started = Time.time;
            _until = Time.time + SocialRules.BubbleSeconds;
        }

        private void Awake()
        {
            if (_label != null) _label.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_label == null || !_label.gameObject.activeSelf) return;
            float t = Time.time - _started;
            if (Time.time >= _until)
            {
                _label.gameObject.SetActive(false);
                if (_body != null) _body.localRotation = Quaternion.identity;
                return;
            }

            // Pop in, then hover; face the local camera.
            float scale = Mathf.Min(1f, t * 6f);
            _label.transform.localScale = Vector3.one * scale;
            Camera cam = Camera.main;
            if (cam != null)
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
            if (_body != null)
                _body.localRotation = t < 1f ? Quaternion.Euler(0f, 0f, Mathf.Sin(t * 20f) * 10f * (1f - t)) : Quaternion.identity;
        }
    }
}
