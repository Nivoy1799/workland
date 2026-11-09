using System;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 017 AC3: the look clients get is bytes only (no float, string, stats or times), and the synced
    /// members of Food (and spec 021 Vessel) carry only names, stars and looks (no quality float, D21). Found by reflection, so this test
    /// assembly needs no FishNet reference.
    /// </summary>
    public class FoodLookSurfaceTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void FoodLook_HasOnlyByteFields()
        {
            FieldInfo[] fields = typeof(FoodLook).GetFields(Instance);
            Assert.IsNotEmpty(fields);
            foreach (FieldInfo field in fields)
                Assert.AreEqual(typeof(byte), field.FieldType, "FoodLook." + field.Name);
            Assert.IsEmpty(typeof(FoodLook).GetProperties(Instance), "no instance properties besides the byte fields");
            Assert.LessOrEqual(fields.Length, 6, "a small look: at most six bytes");
        }

        [Test]
        public void Food_SyncsTheLookAndItsParts_NotTheColourByte()
        {
            Type food = typeof(Food);
            Assert.AreEqual(typeof(FoodLook), SyncedArgument(food, "Look"));
            Assert.AreEqual(typeof(FoodLook), SyncedArgument(food, "Parts"));
            Assert.IsNull(food.GetField("Ingredient", Instance), "the colour-only byte was replaced by Look");
            Assert.IsNull(food.GetField("Quality", Instance), "clients see stars only, never the exact quality (D21)");
            Assert.IsNotNull(food.GetField("Stars", Instance));
        }

        [Test]
        public void SyncedMembers_CarryOnlyNamesStarsAndLooks()
        {
            // D21: clients see names, stars and the coarse look; no float (quality, stats, times) is synced.
            Type[] allowed = { typeof(string), typeof(int), typeof(FoodLook) };
            foreach (FieldInfo field in typeof(Food).GetFields(Instance))
            {
                Type argument = SyncArgument(field);
                if (argument != null) Assert.Contains(argument, allowed, "Food." + field.Name);
            }
            // Spec 019/021: the stove keeps only its flags byte (on, saute per burner); the contents moved to the cookware.
            foreach (FieldInfo field in typeof(StationContents).GetFields(Instance))
            {
                Type argument = SyncArgument(field);
                if (argument != null) Assert.AreEqual(typeof(byte), argument, "StationContents." + field.Name);
            }
            Assert.AreEqual(typeof(byte), SyncedArgument(typeof(StationContents), "StoveFlags"));
        }

        /// <summary>Spec 021 AC7: a Vessel syncs only bytes (the pan mode), strings (names) and FoodLook (the contents).
        /// Spec 027c: plus exactly two presentation bytes, the heat progress and zone (D21 amendment pending).</summary>
        [Test]
        public void Vessel_SyncsOnlyBytesStringsAndLooks()
        {
            Type vessel = VesselTestKit.VesselType;
            Assert.IsNotNull(vessel, "Vessel type missing");
            Type[] allowed = { typeof(byte), typeof(string), typeof(FoodLook) };
            int synced = 0;
            foreach (FieldInfo field in vessel.GetFields(Instance))
            {
                Type argument = SyncArgument(field);
                if (argument == null) continue;
                synced++;
                Assert.Contains(argument, allowed, "Vessel." + field.Name);
            }
            Assert.AreEqual(6, synced, "names, looks, title, mode, heat progress, heat zone");
            Assert.AreEqual(typeof(FoodLook), SyncedArgument(vessel, "Looks"));
            Assert.AreEqual(typeof(string), SyncedArgument(vessel, "Names"));
            Assert.AreEqual(typeof(byte), SyncedArgument(vessel, "Mode"));
            Assert.AreEqual(typeof(byte), SyncedArgument(vessel, "HeatProgress"));
            Assert.AreEqual(typeof(byte), SyncedArgument(vessel, "HeatZone"));
        }

        private static Type SyncArgument(FieldInfo field)
        {
            if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("Sync")) return null;
            return field.FieldType.GetGenericArguments()[0];
        }

        private static Type SyncedArgument(Type owner, string field)
        {
            FieldInfo info = owner.GetField(field, Instance);
            Assert.IsNotNull(info, owner.Name + "." + field + " missing");
            Assert.IsTrue(info.FieldType.IsGenericType, owner.Name + "." + field + " is not a sync type");
            return info.FieldType.GetGenericArguments()[0];
        }
    }
}
