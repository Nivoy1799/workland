using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027c: the client side of the cooking feedback, pure parts: the colour shift through the palette keys, the ring
    /// (visibility, fixed pixel size, view centre, mesh), the loops and cues, and the serve reward (tip, serial, timing).
    /// </summary>
    public class CookingFeedbackTests
    {
        private static readonly FoodLook Potato = new FoodLook(0, FoodLook.ShapeWhole, 0, 0, FoodLook.KindSingle, 0);

        private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        // ---- colour ----

        [Test]
        public void HeatTint_GoesThroughThePaletteKeys()
        {
            Color raw = FoodArtRules.IngredientColor(0);
            Assert.AreEqual(raw, FoodArtRules.HeatTint(Potato, true, HeatZone.Raw, 0f, false));
            Color golden = Color.Lerp(raw, Palette.Mustard, FoodArtRules.HeatGoldenMix);
            Assert.Less(Distance(golden, FoodArtRules.HeatTint(Potato, true, HeatZone.Good, HeatZoneRules.GoodShare, false)), 1e-4f);
            Assert.Less(Distance(FoodArtRules.HeatBrown, FoodArtRules.HeatTint(Potato, true, HeatZone.Perfect, 1f, false)), 1e-4f);
            Assert.AreEqual(Palette.Charcoal, FoodArtRules.HeatTint(Potato, true, HeatZone.Burnt, 0.2f, false), "burnt forces Charcoal");
            Color over = FoodArtRules.HeatTint(Potato, true, HeatZone.Overdone, 1f, false);
            Assert.Less(over.grayscale, FoodArtRules.HeatBrown.grayscale, "overdone is darker than brown");
            Assert.Greater(over.grayscale, Palette.Charcoal.grayscale);
            Assert.AreEqual(FoodArtRules.Tint(Potato, true), FoodArtRules.HeatTint(Potato, true, HeatZone.None, 0.7f, false), "off the heat: bucket colour");
        }

        [Test]
        public void HeatTint_ChangesVisiblyBetweenRingSteps_AndLessInThePot()
        {
            Color early = FoodArtRules.HeatTint(Potato, true, HeatZone.Cooking, 0.3f, false);
            Color late = FoodArtRules.HeatTint(Potato, true, HeatZone.Perfect, 0.9f, false);
            Assert.Greater(Distance(early, late), 0.15f, "the colour visibly changes between frames 20 s apart");
            Color raw = FoodArtRules.IngredientColor(0);
            Color fried = FoodArtRules.HeatTint(Potato, true, HeatZone.Perfect, 1f, false);
            Color boiled = FoodArtRules.HeatTint(Potato, true, HeatZone.Perfect, 1f, true);
            Assert.AreEqual(Distance(raw, fried) * FoodArtRules.HeatBoiledStrength, Distance(raw, boiled), 1e-3f);
            Assert.AreEqual(Color.white, FoodArtRules.HeatTint(Potato, false, HeatZone.Raw, 0f, false), "kit models keep their colours when raw");
            Assert.AreEqual(1f, FoodArtRules.HeatTint(Potato, true, HeatZone.Good, float.NaN, false).a);
        }

        // ---- ring ----

        [Test]
        public void Ring_ShowsOnHeatOrWarm_NotEmptyNotCarried()
        {
            Assert.IsTrue(HeatRingRules.IsShown(HeatZone.Cooking, false, true, 0f));
            Assert.IsTrue(HeatRingRules.IsShown(HeatZone.Good, false, false, HeatRingRules.WarmSeconds - 0.1f), "still warm");
            Assert.IsFalse(HeatRingRules.IsShown(HeatZone.Good, false, false, HeatRingRules.WarmSeconds + 0.1f), "cold");
            Assert.IsFalse(HeatRingRules.IsShown(HeatZone.Good, false, false, float.PositiveInfinity), "never on the heat");
            Assert.IsFalse(HeatRingRules.IsShown(HeatZone.None, false, true, 0f), "empty");
            Assert.IsFalse(HeatRingRules.IsShown(HeatZone.Good, true, true, 0f), "carried");
        }

        [Test]
        public void Ring_HasAFixedPixelSize_AndKeepsOffTheViewCentre()
        {
            float near = HeatRingRules.WorldPerPixel(1f, 60f, 720f);
            float far = HeatRingRules.WorldPerPixel(4f, 60f, 720f);
            Assert.AreEqual(4f * near, far, 1e-6f, "world size grows with distance, so the pixel size stays");
            Assert.AreEqual(2f * Mathf.Tan(30f * Mathf.Deg2Rad) / 720f, near, 1e-7f);
            Assert.AreEqual(0f, HeatRingRules.WorldPerPixel(-1f, 60f, 720f));
            var screen = new Vector2(1280, 720);
            Assert.IsTrue(HeatRingRules.CoversCentre(new Vector2(650, 370), 24f, screen));
            Assert.IsFalse(HeatRingRules.CoversCentre(new Vector2(640 + 24 + HeatRingRules.CentreMarginPixels + 1, 360), 24f, screen));
        }

        [Test]
        public void Ring_Mesh_FillGrowsWithProgress_InPaletteColours()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            HeatRingRules.Build(0f, HeatZone.Raw, v, c, t);
            int empty = v.Count;
            HeatRingRules.Build(0.5f, HeatZone.Cooking, v, c, t);
            int half = v.Count;
            HeatRingRules.Build(1f, HeatZone.Perfect, v, c, t);
            Assert.Greater(half, empty);
            Assert.Greater(v.Count, half);
            Assert.AreEqual(0, t.Count % 3);
            foreach (int i in t) Assert.That(i, Is.InRange(0, v.Count - 1));
            var allowed = new[] { HeatRingRules.Track, RingPalette.Current.Good, RingPalette.Current.Perfect, Palette.Mustard };
            foreach (Color color in c) CollectionAssert.Contains(allowed, color);
            foreach (Vector3 p in v) Assert.LessOrEqual(p.magnitude, 1.0001f);
            Assert.AreEqual(Palette.Cream, HeatRingRules.FillColor(HeatZone.Cooking));
            Assert.AreEqual(Palette.Ground, HeatRingRules.FillColor(HeatZone.Good));
            Assert.AreEqual(Palette.Terracotta, HeatRingRules.FillColor(HeatZone.Overdone));
            Assert.AreEqual(Palette.WoodDark, HeatRingRules.FillColor(HeatZone.Burnt));
            Assert.Less(Vector2.Distance(Vector2.up, HeatRingRules.Direction(0f)), 1e-5f, "starts at the top");
            Assert.Less(Vector2.Distance(Vector2.right, HeatRingRules.Direction(0.25f)), 1e-5f, "clockwise from the top");
        }

        // ---- sound ----

        [Test]
        public void Loops_FollowTheHeat()
        {
            Assert.AreEqual(0f, SoundRules.LoopLevel(false, HeatZone.Perfect), "off the heat: silent");
            Assert.Less(SoundRules.LoopLevel(true, HeatZone.Raw), SoundRules.LoopLevel(true, HeatZone.Cooking));
            Assert.Less(SoundRules.LoopLevel(true, HeatZone.Cooking), SoundRules.LoopLevel(true, HeatZone.Good));
            Assert.AreEqual(1f, SoundRules.LoopPitch(HeatZone.Perfect));
            Assert.Greater(SoundRules.LoopPitch(HeatZone.Overdone), 1f);
            Assert.Less(SoundRules.LoopPitch(HeatZone.Overdone), 1.15f, "slightly");
        }

        [Test]
        public void Cues_OncePerZoneEntry()
        {
            Assert.AreEqual(SoundKey.Perfect, SoundRules.CueFor(HeatZone.Good, false, HeatZone.Perfect, false));
            Assert.IsNull(SoundRules.CueFor(HeatZone.Perfect, false, HeatZone.Perfect, false), "staying");
            Assert.IsNull(SoundRules.CueFor(HeatZone.Overdone, false, HeatZone.Perfect, false), "going back");
            Assert.IsNull(SoundRules.CueFor(HeatZone.None, false, HeatZone.Perfect, false), "first seen (a join)");
            Assert.AreEqual(SoundKey.Warning, SoundRules.CueFor(HeatZone.Perfect, false, HeatZone.Perfect, true));
            Assert.IsNull(SoundRules.CueFor(HeatZone.Perfect, true, HeatZone.Perfect, true), "the warning stays on");
            Assert.AreEqual(SoundKey.Burnt, SoundRules.CueFor(HeatZone.Overdone, false, HeatZone.Burnt, false));
            Assert.IsNull(SoundRules.CueFor(HeatZone.Burnt, false, HeatZone.Burnt, false));
            Assert.IsNull(SoundRules.CueFor(HeatZone.Cooking, false, HeatZone.Good, false), "no cue for good");
            Assert.Greater(SoundRules.CueCooldown, 1f);
        }

        // ---- serve reward ----

        [Test]
        public void ServeReward_CountsSales_AndClamps()
        {
            ServeReward first = ServeReward.Next(default, 3, 4, 64, 4);
            Assert.AreEqual(1, first.Serial);
            Assert.AreEqual(3, first.Counter);
            Assert.AreEqual(4, first.Stars);
            ServeReward second = ServeReward.Next(first, 3, 9, -5, 10);
            Assert.AreEqual(2, second.Serial);
            Assert.AreEqual(StarRating.MaxStars, second.Stars);
            Assert.AreEqual(0, second.Paid);
            Assert.AreEqual(0, second.Tip, "never more tip than paid");
            Assert.AreEqual(1, ServeReward.Next(new ServeReward { Serial = int.MaxValue }, 0, 1, 1, 0).Serial, "wraps, never 0");
            Assert.AreEqual(int.MaxValue, ServeReward.Next(default, 0, 1, long.MaxValue, 0).Paid);
        }

        [Test]
        public void RewardMoment_StarsPopInTurn_TextShowsTheTip_ThenFades()
        {
            Assert.AreEqual(0f, ServeRewardRules.StarScale(1, 0.05f), "the second star waits its turn");
            Assert.Greater(ServeRewardRules.StarScale(0, ServeRewardRules.PopSeconds * 0.6f), 1.2f, "overshoots");
            Assert.AreEqual(1f, ServeRewardRules.StarScale(4, 2f));
            Assert.AreEqual(1f, ServeRewardRules.Alpha(0.5f));
            Assert.AreEqual(0f, ServeRewardRules.Alpha(ServeRewardRules.Seconds));
            Assert.Less(ServeRewardRules.Height(0.5f), ServeRewardRules.Height(1.5f));
            Assert.AreEqual("+$0.64 · tip $0.04", ServeRewardRules.MoneyText(ServeReward.Next(default, 0, 5, 64, 4))); // spec 037 re-pin: money is cents (D39)
            Assert.AreEqual("+$0.16", ServeRewardRules.MoneyText(ServeReward.Next(default, 0, 1, 16, 0)));
            Assert.AreEqual(Palette.Mustard, ServeRewardRules.MoneyColor);
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            ServeRewardRules.AddStar(Vector3.zero, 1f, Palette.Mustard, v, c, t);
            Assert.AreEqual(11, v.Count);
            Assert.AreEqual(30, t.Count);
        }
    }
}
