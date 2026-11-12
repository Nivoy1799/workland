using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 016 AC5: no cooking ServerRpc sends a temperature, a time, an amount, a text or an item from
    /// the client. Clients send intents (station index, option), the server decides. Found by attribute
    /// name, so it does not depend on FishNet's types.
    /// </summary>
    public class RpcSurfaceTests
    {
        private const string InteractorType = "Worktown.Game.Net.PickupInteractor";
        private static readonly Type[] Forbidden = { typeof(float), typeof(double), typeof(long), typeof(string), typeof(Item) };

        private static IEnumerable<MethodInfo> ServerRpcs()
        {
            return typeof(StationRules).Assembly.GetTypes()
                .Where(t => t.Namespace == "Worktown.Game.Cooking" || t.FullName == InteractorType)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "ServerRpcAttribute"));
        }

        [Test]
        public void StationRequest_IsAServerRpc()
        {
            Assert.IsTrue(ServerRpcs().Any(m => m.DeclaringType.FullName == InteractorType && m.Name == "RequestStation"));
        }

        [Test]
        public void CookingRpcs_CarryNoValuesOrItems()
        {
            foreach (MethodInfo rpc in ServerRpcs())
                foreach (ParameterInfo p in rpc.GetParameters())
                    Assert.IsFalse(Forbidden.Contains(p.ParameterType),
                        rpc.DeclaringType.Name + "." + rpc.Name + "(" + p.ParameterType.Name + " " + p.Name + ")");
        }
    }
}
