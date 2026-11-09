using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 027c, client only: the reward moment after a sale at the counter, for every viewer near it. Watches the serve
    /// feed's <see cref="ServeFeed.LastReward"/> SyncVar (no RPC); a new serial pops five stars over the counter (the earned
    /// ones in Mustard), plays the coin sound there and floats the money and tip up in Mustard (<see cref="ServeRewardRules"/>).
    /// The first value a client finds after joining is only recorded. Adds itself after the scene loads; the headless server
    /// has none.
    /// </summary>
    public sealed class ServeRewardView : MonoBehaviour
    {
        private const int MaxPopups = 3;
        private const float FindInterval = 1f;

        private sealed class Popup
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Stars;
            public TextMesh Money;
            public Vector3 Start;
            public float Born;
            public int Earned;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private ServeFeed _feed;
        private float _nextFind;
        private bool _seen;
        private int _serial;
        private Material _material;
        private Font _font;
        private KitchenSounds _sounds;
        private MaterialPropertyBlock _block;
        private readonly List<Popup> _popups = new List<Popup>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (FindAnyObjectByType<ServeRewardView>() != null) return;
            new GameObject("ServeReward").AddComponent<ServeRewardView>();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (_feed == null && now >= _nextFind)
            {
                _nextFind = now + FindInterval;
                _feed = FindAnyObjectByType<ServeFeed>();
            }
            if (_feed == null || !_feed.IsSpawned) _seen = false; // review: a rejoin finds the last sale again, never pops it
            if (_feed != null && _feed.IsSpawned)
            {
                ServeReward reward = _feed.LastReward.Value;
                if (!_seen)
                {
                    _seen = true; // a joining client finds the last sale: not a new one
                    _serial = reward.Serial;
                }
                else if (reward.Serial != _serial)
                {
                    _serial = reward.Serial;
                    Show(reward);
                }
            }
            Animate(now);
        }

        private void Show(ServeReward reward)
        {
            Station counter = CounterOf(reward.Counter);
            if (counter == null) return;
            Vector3 start = ServeRewardRules.StartOver(counter.transform.position, TopOf(counter));
            Camera cam = Camera.main;
            if (cam == null || Vector3.Distance(cam.transform.position, start) > ServeRewardRules.ViewDistance) return;
            // Review: the coin only for a sale close enough to see.
            if (_sounds == null) _sounds = FindAnyObjectByType<KitchenSounds>();
            if (_sounds != null) _sounds.Play(SoundKey.Coin, start);
            Debug.Log("[Reward] " + reward + " at " + counter.name);

            if (_popups.Count >= MaxPopups) Release(0);
            _popups.Add(Create(start, ServeRewardRules.EarnedStars(reward), ServeRewardRules.MoneyText(reward)));
        }

        /// <summary>The top of the counter (its colliders' bounds); its pivot height when it has none.</summary>
        private static float TopOf(Station counter)
        {
            Collider[] colliders = counter.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return counter.transform.position.y;
            float top = float.NegativeInfinity;
            foreach (Collider c in colliders) top = Mathf.Max(top, c.bounds.max.y);
            return top;
        }

        private static Station CounterOf(int index)
        {
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
                if (s.Index == index && s.Kind == StationKind.Counter) return s;
            return null;
        }

        private Popup Create(Vector3 start, int earned, string money)
        {
            if (_material == null) _material = OverlayMaterial.Create(null, "ServeReward");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("ServeReward popup");
            root.transform.position = start;

            var starsObject = new GameObject("Stars");
            starsObject.transform.SetParent(root.transform, false);
            var mesh = new Mesh { name = "ServeRewardStars" };
            mesh.MarkDynamic();
            starsObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var stars = starsObject.AddComponent<MeshRenderer>();
            stars.sharedMaterial = _material;
            stars.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stars.receiveShadows = false;

            var textObject = new GameObject("Money");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, -0.07f, 0f);
            var text = textObject.AddComponent<TextMesh>();
            text.font = _font;
            textObject.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            text.fontSize = 64;
            text.characterSize = 0.016f;
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.color = ServeRewardRules.MoneyColor;
            text.text = money;

            return new Popup { Root = root, Mesh = mesh, Stars = stars, Money = text, Start = start, Born = Time.unscaledTime, Earned = earned };
        }

        private void Animate(float now)
        {
            Camera cam = Camera.main;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                Popup popup = _popups[i];
                float t = now - popup.Born;
                if (t >= ServeRewardRules.Seconds || popup.Root == null)
                {
                    Release(i);
                    continue;
                }
                popup.Root.transform.position = popup.Start + Vector3.up * ServeRewardRules.Height(t);
                if (cam != null) popup.Root.transform.rotation = Quaternion.LookRotation(popup.Root.transform.position - cam.transform.position);
                float alpha = ServeRewardRules.Alpha(t);
                BuildStars(popup, t);
                _block ??= new MaterialPropertyBlock();
                _block.SetColor(BaseColorId, Palette.WithAlpha(Color.white, alpha));
                popup.Stars.SetPropertyBlock(_block);
                popup.Money.color = Palette.WithAlpha(ServeRewardRules.MoneyColor, alpha);
            }
        }

        private void BuildStars(Popup popup, float t)
        {
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            int count = StarRating.MaxStars;
            for (int s = 0; s < count; s++)
            {
                float scale = ServeRewardRules.StarScale(s, t) * ServeRewardRules.StarSize;
                if (scale <= 0f) continue;
                float x = (s - (count - 1) * 0.5f) * ServeRewardRules.StarSpacing;
                // The root looks away from the camera (like the world labels), so its +X is the viewer's right.
                ServeRewardRules.AddStar(new Vector3(x, 0f, 0f), scale,
                    s < popup.Earned ? ServeRewardRules.Earned : ServeRewardRules.NotEarned, _vertices, _colors, _triangles);
            }
            HeatRingRules.ToVertexColors(_colors, QualitySettings.activeColorSpace == ColorSpace.Linear);
            popup.Mesh.Clear();
            popup.Mesh.SetVertices(_vertices);
            popup.Mesh.SetColors(_colors);
            popup.Mesh.SetTriangles(_triangles, 0);
            popup.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        }

        private void Release(int index)
        {
            Popup popup = _popups[index];
            _popups.RemoveAt(index);
            if (popup.Root != null) Destroy(popup.Root);
            if (popup.Mesh != null) Destroy(popup.Mesh);
        }

        private void OnDestroy()
        {
            for (int i = _popups.Count - 1; i >= 0; i--) Release(i);
        }
    }
}
