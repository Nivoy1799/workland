using System;
using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>One catalog row: a visual slot and the wrapper prefab that fills it.</summary>
    [Serializable]
    public struct ArtCatalogEntry
    {
        public ArtKey Key;
        public GameObject Prefab;
    }
}
