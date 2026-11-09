using UnityEngine;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 034, client: the unpack at the click (docs/feel.md 1, 2): a collider-free copy of the carried box's model tips towards the
    /// slot (BoxLookRules), moves into it and shrinks away over <see cref="BoxLookRules.UnpackSeconds"/>, while the slot's samples pop in
    /// (StockSlotLook). The real box hides at the click (it is despawned when the server empties it). Nothing teleports.
    /// </summary>
    public sealed class UnpackLook : MonoBehaviour
    {
        /// <summary>The copy's size at the end of the look (a share of its start).</summary>
        public const float EndScale = 0.35f;

        private Vector3 _from;
        private Vector3 _to;
        private Quaternion _rotation;
        private Vector3 _scale;
        private float _start;

        /// <summary>
        /// Starts the look from <paramref name="model"/> drawn at <paramref name="from"/> (its world pose as the player sees it) with
        /// <paramref name="scale"/>, towards <paramref name="to"/>. Null when there is nothing to copy.
        /// </summary>
        public static UnpackLook Begin(GameObject model, Pose from, float scale, Vector3 to)
        {
            if (model == null) return null;
            GameObject copy = Instantiate(model, from.position, from.rotation);
            copy.name = "UnpackLook";
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (MonoBehaviour b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
            copy.SetActive(true);
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            var look = copy.AddComponent<UnpackLook>();
            look._from = from.position;
            look._to = to;
            look._rotation = from.rotation;
            look._scale = model.transform.lossyScale * Mathf.Max(0.01f, scale);
            look._start = Time.unscaledTime;
            copy.transform.localScale = look._scale;
            return look;
        }

        /// <summary>Pure: the share of the way to the slot <paramref name="t"/> s into the look (an ease-out, 0..1).</summary>
        public static float Travel(float t)
        {
            float u = Mathf.Clamp01(t / BoxLookRules.UnpackSeconds);
            return 1f - (1f - u) * (1f - u);
        }

        private void LateUpdate()
        {
            float t = Time.unscaledTime - _start;
            if (t >= BoxLookRules.UnpackSeconds)
            {
                Destroy(gameObject);
                return;
            }
            float k = Travel(t);
            transform.SetPositionAndRotation(Vector3.Lerp(_from, _to, k), _rotation * Quaternion.Euler(BoxLookRules.UnpackAngle(t), 0f, 0f));
            transform.localScale = _scale * Mathf.Lerp(1f, EndScale, k);
        }
    }
}
