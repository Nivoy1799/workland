using System;
using FishNet.Object;
using UnityEngine;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: the prefab of each piece type (written by PiecePrefabBuilder) and of each item kind a layout's homes name ("board",
    /// "knife", "pan", ...). The LayoutSpawner spawns from it; tests instantiate from it offline.
    /// </summary>
    [CreateAssetMenu(menuName = "Worktown/Piece Prefabs")]
    public sealed class PiecePrefabs : ScriptableObject
    {
        [Serializable]
        public struct PieceEntry
        {
            public string TypeId;
            public PieceBehaviour Prefab;
        }

        [Serializable]
        public struct ItemEntry
        {
            public string Kind;
            public NetworkObject Prefab;
        }

        [SerializeField] private PieceEntry[] _pieces = new PieceEntry[0];
        [SerializeField] private ItemEntry[] _items = new ItemEntry[0];

        public PieceEntry[] PieceEntries => _pieces;
        public ItemEntry[] ItemEntries => _items;

        /// <summary>The prefab of piece type <paramref name="typeId"/>, or null (an unknown type is not spawned).</summary>
        public PieceBehaviour Piece(string typeId)
        {
            foreach (PieceEntry e in _pieces)
                if (e.Prefab != null && string.Equals(e.TypeId, typeId, StringComparison.Ordinal)) return e.Prefab;
            return null;
        }

        /// <summary>The prefab of item kind <paramref name="kind"/>, or null.</summary>
        public NetworkObject Item(string kind)
        {
            foreach (ItemEntry e in _items)
                if (e.Prefab != null && string.Equals(e.Kind, kind, StringComparison.Ordinal)) return e.Prefab;
            return null;
        }

        /// <summary>Editor: the builder fills the tables.</summary>
        public void Set(PieceEntry[] pieces, ItemEntry[] items)
        {
            _pieces = pieces ?? new PieceEntry[0];
            _items = items ?? new ItemEntry[0];
        }
    }
}
