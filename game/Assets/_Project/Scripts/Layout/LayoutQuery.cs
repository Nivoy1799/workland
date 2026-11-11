using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: how tours and tests find things in the spawned layout, never by a number that only today's kitchen has: the nth piece of
    /// a type (by ascending id, from 1), a surface of it by its local name ("StoveTop" on the first stove), the nth station of a kind.
    /// Reads the LayoutRegistry.
    /// </summary>
    public static class LayoutQuery
    {
        /// <summary>The <paramref name="nth"/> piece (1 = the lowest id) of <paramref name="typeId"/>, or null.</summary>
        public static PieceBehaviour Piece(string typeId, int nth = 1)
        {
            int seen = 0;
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
            {
                if (p == null || !string.Equals(p.TypeId, typeId, StringComparison.Ordinal)) continue;
                if (++seen == nth) return p;
            }
            return null;
        }

        /// <summary>Every piece of <paramref name="typeId"/> by ascending id.</summary>
        public static List<PieceBehaviour> Pieces(string typeId)
        {
            var list = new List<PieceBehaviour>();
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
                if (p != null && string.Equals(p.TypeId, typeId, StringComparison.Ordinal)) list.Add(p);
            return list;
        }

        /// <summary>
        /// The surface of the <paramref name="nth"/> piece of <paramref name="typeId"/> whose object name is <paramref name="localName"/>
        /// (or starts with it and a space and a number: "Counter top" names "Counter top 2"); null when there is none.
        /// </summary>
        public static PlaceSurface Surface(string typeId, int nth, string localName)
        {
            PieceBehaviour piece = Piece(typeId, nth);
            if (piece == null) return null;
            foreach (PlaceSurface s in piece.Surfaces)
                if (s != null && s.gameObject.activeSelf && Names(s.name, localName)) return s;
            return null;
        }

        /// <summary>Surface <paramref name="local"/> (its local number) of the <paramref name="nth"/> piece of <paramref name="typeId"/>, or null.</summary>
        public static PlaceSurface Surface(string typeId, int nth, int local)
        {
            PieceBehaviour piece = Piece(typeId, nth);
            return piece != null && local >= 0 && local < piece.Surfaces.Count ? piece.Surfaces[local] : null;
        }

        /// <summary>The <paramref name="nth"/> station (1 = the lowest id) of <paramref name="kind"/>, or null.</summary>
        public static Station Station(StationKind kind, int nth = 1)
        {
            int seen = 0;
            foreach (Station s in LayoutRegistry.Stations)
                if (s != null && s.Kind == kind && ++seen == nth) return s;
            return null;
        }

        /// <summary>Every registered surface whose name is <paramref name="localName"/> or that name and a number.</summary>
        public static List<PlaceSurface> SurfacesNamed(string localName)
        {
            var list = new List<PlaceSurface>();
            foreach (PlaceSurface s in LayoutRegistry.Surfaces)
                if (s != null && Names(s.name, localName)) list.Add(s);
            return list;
        }

        /// <summary>"Counter top" names "Counter top" and "Counter top 2", not "Counter tops".</summary>
        public static bool Names(string objectName, string localName)
        {
            if (objectName == null || localName == null || !objectName.StartsWith(localName, StringComparison.Ordinal)) return false;
            if (objectName.Length == localName.Length) return true;
            return objectName.Length > localName.Length + 1 && objectName[localName.Length] == ' ' && char.IsDigit(objectName[localName.Length + 1]);
        }
    }
}
