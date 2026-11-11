using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 047 AC1: only connection messages were added to the network surface: three broadcasts with exactly these fields (no game
    /// data: no positions, money, items), and no RPC carries them. AC2: the build hash (Rules.BuildHash over ProtocolSurface) is stable
    /// and follows the surface. Found by reflection and type names, so this test assembly needs no FishNet reference.
    /// Follow-up 047 (2026-11-11): the session ban's refusal is a new reason byte (KickedBefore = 10) inside the existing
    /// ConnectionNotice; no broadcast, RPC or synced field changed (the reason bytes are pinned in Followup047Tests).
    /// </summary>
    public class ConnectionSurfaceTests
    {
        private static Assembly Game => typeof(NetBootstrap).Assembly;

        private static IEnumerable<Type> Broadcasts() =>
            Game.GetTypes().Where(t => t.IsValueType && t.GetInterfaces().Any(i => i.Name == "IBroadcast"));

        private static string Fields(Type type) =>
            string.Join(";", type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => ProtocolSurface.Name(f.FieldType) + " " + f.Name));

        [Test]
        public void TheOnlyBroadcasts_AreTheThreeConnectionMessages()
        {
            CollectionAssert.AreEquivalent(new[] { "ConnectionNotice", "JoinRequest", "LobbyRoster" }, Broadcasts().Select(t => t.Name).ToArray());
            Assert.AreEqual("System.String Build;System.String Name", Fields(typeof(JoinRequest)));
            Assert.AreEqual("System.Byte Reason;System.String Detail", Fields(typeof(ConnectionNotice)));
            Assert.AreEqual("System.Int32[] Ids;System.String[] Names;System.Int32 HostId;System.Int32 MaxPlayers", Fields(typeof(LobbyRoster)));
        }

        [Test]
        public void ANewReason_RidesInTheNotice_AndChangesNoSurfaceLine()
        {
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.Contains("broadcast Worktown.Game.Net.ConnectionNotice{System.Byte Reason;System.String Detail;}", lines);
            Assert.IsFalse(lines.Any(l => l.Contains("KickedBefore") || l.Contains("DisconnectReason")),
                "reason values are not part of the surface: an older client shows an unknown reason by its byte");
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
        }

        [Test]
        public void TheSurface_ListsRpcsSyncedFieldsAndBroadcasts()
        {
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.IsTrue(lines.Any(l => l.StartsWith("ServerRpc Worktown.Game.Net.PickupInteractor.RequestStation(", StringComparison.Ordinal)),
                "the station request");
            Assert.IsTrue(lines.Any(l => l.StartsWith("TargetRpc ", StringComparison.Ordinal)));
            Assert.IsTrue(lines.Any(l => l.StartsWith("sync Worktown.Game.Cooking.Food.DisplayName:", StringComparison.Ordinal)), "a SyncVar");
            Assert.Contains("broadcast Worktown.Game.Net.JoinRequest{System.String Build;System.String Name;}", lines);
            Assert.IsFalse(lines.Any(l => l.Contains("Version=")), "no assembly versions (they differ between the Editor and a player)");
            CollectionAssert.AreEquivalent(lines, ProtocolSurface.Lines(Game), "the same lines every time");
        }

        [Test]
        public void TypeNames_HaveNoAssemblyDetails()
        {
            Assert.AreEqual("System.Int32[]", ProtocolSurface.Name(typeof(int[])));
            Assert.AreEqual("System.Collections.Generic.List`1<System.String>", ProtocolSurface.Name(typeof(List<string>)));
            Assert.AreEqual("System.Collections.Generic.Dictionary`2<System.Int32,System.Int32[]>",
                ProtocolSurface.Name(typeof(Dictionary<int, int[]>)));
        }

        [Test]
        public void TheBuildHash_IsStable_AndIgnoresTheLineOrder()
        {
            string a = BuildHash.Compute("0.1.0", new[] { "x", "y" });
            Assert.AreEqual(a, BuildHash.Compute("0.1.0", new[] { "y", "x" }));
            Assert.AreEqual(16, a.Length);
            Assert.AreEqual("8", BuildHash.Short("8").Substring(0, 1));
            Assert.AreEqual(a.Substring(0, 8), BuildHash.Short(a));
            Assert.AreEqual("unknown", BuildHash.Short(null));
        }

        [Test]
        public void TheBuildHash_ChangesWithTheVersionOrAnyMessage()
        {
            string a = BuildHash.Compute("0.1.0", new[] { "ServerRpc A.B(System.Int32)" });
            Assert.AreNotEqual(a, BuildHash.Compute("0.1.1", new[] { "ServerRpc A.B(System.Int32)" }));
            Assert.AreNotEqual(a, BuildHash.Compute("0.1.0", new[] { "ServerRpc A.B(System.Int32,System.Int32)" }));
            Assert.AreNotEqual(a, BuildHash.Compute("0.1.0", new[] { "ServerRpc A.B(System.Int32)", "sync A.C:X" }));
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", new[] { "ab", "c" }), BuildHash.Compute("0.1.0", new[] { "a", "bc" }),
                "lines are kept apart");
        }

        [Test]
        public void TheGamesOwnBuildHash_IsTheSameEveryTime()
        {
            string a = BuildHash.Compute(UnityEngine.Application.version, ProtocolSurface.Lines(Game));
            string b = BuildHash.Compute(UnityEngine.Application.version, ProtocolSurface.Lines(Game));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(BuildHash.Compute(UnityEngine.Application.version, new string[0]), a);
        }
    }
}
