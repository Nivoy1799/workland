using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 018 AC6: first person adds exactly one input to the predicted movement, a bool view hint named
    /// FirstPerson, and no request gained an argument (the client still sends only intents). Found by
    /// name and attribute name, so this test assembly needs no FishNet reference (like RpcSurfaceTests).
    /// Spec 020 AC6 (changed on purpose): one new request, RequestPlace(int surface, int spot); MoveData unchanged.
    /// </summary>
    public class MoveDataSurfaceTests
    {
        private const string MoveDataType = "Worktown.Game.Net.PlayerController+MoveData";
        private const string InteractorType = "Worktown.Game.Net.PickupInteractor";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type Find(string fullName)
        {
            Type type = typeof(StationRules).Assembly.GetType(fullName);
            Assert.IsNotNull(type, fullName + " not found");
            return type;
        }

        [Test]
        public void MoveData_HasExactlyTheSpec018Fields()
        {
            string[] names = Find(MoveDataType).GetFields(Fields).Select(f => f.Name).OrderBy(n => n).ToArray();
            // Spec 010 fields plus FishNet's tick, plus FirstPerson (spec 018). Nothing else.
            string[] expected = { "CameraYaw", "Crouch", "FirstPerson", "Jump", "Move", "Sprint", "_tick" };
            CollectionAssert.AreEqual(expected.OrderBy(n => n).ToArray(), names, string.Join(", ", names));
        }

        [Test]
        public void MoveData_FirstPersonIsABool()
        {
            FieldInfo field = Find(MoveDataType).GetField("FirstPerson", Fields);
            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(bool), field.FieldType);
            Assert.IsTrue(field.IsPublic);
        }

        [TestCase("RequestPickup", 1)]
        [TestCase("RequestDrop", 0)]
        [TestCase("RequestStation", 2)]
        [TestCase("RequestPlace", 2)]
        public void InteractorRequests_KeepTheirArguments(string rpc, int parameters)
        {
            MethodInfo method = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == rpc)
                .SingleOrDefault(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            Assert.IsNotNull(method, rpc + " is not a ServerRpc");
            Assert.AreEqual(parameters, method.GetParameters().Length, rpc);
        }

        [Test]
        public void RequestPlace_TakesOnlyInts()
        {
            MethodInfo method = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == "RequestPlace" && m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            CollectionAssert.AreEqual(new[] { typeof(int), typeof(int) }, method.GetParameters().Select(p => p.ParameterType).ToArray(),
                "surface and spot index only, never a position");
        }

        [Test]
        public void Interactor_HasExactlyTheSpec020ServerRpcs()
        {
            string[] rpcs = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"))
                .Select(m => m.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "RequestDrop", "RequestPickup", "RequestPlace", "RequestStation" }, rpcs, string.Join(", ", rpcs));
        }
    }
}
