using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027d AC4: customer looks are distinct, deterministic per id and built from the palette.</summary>
    public class CustomerLooksTests
    {
        private static bool InHair(float x, float y) => x < 640f && y < 100f;
        private static bool InShirt(float x, float y) => x < 640f && y >= 490f && !InTrousers(x, y);
        private static bool InTrousers(float x, float y) => x >= 611f && y >= 764f;

        // A tiny stand-in for the Kenney atlas: skin everywhere, dark hair, red shirt, blue (male) or black (female) trousers.
        private static Color32[] Atlas(int size, bool female)
        {
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float x = (i % size) * 1024f / size, y = (size - 1 - i / size) * 1024f / size;
                Color c = new Color(0.97f, 0.58f, 0.45f);
                if (InHair(x, y)) c = new Color(0.3f, 0.09f, 0.07f);
                if (InShirt(x, y)) c = new Color(0.92f, 0.3f, 0.25f);
                if (InTrousers(x, y)) c = female ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.1f, 0.37f, 0.54f);
                pixels[i] = c;
            }
            return pixels;
        }

        [Test]
        public void ThereAreAtLeastFourLooks_AllDifferent_OnePerCustomerKey()
        {
            Assert.GreaterOrEqual(CustomerLooks.All.Count, 4);
            Assert.AreEqual(ArtCatalogRules.CustomerCharacterKeys().Length, CustomerLooks.All.Count);
            var seen = new HashSet<string>();
            var skins = new HashSet<string>();
            for (int i = 0; i < CustomerLooks.All.Count; i++)
            {
                CustomerLook look = CustomerLooks.All[i];
                Assert.AreEqual(ArtCatalogRules.CustomerCharacterKeys()[i], look.Key, "variant order");
                Assert.IsTrue(seen.Add(look.BaseSkin + look.Shirt + look.Trousers + look.Hair), look.Key + " repeats another look");
                Assert.IsTrue(skins.Add(look.SkinName), look.Key + " shares a texture");
            }
        }

        [Test]
        public void NeighbouringIds_GetDifferentLooks_AndTheLookIsStablePerId()
        {
            for (int id = 0; id < 40; id++)
            {
                Assert.AreSame(CustomerLooks.ForCustomer(id), CustomerLooks.ForCustomer(id), "stable");
                Assert.AreNotSame(CustomerLooks.ForCustomer(id), CustomerLooks.ForCustomer(id + 1), "neighbours differ");
            }
            var shown = new HashSet<CustomerLook>();
            for (int id = 100; id < 106; id++) shown.Add(CustomerLooks.ForCustomer(id));
            Assert.GreaterOrEqual(shown.Count, 4, "six guests in a row show at least four looks");
        }

        [Test]
        public void Recolour_PaintsShirtTrousersAndHair_AndLeavesSkinAlone()
        {
            const int size = 128;
            foreach (bool female in new[] { false, true })
            {
                Color32[] source = Atlas(size, female);
                Color32[] result = SkinRecolourRules.Apply(source, size, female, Palette.Ground, Palette.Mustard, Palette.Ink);
                int changed = 0;
                for (int i = 0; i < source.Length; i++)
                {
                    float x = (i % size) * 1024f / size, y = (size - 1 - i / size) * 1024f / size;
                    bool skin = !InHair(x, y) && !InShirt(x, y) && !InTrousers(x, y);
                    if (skin) Assert.AreEqual(source[i], result[i], "skin stays");
                    else if (!result[i].Equals(source[i])) changed++;
                }
                Assert.Greater(changed, 100, "shirt, trousers and hair are painted");
                Color shirt = result[(size - 1 - (int)(700f / 1024f * size)) * size + (int)(200f / 1024f * size)];
                Color.RGBToHSV(shirt, out float h, out _, out _);
                Color.RGBToHSV(Palette.Ground, out float sageHue, out _, out _);
                Assert.AreEqual(sageHue, h, 0.02f, "the shirt takes the palette hue");
            }
        }

        [Test]
        public void Recolour_WithNoColours_ChangesNothing()
        {
            Color32[] source = Atlas(64, false);
            Assert.AreEqual(source, SkinRecolourRules.Apply(source, 64, false, null, null, null));
        }
    }
}
