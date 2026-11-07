using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 019 AC6: the cut progress is bytes only, the stove lights sync one byte, and the knife and buttons
    /// add no request of their own (spec 021: the stove buttons use RequestStation(int, int), cookware RequestUse). Found by reflection and
    /// attribute name, so this test assembly needs no FishNet reference (like RpcSurfaceTests).
    /// </summary>
    public class HandsOnSurfaceTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Declared = Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        [Test]
        public void CutProgress_HasOnlyByteFields()
        {
            FieldInfo[] fields = typeof(CutProgress).GetFields(Instance);
            CollectionAssert.AreEquivalent(new[] { "Shape", "Turned" }, fields.Select(f => f.Name).ToArray());
            foreach (FieldInfo field in fields)
                Assert.AreEqual(typeof(byte), field.FieldType, "CutProgress." + field.Name);
            Assert.IsEmpty(typeof(CutProgress).GetProperties(Instance), "no instance properties besides the byte fields");
        }

        [Test]
        public void StoveFlags_IsASyncedByte()
        {
            FieldInfo field = typeof(StationContents).GetField("StoveFlags", Instance);
            Assert.IsNotNull(field);
            Assert.IsTrue(field.FieldType.IsGenericType && field.FieldType.Name.StartsWith("SyncVar"), field.FieldType.Name);
            Assert.AreEqual(typeof(byte), field.FieldType.GetGenericArguments()[0]);
        }

        [Test]
        public void StationRequest_StillTakesTwoInts()
        {
            Type interactor = typeof(StationRules).Assembly.GetType("Worktown.Game.Net.PickupInteractor");
            MethodInfo rpc = interactor.GetMethods(Declared)
                .Single(m => m.Name == "RequestStation" && m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            CollectionAssert.AreEqual(new[] { typeof(int), typeof(int) }, rpc.GetParameters().Select(p => p.ParameterType).ToArray());
        }

        [TestCase(typeof(Knife))]
        [TestCase(typeof(StationButton))]
        [TestCase(typeof(CutRules))]
        [TestCase(typeof(Vessel))]
        [TestCase(typeof(VesselRules))]
        public void HandsOnTypes_HaveNoRpcs(Type type)
        {
            string[] rpcs = type.GetMethods(Declared)
                .Where(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name.EndsWith("RpcAttribute")))
                .Select(m => m.Name).ToArray();
            Assert.IsEmpty(rpcs, type.Name + ": " + string.Join(", ", rpcs));
        }
    }
}
