using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Art;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 026 AC2: every catalog model is built at its target size from <see cref="ArtSizeTable"/> (within 15 %), and the
    /// table agrees with the reference sizes in <c>docs/art-direction.md</c>.
    /// </summary>
    public class ArtSizeTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private static string GuidePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "art-direction.md"));

        [Test]
        public void Factors_AreThoseOfTheGuide()
        {
            Assert.AreEqual(1f, ArtSizeTable.Factor(SizeClass.World));
            Assert.AreEqual(1.15f, ArtSizeTable.Factor(SizeClass.Cookware));
            Assert.AreEqual(1.5f, ArtSizeTable.Factor(SizeClass.Small));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtSizeTable.Factor((SizeClass)99));

            // The class table of the guide: "| World (...) | x1.0 | real size |".
            var factors = new Dictionary<SizeClass, float>();
            foreach (string[] row in TableRows(File.ReadAllLines(GuidePath), "| Class | Factor | Rule |"))
            {
                SizeClass sizeClass = row[0].StartsWith("World") ? SizeClass.World
                    : row[0].StartsWith("Cookware") ? SizeClass.Cookware : SizeClass.Small;
                factors[sizeClass] = Number(row[1].TrimStart('x'));
            }
            Assert.AreEqual(3, factors.Count, "the guide's class table");
            foreach (KeyValuePair<SizeClass, float> f in factors)
                Assert.AreEqual(f.Value, ArtSizeTable.Factor(f.Key), 1e-6f, f.Key.ToString());
        }

        [Test]
        public void TargetSize_IsRealSizeTimesTheClassFactor()
        {
            // The examples of the spec.
            Assert.AreEqual(0.12f, ArtSizeTable.TargetSize(ArtKey.FoodApple), 1e-4f);
            Assert.AreEqual(0.5175f, ArtSizeTable.TargetSize(ArtKey.FoodPan), 1e-4f);
            Assert.AreEqual(0.345f, ArtSizeTable.TargetSize(ArtKey.FoodPot), 1e-4f);
            Assert.AreEqual(0.345f, ArtSizeTable.TargetSize(ArtKey.ToolKnife), 1e-4f);
            foreach (ArtKey house in new[] { ArtKey.BuildingHouseA, ArtKey.BuildingHouseC, ArtKey.BuildingHouseF, ArtKey.BuildingHouseK })
                Assert.AreEqual(7.5f, ArtSizeTable.TargetSize(house), 1e-4f, house.ToString());
            Assert.AreEqual(8f, ArtSizeTable.TargetSize(ArtKey.PropTreeLarge), 1e-4f);
            Assert.AreEqual(4.5f, ArtSizeTable.TargetSize(ArtKey.PropLamp), 1e-4f);
            Assert.AreEqual(1.75f, ArtSizeTable.TargetSize(ArtKey.CharacterPlayerMale), 1e-4f);
            Assert.AreEqual(ArtSizeTable.WorktopHeight, ArtSizeTable.TargetSize(ArtKey.StationPan), 1e-4f, "the stove top");
            Assert.AreEqual(ArtSizeTable.ServingCounterHeight, ArtSizeTable.TargetSize(ArtKey.StationCounter), 1e-4f);
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtSizeTable.TargetSize(ArtKey.Ground), "no model, no row");
            Assert.IsFalse(ArtSizeTable.TryGet(ArtKey.Ground, out _));
        }

        [Test]
        public void Table_HasOneRowPerKey_WithSensibleSizes()
        {
            var seen = new HashSet<ArtKey>();
            foreach (ArtSize row in ArtSizeTable.All)
            {
                Assert.IsTrue(seen.Add(row.Key), "two rows for " + row.Key);
                Assert.Greater(row.Real, 0f, row.Key.ToString());
                if (row.Class == SizeClass.Small) Assert.Less(row.Real, 0.15f, row.Key + ": small items are under 15 cm");
            }
        }

        [Test]
        public void Fits_AllowsFifteenPercent()
        {
            Assert.IsTrue(ArtSizeTable.Fits(1.15f, 1f));
            Assert.IsTrue(ArtSizeTable.Fits(0.85f, 1f));
            Assert.IsFalse(ArtSizeTable.Fits(1.16f, 1f));
            Assert.IsFalse(ArtSizeTable.Fits(0.84f, 1f));
            Assert.IsFalse(ArtSizeTable.Fits(0f, 0f));
        }

        [TestCase(2.48f, 4, 0.62f)]
        [TestCase(1.26f, 2, 0.63f)]
        [TestCase(1.76f, 3, 0.58667f)]
        [TestCase(0.6f, 1, 0.6f)]
        [TestCase(0.4f, 1, 0.4f)]
        [TestCase(0.95f, 2, 0.475f)]
        [TestCase(0.92f, 2, 0.46f)]
        public void WorktopModules_SplitsARunIntoEqualModulesOfAboutSixtyCentimetres(float length, int count, float width)
        {
            (int c, float w) = ArtSizeTable.WorktopModules(length);
            Assert.AreEqual(count, c);
            Assert.AreEqual(width, w, 1e-4f);
        }

        [Test]
        public void WorktopModules_AnyRunWorks_AndRunsOverNinetyCentimetresStayWithinTheSlack()
        {
            for (float length = 0.05f; length < 20f; length += 0.01f)
            {
                (int count, float width) = ArtSizeTable.WorktopModules(length);
                Assert.GreaterOrEqual(count, 1);
                Assert.AreEqual(length, count * width, 1e-4f, "the modules fill a " + length + " m run");
                if (length > 0.91f)
                    Assert.LessOrEqual(Mathf.Abs(width - ArtSizeTable.WorktopModuleWidth), ArtSizeTable.WorktopModuleSlack + 1e-4f, length + " m");
            }
        }

        [Test]
        public void WorktopModules_RejectsAnEmptyRun()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtSizeTable.WorktopModules(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtSizeTable.WorktopModules(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtSizeTable.WorktopModules(float.PositiveInfinity));
        }

        // AC2: the built wrappers.
        [Test]
        public void EveryCatalogModel_HasARow_AndIsBuiltAtItsTargetSize()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "ArtCatalog.asset missing; run Worktown/Build Graybox Scene");
            var failures = new List<string>();
            int checkedCount = 0;
            foreach (ArtCatalogEntry entry in catalog.Entries)
            {
                if (entry.Prefab == null) continue;
                if (!ArtSizeTable.TryGet(entry.Key, out ArtSize size))
                {
                    failures.Add(entry.Key + " has a catalog model but no row in ArtSizeTable");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab);
                try
                {
                    Transform model = instance.transform.Find("Model");
                    Assert.IsNotNull(model, entry.Key + " wrapper has no Model child");
                    Vector3 built = ModelBounds(model.gameObject).size;
                    float measured = size.Axis == SizeAxis.Height ? built.y : Mathf.Max(built.x, Mathf.Max(built.y, built.z));
                    checkedCount++;
                    if (!ArtSizeTable.Fits(measured, size.Target))
                        failures.Add(entry.Key + ": " + size.Axis + " " + measured.ToString("F3") + " m, target " + size.Target.ToString("F3") + " m");
                }
                finally { Object.DestroyImmediate(instance); }
            }
            Assert.Greater(checkedCount, 40, "the catalog has its models");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // AC2: the guide and the table agree.
        [Test]
        public void GuideTable_AgreesWithArtSizeTable()
        {
            Assert.IsTrue(File.Exists(GuidePath), GuidePath);
            var guide = new Dictionary<string, (List<float> Real, List<float> InGame)>();
            foreach (string[] row in TableRows(File.ReadAllLines(GuidePath), "| Thing | Real | In game | Notes |"))
            {
                List<float> real = Values(row[1]);
                List<float> inGame = row[2].Trim() == "same" ? real : Values(row[2]);
                guide[row[0]] = (real, inGame);
            }
            Assert.Greater(guide.Count, 15, "the guide's reference sizes");

            int compared = 0;
            foreach (ArtSize size in ArtSizeTable.All)
            {
                if (size.GuideRow == null) continue;
                Assert.IsTrue(guide.TryGetValue(size.GuideRow, out var row), size.Key + ": no guide row \"" + size.GuideRow + "\"");
                Assert.Less(size.GuideIndex, row.Real.Count, size.Key + ": the guide row has no value " + size.GuideIndex);
                Assert.AreEqual(row.Real[size.GuideIndex], size.Real, 0.001f, size.Key + ": real size differs from the guide");
                Assert.AreEqual(row.InGame[size.GuideIndex], size.Target, 0.006f, size.Key + ": size in the game differs from the guide");
                compared++;
            }
            Assert.Greater(compared, 30, "most keys come from a guide row");
        }

        [Test]
        public void GuideValues_AreParsed()
        {
            CollectionAssert.AreEqual(new[] { 4f, 8f }, Values("4 / 8"));
            CollectionAssert.AreEqual(new[] { 7.5f }, Values("7–8"));
            CollectionAssert.AreEqual(new[] { 0.9f, 0.6f }, Values("0.90 high, 0.60 deep"));
            CollectionAssert.AreEqual(new[] { 0.135f }, Values("0.135"));
        }

        /// <summary>The cells (without the outer pipes, trimmed) of the markdown table under <paramref name="header"/>.</summary>
        internal static List<string[]> TableRows(string[] lines, string header)
        {
            var rows = new List<string[]>();
            int start = Array.FindIndex(lines, l => l.Trim().StartsWith(header));
            Assert.GreaterOrEqual(start, 0, "table \"" + header + "\" not found in the guide");
            for (int i = start + 2; i < lines.Length && lines[i].TrimStart().StartsWith("|"); i++)
            {
                string[] cells = lines[i].Trim().Trim('|').Split('|');
                for (int c = 0; c < cells.Length; c++) cells[c] = cells[c].Trim();
                rows.Add(cells);
            }
            return rows;
        }

        /// <summary>Numbers of a guide cell: "4 / 8" gives two, "7–8" the middle of the range, "0.90 high, 0.60 deep" both.</summary>
        private static List<float> Values(string cell)
        {
            var result = new List<float>();
            foreach (string part in cell.Split('/'))
            {
                MatchCollection numbers = Regex.Matches(part, @"\d+(?:\.\d+)?");
                if (numbers.Count == 2 && part.Contains("–"))
                {
                    result.Add((Number(numbers[0].Value) + Number(numbers[1].Value)) * 0.5f);
                    continue;
                }
                foreach (Match m in numbers) result.Add(Number(m.Value));
            }
            return result;
        }

        private static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>World bounds of a model: baked meshes for skinned people (their renderer bounds are loose), else the renderers.</summary>
        private static Bounds ModelBounds(GameObject model)
        {
            bool any = false;
            var b = new Bounds();
            var baked = new Mesh();
            try
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        skin.BakeMesh(baked, true);
                        foreach (Vector3 v in baked.vertices)
                        {
                            Vector3 w = skin.transform.TransformPoint(v);
                            if (!any) b = new Bounds(w, Vector3.zero);
                            else b.Encapsulate(w);
                            any = true;
                        }
                    }
                    else
                    {
                        if (!any) b = renderer.bounds;
                        else b.Encapsulate(renderer.bounds);
                        any = true;
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            Assert.IsTrue(any, model.name + " has no renderer");
            return b;
        }
    }
}
