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
    /// Spec 021 AC7 (changed on purpose): one more request, RequestUse(NetworkObject target, int action).
    /// Spec 054b option (a) (changed on purpose): MoveData gains ViewTick (uint), the server tick the owner's drawn players stand for;
    /// the server clamps it and uses it only for the separation push (LagCompensation).
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
            // Spec 010 fields plus FishNet's tick, plus FirstPerson (spec 018), plus ViewTick (spec 054b option (a)). Nothing else.
            string[] expected = { "CameraYaw", "Crouch", "FirstPerson", "Jump", "Move", "Sprint", "ViewTick", "_tick" };
            CollectionAssert.AreEqual(expected.OrderBy(n => n).ToArray(), names, string.Join(", ", names));
        }

        [Test]
        public void MoveData_ViewTickIsAUint()
        {
            FieldInfo field = Find(MoveDataType).GetField("ViewTick", Fields);
            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(uint), field.FieldType, "a server tick");
            Assert.IsTrue(field.IsPublic);
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
        [TestCase("RequestUse", 2)]
        // Spec 045: each request also carries its number last (an int the server only echoes in its answer), so the counts are + 1.
        public void InteractorRequests_KeepTheirArguments(string rpc, int parameters)
        {
            MethodInfo method = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == rpc)
                .SingleOrDefault(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            Assert.IsNotNull(method, rpc + " is not a ServerRpc");
            ParameterInfo[] all = method.GetParameters();
            Assert.AreEqual(parameters + 1, all.Length, rpc);
            Assert.AreEqual(typeof(int), all[all.Length - 1].ParameterType, rpc + ": the request number");
            Assert.AreEqual("request", all[all.Length - 1].Name, rpc);
        }

        [Test]
        public void RequestPlace_TakesOnlyInts()
        {
            MethodInfo method = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == "RequestPlace" && m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            CollectionAssert.AreEqual(new[] { typeof(int), typeof(int), typeof(int) }, method.GetParameters().Select(p => p.ParameterType).ToArray(),
                "surface and spot index (and spec 045's request number) only, never a position");
        }

        /// <summary>Spec 021 AC7: the use request names an object and an action number, never a value or an item.</summary>
        [Test]
        public void RequestUse_TakesOnlyANetworkObjectAndAnInt()
        {
            MethodInfo method = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == "RequestUse" && m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual("FishNet.Object.NetworkObject", parameters[0].ParameterType.FullName, "the target cookware");
            Assert.AreEqual(typeof(int), parameters[1].ParameterType, "0 use, 1 stroke, 2 turn");
            Assert.AreEqual(typeof(int), parameters[2].ParameterType, "spec 045: the request number");
        }

        [Test]
        public void Interactor_HasExactlyTheSpec021ServerRpcs()
        {
            string[] rpcs = Find(InteractorType)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"))
                .Select(m => m.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "RequestDrop", "RequestPickup", "RequestPlace", "RequestStation", "RequestUse" }, rpcs, string.Join(", ", rpcs));
        }
    }
}
