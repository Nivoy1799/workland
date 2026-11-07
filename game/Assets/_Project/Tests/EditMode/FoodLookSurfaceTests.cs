using System;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 017 AC3: the look clients get is bytes only (no float, string, stats or times), and the synced
    /// members of Food and StationContents carry only names, stars and looks (no quality float, D21). Found by reflection, so this test
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
            foreach (Type type in new[] { typeof(Food), typeof(StationContents) })
            {
                foreach (FieldInfo field in type.GetFields(Instance))
                {
                    if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("Sync")) continue;
                    Type argument = field.FieldType.GetGenericArguments()[0];
                    // Spec 019: the stove flags byte (on, saute) for the button lights is the one byte allowed.
                    if (type == typeof(StationContents) && field.Name == "StoveFlags" && argument == typeof(byte)) continue;
                    Assert.Contains(argument, allowed, type.Name + "." + field.Name);
                }
            }
            Assert.AreEqual(typeof(FoodLook), SyncedArgument(typeof(StationContents), "Looks"));
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
