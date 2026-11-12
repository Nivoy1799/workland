using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 026 AC3: one palette (D32). <see cref="Palette"/> has the town colours of <c>docs/art-direction.md</c> with their
    /// hex values, <c>UI/Palette.uss</c> defines the same as variables, and no other script or stylesheet holds a colour.
    /// </summary>
    public class PaletteTests
    {
        private static string ProjectFolder => Path.Combine(Application.dataPath, "_Project");
        private static string GuidePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "art-direction.md"));
        private static string PaletteUssPath => Path.Combine(ProjectFolder, "UI", "Palette.uss");

        /// <summary>The guide's colour table: name, RGB hex and alpha ("at 85 %").</summary>
        private static List<(string Name, string Hex, float Alpha)> GuideColours()
        {
            var colours = new List<(string, string, float)>();
            foreach (string[] row in ArtSizeTests.TableRows(File.ReadAllLines(GuidePath), "| Name | Hex | Use |"))
            {
                // A row may hold several colours: "Leaf, Plant" with "`#5E8F4F`, `#3F6B3A`" gives two entries.
                string[] names = row[0].Split(',');
                MatchCollection hexes = Regex.Matches(row[1], "#([0-9A-Fa-f]{6})");
                Assert.AreEqual(names.Length, hexes.Count, "one hex per name for " + row[0]);
                Match alpha = Regex.Match(row[1], @"at (\d+) ?%");
                for (int n = 0; n < names.Length; n++)
                    colours.Add((names[n].Trim(), "#" + hexes[n].Groups[1].Value.ToUpperInvariant(),
                        alpha.Success ? int.Parse(alpha.Groups[1].Value, CultureInfo.InvariantCulture) / 100f : 1f));
            }
            return colours;
        }

        [Test]
        public void Palette_HasTheGuidesTownColours_WithTheGuidesHexValues()
        {
            List<(string Name, string Hex, float Alpha)> guide = GuideColours();
            Assert.AreEqual(21, guide.Count, "the guide's town palette: 19 rows, 21 colours");
            Assert.AreEqual(guide.Count, Palette.GuideColours.Count);
            for (int i = 0; i < guide.Count; i++)
            {
                (string name, Color color) = Palette.GuideColours[i];
                Assert.AreEqual(guide[i].Name, name, "order and names as in the guide");
                Assert.AreEqual(guide[i].Hex, Palette.Hex(color), name);
                Assert.AreEqual(guide[i].Alpha, color.a, 0.002f, name + " opacity");
                var field = typeof(Palette).GetField(name);
                Assert.IsNotNull(field, "Palette." + name);
                Assert.AreEqual(color, (Color)field.GetValue(null), name);
            }
            Assert.AreEqual(0.85f, Palette.Panel.a, 0.001f);
        }

        [Test]
        public void PaletteUss_DefinesTheSameColoursAsVariables()
        {
            string uss = File.ReadAllText(PaletteUssPath);
            var variables = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(uss, @"--wt-([a-z0-9-]+)\s*:\s*([^;]+);")) variables[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            foreach ((string name, Color color) in System.Linq.Enumerable.Concat(Palette.GuideColours, Palette.UiColours))
            {
                string variable = Kebab(name);
                Assert.IsTrue(variables.TryGetValue(variable, out string value), "Palette.uss has no --wt-" + variable);
                Color parsed = ParseUssColour(value);
                Assert.AreEqual(Palette.Hex(color), Palette.Hex(parsed), "--wt-" + variable);
                Assert.AreEqual(color.a, parsed.a, 0.005f, "--wt-" + variable + " opacity");
            }
        }

        [Test]
        public void WorktownUss_UsesOnlyPaletteVariables_ThatPaletteUssDefines()
        {
            string palette = File.ReadAllText(PaletteUssPath);
            string worktown = File.ReadAllText(Path.Combine(ProjectFolder, "UI", "Worktown.uss"));
            StringAssert.Contains("@import url(\"Palette.uss\")", worktown);
            StringAssert.Contains("@import url(\"Palette.uss\")", File.ReadAllText(Path.Combine(ProjectFolder, "UI", "WorktownTheme.tss")),
                "the theme puts the variables at the panel root");
            MatchCollection uses = Regex.Matches(worktown, @"var\(\s*--([a-z0-9-]+)\s*\)");
            Assert.Greater(uses.Count, 10);
            foreach (Match use in uses)
                StringAssert.Contains("--" + use.Groups[1].Value + ":", palette, "Worktown.uss uses an undefined variable");
        }

        /// <summary>A colour built in C#: <c>new Color(</c>, <c>new UnityEngine.Color32(</c>, or a target-typed <c>new(</c> into a Color.</summary>
        private static readonly Regex ColourConstructor = new Regex(
            @"new\s+(?:UnityEngine\s*\.\s*)?Color(?:32)?\s*\(" +
            @"|\bColor(?:32)?\??(?:\s*\[\s*\])?\s+\w+\s*(?:=>|=)\s*(?:\{[^}]*)?\bnew\s*\(");

        /// <summary>CSS colour keywords; colours must come from the palette variables.</summary>
        private static readonly HashSet<string> NamedColours = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "white", "black", "red", "green", "blue", "yellow", "orange", "purple", "pink", "gray", "grey", "cyan", "magenta",
            "brown", "lime", "navy", "teal", "olive", "maroon", "silver", "gold", "aqua", "fuchsia", "beige", "ivory", "khaki",
            "violet", "indigo", "crimson", "coral", "salmon", "tan", "turquoise", "lavender", "chocolate", "tomato", "wheat"
        };

        private static readonly Regex HexOrRgb = new Regex(@"(?<![\w-])#[0-9A-Fa-f]{3,8}(?![\w-])|rgba?\s*\(");

        [TestCase("var c = new Color(1f, 0f, 0f);", true)]
        [TestCase("Color32 p = new Color32(255, 255, 255, 0);", true)]
        [TestCase("var c = new UnityEngine.Color(1f, 0f, 0f);", true)]
        [TestCase("private static readonly Color Red = new(1f, 0f, 0f);", true)]
        [TestCase("Color32 pixel = new (255, 255, 255, 0);", true)]
        [TestCase("public static Color Red => new(1f, 0f, 0f);", true)]
        [TestCase("Color[] all = { new(1f, 0f, 0f) };", true)]
        [TestCase("var pixels = new Color32[n * n];", false)]
        [TestCase("Color c = Palette.WithAlpha(Palette.Ground, 0.5f);", false)]
        [TestCase("var block = new MaterialPropertyBlock();", false)]
        public void ColourConstructorPattern_FindsEveryForm(string line, bool found)
        {
            Assert.AreEqual(found, ColourConstructor.IsMatch(line), line);
        }

        [Test]
        public void NoScriptOutsideThePalette_ConstructsAColour()
        {
            var offenders = new List<string>();
            string palette = Path.GetFullPath(Path.Combine(ProjectFolder, "Scripts", "Art", "Palette.cs"));
            var files = new List<string>(Directory.GetFiles(Path.Combine(ProjectFolder, "Scripts"), "*.cs", SearchOption.AllDirectories));
            Assert.Greater(files.Count, 50);
            files.AddRange(Directory.GetFiles(Path.Combine(ProjectFolder, "Editor"), "*.cs", SearchOption.AllDirectories));
            foreach (string file in files)
            {
                if (string.Equals(Path.GetFullPath(file), palette, StringComparison.OrdinalIgnoreCase)) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (ColourConstructor.IsMatch(lines[i])) offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + ": " + lines[i].Trim());
            }
            Assert.IsEmpty(offenders, "use a Palette colour (WithAlpha / Shade for variants):\n" + string.Join("\n", offenders));
        }

        /// <summary>Colour values in a stylesheet (comments, var() and url() removed): hex, rgb(a) and colour keywords.</summary>
        private static List<string> ColourValues(string stylesheet, bool allowHexAndRgb)
        {
            var found = new List<string>();
            string text = Regex.Replace(stylesheet, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            text = Regex.Replace(text, @"(?:var|url)\s*\([^)]*\)", string.Empty);
            if (!allowHexAndRgb)
                foreach (Match m in HexOrRgb.Matches(text)) found.Add(m.Value);
            foreach (Match declaration in Regex.Matches(text, @"(-?[A-Za-z][\w-]*)\s*:\s*([^;{}]+);"))
                foreach (Match word in Regex.Matches(declaration.Groups[2].Value, @"[A-Za-z]+"))
                    if (NamedColours.Contains(word.Value)) found.Add(declaration.Value.Trim());
            return found;
        }

        [TestCase(".a { color: white; }", false, 1)]
        [TestCase(".a { border-color: Red; }", false, 1)]
        [TestCase(".a { color: #fff; }", false, 1)]
        [TestCase(".a { background-color: rgba(0, 0, 0, 0.5); }", false, 1)]
        [TestCase(".a { color: var(--wt-sky); -unity-font-style: bold; }", false, 0)]
        [TestCase("@import url(\"unity-theme://default\");\n#fade-box { color: var(--wt-ink); }", false, 0)]
        [TestCase(":root { --wt-cream: #F3E9D2; }", true, 0)]
        public void ColourValuePattern_FindsHexRgbAndKeywords(string stylesheet, bool allowHexAndRgb, int count)
        {
            Assert.AreEqual(count, ColourValues(stylesheet, allowHexAndRgb).Count, stylesheet);
        }

        [Test]
        public void NoStylesheetOutsideThePalette_HoldsAColourValue()
        {
            var offenders = new List<string>();
            var files = new List<string>(Directory.GetFiles(ProjectFolder, "*.uss", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(ProjectFolder, "*.tss", SearchOption.AllDirectories));
            Assert.GreaterOrEqual(files.Count, 3);
            foreach (string file in files)
                foreach (string value in ColourValues(File.ReadAllText(file), Path.GetFileName(file) == "Palette.uss"))
                    offenders.Add(Path.GetFileName(file) + ": " + value);
            Assert.IsEmpty(offenders, "use a --wt-* variable from Palette.uss:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void PaletteUss_DefinesOnlyTheTownAndUiColours()
        {
            var expected = new List<string>();
            foreach ((string name, Color _) in Palette.GuideColours) expected.Add(Kebab(name));
            foreach ((string name, Color _) in Palette.UiColours) expected.Add(Kebab(name));
            var defined = new List<string>();
            foreach (Match m in Regex.Matches(File.ReadAllText(PaletteUssPath), @"--wt-([a-z0-9-]+)\s*:")) defined.Add(m.Groups[1].Value);
            CollectionAssert.AreEquivalent(expected, defined, "Palette.uss holds exactly the guide's town colours plus the three UI colours");
        }

        [Test]
        public void FoodColours_AreMuted()
        {
            Assert.GreaterOrEqual(Palette.FoodColours.Count, 4);
            foreach ((string name, Color color) in Palette.FoodColours)
            {
                Color.RGBToHSV(color, out _, out float saturation, out _);
                Assert.LessOrEqual(saturation, Palette.MaxFoodSaturation + 1e-4f, name);
            }
            Assert.AreEqual(Palette.FoodPotato, FoodArtRules.IngredientColor(0));
            Assert.AreEqual(Palette.Chrome, FoodArtRules.IngredientColor(200), "unknown ingredient");
        }

        [Test]
        public void CustomerTints_PickFromTheGuidesSix_AndWrap()
        {
            Color[] expected = { Palette.Cream, Palette.Terracotta, Palette.Mustard, Palette.Ground, Palette.Sky, Palette.DeepBlue };
            Assert.AreEqual(expected.Length, Palette.CustomerTintCount);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], Palette.Customer(i));
                Assert.AreEqual(expected[i], Palette.Customer(i + expected.Length), "wraps");
                Assert.AreEqual(expected[i], Palette.Customer(i - expected.Length), "negative wraps");
            }
        }

        [Test]
        public void WithAlphaAndShade_KeepThePaletteHue()
        {
            Color c = Palette.WithAlpha(Palette.Ground, 0.45f);
            Assert.AreEqual(0.45f, c.a, 1e-6f);
            Assert.AreEqual(Palette.Hex(Palette.Ground), Palette.Hex(c));
            Color dark = Palette.Shade(Palette.WithAlpha(Palette.WoodDark, 0.2f), 0.5f);
            Assert.AreEqual(Palette.WoodDark.r * 0.5f, dark.r, 1e-6f);
            Assert.AreEqual(Palette.WoodDark.b * 0.5f, dark.b, 1e-6f);
            Assert.AreEqual(1f, dark.a, "shaded colours are opaque");
            Assert.AreEqual("#F3E9D2", Palette.Hex(Palette.Cream));
        }

        /// <summary>"SoftBlue" to "soft-blue".</summary>
        private static string Kebab(string name) => Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

        private static Color ParseUssColour(string value)
        {
            Match rgba = Regex.Match(value, @"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)");
            if (rgba.Success)
                return Palette.WithAlpha(Rgb(Byte(rgba.Groups[1].Value), Byte(rgba.Groups[2].Value), Byte(rgba.Groups[3].Value)),
                    float.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture));
            Assert.IsTrue(ColorUtility.TryParseHtmlString(value, out Color c), "not a colour: " + value);
            return c;
        }

        private static int Byte(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        private static Color Rgb(int r, int g, int b)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString("#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"), out Color c));
            return c;
        }
    }
}
