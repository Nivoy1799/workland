using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Worktown.Game.Player;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 028b, owner only, look only (docs/feel.md 1–2): taking an ingredient from a stock slot shows at once. A copy of
    /// the slot's first item model (no colliders) flies on the pick-up arc (FeelSettings.PickupSeconds and PickupLift) from
    /// the slot into the hand and waits there. When the server's food is in the hands the copy goes (<see cref="Confirm"/>;
    /// if it is still on its arc, the real item stays hidden until the copy lands, so nothing snaps and there is never a
    /// second item); if the server says no or nothing comes in time, the copy settles back into the slot and goes
    /// (<see cref="Deny"/>; the interactor plays "nope"). The stock never changes on the client: the slot keeps its items
    /// (stock is endless until spec 034). Placed right before each camera render, after the view has moved.
    /// </summary>
    public sealed class StockTakeLook : MonoBehaviour
    {
        /// <summary>Name of the copy's game object.</summary>
        public const string ObjectName = "StockTakeLook";

        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<Renderer> _scratch = new List<Renderer>();
        private Pose _slot;
        private Func<Pose> _hand;
        private float _start;
        private float _seconds;
        private float _lift;
        private bool _returning;
        private Pose _returnFrom;
        private float _returnStart;
        private float _returnSeconds;
        private float _settle;
        private bool _confirmed;
        private GameObject _real;
        private bool _hooked;

        /// <summary>True after <see cref="Deny"/>, while it settles back into the slot.</summary>
        public bool IsReturning => _returning;

        /// <summary>
        /// Starts the look for the slot <paramref name="slot"/> (its first child is the item model that is copied) towards
        /// <paramref name="hand"/> (asked again every frame: the hand moves with the view). Null when the slot shows no model.
        /// </summary>
        public static StockTakeLook Begin(Transform slot, Func<Pose> hand, float now, FeelSettings feel = null)
        {
            if (slot == null || hand == null || slot.childCount == 0) return null;
            feel ??= FeelSettings.Current ?? FeelSettings.PresetA();
            Transform model = slot.GetChild(0);
            GameObject copy = Instantiate(model.gameObject, model.position, model.rotation);
            copy.name = ObjectName;
            copy.transform.localScale = model.lossyScale;
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
                Gone(c);
            }
            var look = copy.AddComponent<StockTakeLook>();
            look._slot = new Pose(model.position, model.rotation);
            look._hand = hand;
            look._start = now;
            look._seconds = Mathf.Max(0.01f, feel.PickupSeconds);
            look._lift = feel.PickupLift;
            look._returnSeconds = Mathf.Max(0.01f, feel.PlaceSeconds);
            look._settle = feel.PlaceSettle;
            look.Place(now);
            return look;
        }

        /// <summary>Where the copy is drawn at <paramref name="now"/>: on the arc to the hand, at the hand, or settling back.</summary>
        public Pose Evaluate(float now)
        {
            if (_returning)
            {
                float r = FeelRules.Clamp01((now - _returnStart) / _returnSeconds);
                return new Pose(FeelRules.TweenPosition(TweenStyle.Settle, _returnFrom.position, _slot.position, r, _settle),
                    FeelRules.TweenRotation(TweenStyle.Settle, _returnFrom.rotation, _slot.rotation, r));
            }
            Pose hand = _hand != null ? _hand() : _slot;
            float t = FeelRules.Clamp01((now - _start) / _seconds);
            return new Pose(FeelRules.TweenPosition(TweenStyle.Arc, _slot.position, hand.position, t, _lift),
                FeelRules.TweenRotation(TweenStyle.Arc, _slot.rotation, hand.rotation, t));
        }

        /// <summary>True once the arc has reached the hand (or the copy is settling back).</summary>
        public bool HasLanded(float now) => _returning || now - _start >= _seconds;

        /// <summary>
        /// The server's food is in the hands. If the copy is still on its arc, the real item (<paramref name="real"/>) stays
        /// hidden until the copy lands; then the copy goes and the real one shows. Without a real item it goes at once.
        /// </summary>
        public void Confirm(GameObject real = null, float now = float.NaN)
        {
            if (float.IsNaN(now)) now = Time.unscaledTime;
            if (real != null && !_returning && !HasLanded(now))
            {
                _confirmed = true;
                _real = real;
                Hide(real);
                return;
            }
            Finish();
        }

        /// <summary>Refused or timed out: the copy settles back into the slot from where it is drawn, then goes.</summary>
        public void Deny(float now)
        {
            if (_returning || _confirmed) return;
            _returnFrom = Evaluate(now);
            _returning = true;
            _returnStart = now;
        }

        private void Finish()
        {
            ShowHidden();
            Unhook();
            gameObject.SetActive(false);
            Gone(gameObject);
        }

        /// <summary>Hides every renderer of the real item, also ones its visual adds after the confirm (asked every frame until
        /// the copy lands).</summary>
        private void Hide(GameObject real)
        {
            if (real == null) return;
            real.GetComponentsInChildren(true, _scratch);
            foreach (Renderer r in _scratch)
            {
                if (r == null || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                _hidden.Add(r);
            }
            _scratch.Clear();
        }

        private void ShowHidden()
        {
            foreach (Renderer r in _hidden)
                if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
        }

        private static void Gone(UnityEngine.Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }

        private void OnEnable()
        {
            if (_hooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _hooked = true;
        }

        private void OnDisable() => Unhook();

        private void OnDestroy()
        {
            ShowHidden();
            Unhook();
        }

        private void Unhook()
        {
            if (!_hooked) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _hooked = false;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if ((_returning && now - _returnStart >= _returnSeconds) || (_confirmed && HasLanded(now)))
            {
                Finish(); // back in the slot, or landed in the hand where the real item now shows
                return;
            }
            Place(now);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) => Place(Time.unscaledTime);

        private void Place(float now)
        {
            if (_confirmed) Hide(_real);
            Pose pose = Evaluate(now);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
    }
}
