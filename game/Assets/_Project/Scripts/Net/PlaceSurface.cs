using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 020: the top of a worktop (or of the board station's free half) that items can be put on. Static scene
    /// object, the same on server and clients; requests refer to it by <see cref="Index"/>. Data only: the spot
    /// grid comes from <see cref="PlaceSpots"/>, the server (PickupService) decides who may put what where.
    /// Sits on the object with the collider the aim ray hits; the top is at local height <see cref="TopY"/>,
    /// centred on the local origin, <see cref="Size"/> wide (x) and deep (z). The object is not scaled.
    /// </summary>
    public sealed class PlaceSurface : MonoBehaviour
    {
        [SerializeField] private int _index;
        [Tooltip("Width (x) and depth (local z) of the top in metres.")]
        [SerializeField] private Vector2 _size = new Vector2(1f, 0.6f);
        [Tooltip("Local height of the top, where items rest.")]
        [SerializeField] private float _topY;

        public int Index => _index;
        public Vector2 Size => _size;
        public float TopY => _topY;
        public int SpotCount => PlaceSpots.Count(_size);

        /// <summary>World position of a spot on the top (where the item's bottom rests).</summary>
        public Vector3 SpotWorldPosition(int spot)
        {
            Vector2 p = PlaceSpots.Position(_size, spot);
            return transform.TransformPoint(new Vector3(p.x, _topY, p.y));
        }

        /// <summary>A world point (e.g. the aim hit) in the top plane, relative to the top's centre.</summary>
        public Vector2 ToTopPlane(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.z);
        }
    }
}
