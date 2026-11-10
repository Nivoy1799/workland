using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 039, pure: the "place anywhere" contract. Every horizontal top in the shop at counter height
    /// (<see cref="MinTop"/> to <see cref="MaxTop"/>) is covered by a <see cref="PlaceSurface"/>, except the parts listed
    /// in <see cref="Exceptions"/>, which have a function of their own (each with its reason). PlaceCoverageTests samples every
    /// such top on a <see cref="SampleStep"/> grid and fails on a dead point; a new station either gets a surface or an entry
    /// here. Within <see cref="Rim"/> of a listed part, or of something standing on the top (a register on the front counter),
    /// there is no room for an item anyway: a spot keeps PlaceSpots.EdgeMargin from its edges, plus half a sample cell.
    /// </summary>
    public static class PlaceCoverage
    {
        /// <summary>Lowest top that counts as counter height (m).</summary>
        public const float MinTop = 0.85f;
        /// <summary>Highest top that counts as counter height (m); the fridge and shelf tops (1.8 m) are above it (spec 039 Q2).</summary>
        public const float MaxTop = 1.10f;
        /// <summary>Spacing of the coverage test's sample grid (m).</summary>
        public const float SampleStep = 0.1f;
        /// <summary>A surface "is at" a top when its rest height is within this of the top (m).</summary>
        public const float HeightTolerance = 0.03f;
        /// <summary>The band around a listed part or a thing standing on the top where no item fits (m).</summary>
        public const float Rim = PlaceSpots.EdgeMargin + SampleStep * 0.3f;

        /// <summary>
        /// The parts of counter-height tops that are not place surfaces. An entry names an object: the object itself, or the name
        /// followed by a space and a number ("Burner 0"), and everything under it. <c>GuestHalfOnly</c>: only the half of that
        /// object's top on its local +z side (the front counter's street side; its cook side is local -z).
        /// </summary>
        public static readonly IReadOnlyList<(string Name, string Reason, bool GuestHalfOnly)> Exceptions = new (string, string, bool)[]
        {
            ("Burner", "each burner is its own one-spot surface (spec 021); the hob between and beside them belongs to the burners", false),
            ("Fryer basket", "the fryer's oil wells and baskets: food goes into a basket (spec 029); the wells lie below counter height", false),
            ("Fries box", "the fryer deck's display of empty fries boxes along its back edge (spec 029)", false),
            ("Griddle spot", "the griddle plate's six cooking spots (spec 030)", false),
            ("Basin", "the sink basin: a dirty dish goes in, a click scrubs (spec 035)", false),
            ("Faucet", "the sink's tap stands behind the basin (spec 035)", false),
            ("Spit cone", "the seitan spit's cone: a hold shaves it (spec 036)", false),
            ("Spit tray", "the spit's catch tray: the shavings land there (spec 036)", false),
            ("Build spot", "the assembly board's build spot: a wrap or box is built there (spec 031)", false),
            ("Tool", "the assembly board's sauce dispensers and packaging rack: a click squeezes or takes (spec 031)", false),
            ("Drink tool", "the drink dispenser's taps, cup stack and lid stack (spec 033)", false),
            ("Till", "the register's tray, buttons and card terminal (spec 032)", false),
            ("Counter", "the serving counter: a click there sells or serves (spec 009/022)", false),
            ("PlateStack", "the plate shelf's clean plates: a click takes one (spec 021/035)", false),
            ("Trash", "the trash can's lid: a click throws the held thing away (spec 035)", false),
            ("Front counter", "the front counter's guest half: customers stand there, only the cook side takes items (spec 039 Q1)", true),
        };

        public static bool IsCounterHeight(float top) => top >= MinTop - 0.0001f && top <= MaxTop + 0.0001f;

        /// <summary>The index of the exception that names <paramref name="objectName"/>, or -1.</summary>
        public static int ExceptionFor(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return -1;
            for (int i = 0; i < Exceptions.Count; i++)
                if (Names(objectName, Exceptions[i].Name)) return i;
            return -1;
        }

        /// <summary>"Burner" names "Burner" and "Burner 0", not "Burners" or "Burner ring".</summary>
        public static bool Names(string objectName, string entry)
        {
            if (objectName == null || entry == null || !objectName.StartsWith(entry, System.StringComparison.Ordinal)) return false;
            if (objectName.Length == entry.Length) return true;
            return objectName.Length > entry.Length + 1 && objectName[entry.Length] == ' ' && char.IsDigit(objectName[entry.Length + 1]);
        }

        /// <summary>
        /// Sample points of a top <paramref name="size"/> wide (x) and deep (y), relative to its centre: the middles of equal cells
        /// at most <see cref="SampleStep"/> on a side, at least one.
        /// </summary>
        public static List<Vector2> Grid(Vector2 size)
        {
            var points = new List<Vector2>();
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / SampleStep - 0.001f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(size.y / SampleStep - 0.001f));
            float stepX = size.x / nx, stepZ = size.y / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    points.Add(new Vector2(-size.x * 0.5f + (i + 0.5f) * stepX, -size.y * 0.5f + (j + 0.5f) * stepZ));
            return points;
        }
    }
}
