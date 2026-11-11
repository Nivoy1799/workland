using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 020: the top of a worktop (spec 021: also of a stove outside its burners, and each burner as a surface with
    /// one spot and the burner flag; spec 042: each tap's cup spot on a drink dispenser's drip tray, one spot) that items can be put on. Part of
    /// a layout piece (spec 062a), the same on server and clients; requests refer to it by its stable <see cref="Id"/> (SurfaceId: the
    /// piece id and its local number, given when the piece registers). Data only: the spot
    /// grid comes from <see cref="PlaceSpots"/>, the server (PickupService) decides who may put what where.
    /// Sits on the object with the collider the aim ray hits; the top is at local height <see cref="TopY"/>,
    /// centred on the local origin, <see cref="Size"/> wide (x) and deep (z). The object is not scaled.
    /// </summary>
    public sealed class PlaceSurface : MonoBehaviour
    {
        [Tooltip("Width (x) and depth (local z) of the top in metres.")]
        [SerializeField] private Vector2 _size = new Vector2(1f, 0.6f);
        [Tooltip("Local height of the top, where items rest.")]
        [SerializeField] private float _topY;
        [Tooltip("Spec 021: a burner of a stove (one spot); a pan or pot on it cooks while its knob is on.")]
        [SerializeField] private bool _isBurner;
        [Tooltip("Spec 021, burners: which burner of that stove (0 or 1).")]
        [SerializeField] private int _burner = -1;

        [Tooltip("Spec 042: a cup spot on a drink dispenser's drip tray (one spot): the tap above it pours into the cup standing there.")]
        [SerializeField] private bool _isCupSpot;
        [Tooltip("Spec 042, cup spots: the tap above it (DrinkRules: 0 lemonade, 1 iced tea, 2 soda).")]
        [SerializeField] private int _tap = -1;

        private int _id = Layout.SurfaceId.None;
        private int _pieceId;

        /// <summary>Spec 062a: the stable id (SurfaceId.Of(piece, local)); SurfaceId.None until its piece registers.</summary>
        public int Id => _id;
        /// <summary>Spec 062a: the id of the piece this surface is part of (0 until it registers).</summary>
        public int PieceId => _pieceId;
        /// <summary>Spec 042: a tap's cup spot on the drip tray (only a cup that fits the tap goes there).</summary>
        public bool IsCupSpot => _isCupSpot;
        /// <summary>Spec 062a (was the dispenser's station index): the piece id of the drink dispenser this cup spot is on, or -1.</summary>
        public int DispenserPieceId => _isCupSpot ? _pieceId : -1;
        public int CupTap => _isCupSpot ? _tap : -1;
        public Vector2 Size => _size;
        public float TopY => _topY;
        public int SpotCount => PlaceSpots.Count(_size);
        public bool IsBurner => _isBurner;
        /// <summary>Spec 062a (was the stove's station index): the piece id of the stove this burner is on, or -1.</summary>
        public int StovePieceId => _isBurner ? _pieceId : -1;
        public int Burner => _burner;

        /// <summary>Spec 062a: given by the piece when it registers (server and clients compute the same id).</summary>
        internal void AssignId(int id, int pieceId)
        {
            _id = id;
            _pieceId = pieceId;
        }

        /// <summary>Spec 062a: a counter piece's length sets the top's width (x).</summary>
        internal void SetWidth(float width) => _size = new Vector2(width, _size.y);

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
