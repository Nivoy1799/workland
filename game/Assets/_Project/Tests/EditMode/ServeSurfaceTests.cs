using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Review of spec 027c: the serve feed's synced surface is pinned like FoodLookSurfaceTests pins Food and Vessel. ServeFeed
    /// syncs exactly two members (the feed lines and the last reward), and ServeReward has exactly the five fields the
    /// reward moment shows (serial, counter, stars, paid, tip), no float and no quality (D21). By reflection, so the
    /// test does not depend on FishNet's types.
    /// </summary>
    public class ServeSurfaceTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type SyncArgument(FieldInfo field)
        {
            if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("Sync")) return null;
            return field.FieldType.GetGenericArguments()[0];
        }

        [Test]
        public void ServeFeed_SyncsExactlyTheLinesAndTheLastReward()
        {
            FieldInfo[] synced = typeof(ServeFeed).GetFields(Instance).Where(f => SyncArgument(f) != null).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Entries", "LastReward" }, synced.Select(f => f.Name).ToArray());
            Assert.AreEqual(typeof(string), SyncArgument(typeof(ServeFeed).GetField("Entries", Instance)));
            Assert.AreEqual(typeof(ServeReward), SyncArgument(typeof(ServeFeed).GetField("LastReward", Instance)));
        }

        [Test]
        public void ServeReward_HasExactlyFiveSmallFields()
        {
            FieldInfo[] fields = typeof(ServeReward).GetFields(Instance);
            CollectionAssert.AreEquivalent(new[] { "Serial", "Counter", "Stars", "Paid", "Tip" }, fields.Select(f => f.Name).ToArray());
            foreach (FieldInfo field in fields)
                Assert.IsTrue(field.FieldType == typeof(int) || field.FieldType == typeof(byte), "ServeReward." + field.Name + " is " + field.FieldType.Name);
            Assert.AreEqual(typeof(byte), typeof(ServeReward).GetField("Stars").FieldType);
            Assert.IsEmpty(typeof(ServeReward).GetProperties(Instance), "no hidden state behind properties");
        }
    }
}
