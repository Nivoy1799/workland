using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Dev;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 054 review fixes: the wire revision is in the build hash (old and new builds refuse each other), the players' collision rule lives
    /// on one session object and is pinned in the surface, items teleport instead of sliding, the remote player extrapolates once, a
    /// pick-up clears the stale transform goals, the correction bins are read-only.
    /// </summary>
    public class NetReview054Tests
    {
        private const string Prefabs = "Assets/_Project/Prefabs";
        private static System.Reflection.Assembly Game => typeof(NetBootstrap).Assembly;

        private static Component Find(GameObject go, string typeName)
        {
            foreach (Component c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) return c;
            return null;
        }

        private static SerializedProperty Prop(Component c, string field)
        {
            SerializedProperty p = new SerializedObject(c).FindProperty(field);
            Assert.IsNotNull(p, c.GetType().Name + " has no serialized field " + field + " (FishNet upgrade?)");
            return p;
        }

        [Test]
        public void TheWireRevision_IsInTheSurface_AndChangesTheHash()
        {
            // Spec 037 re-pin: 4, money on the wire is cents (D39), which no reflected line shows.
            // Spec 062a re-pin: 5, the surface and station numbers on the wire are stable ids (the same int parameters).
            // Spec 054b re-pin: 6, the collision mode byte, the soft separation and MoveData.ViewTick.
            // Spec 057 re-pin: 7, ShopStage's restaurant fields and the goal board, a new spawned station piece.
            Assert.AreEqual(9, ProtocolSurface.Revision, "054, the 053 review, spec 037, spec 062a, spec 054b, spec 057, spec 059, then spec 062b changed the wire (bump on every wire change)");
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.AreEqual(1, lines.Count(l => l == "revision 9"));
            Assert.AreEqual(1, lines.Count(l => l.StartsWith("revision ", StringComparison.Ordinal)));
            List<string> before = lines.Where(l => !l.StartsWith("revision ", StringComparison.Ordinal)).ToList();
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before), BuildHash.Compute("0.1.0", lines), "a build without the revision is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 1")), BuildHash.Compute("0.1.0", lines));
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 2")), BuildHash.Compute("0.1.0", lines), "a 054 build is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 3")), BuildHash.Compute("0.1.0", lines), "a whole-dollar build is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 4")), BuildHash.Compute("0.1.0", lines), "a pass-through build is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 5")), BuildHash.Compute("0.1.0", lines), "a 062a build without 054b is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 6")), BuildHash.Compute("0.1.0", lines), "a 054b build without 057 is refused");
        }

        [Test]
        public void TheCollisionRule_IsOneSessionField_NotOnEveryPlayer()
        {
            List<string> lines = ProtocolSurface.Lines(Game);
            // Spec 054b: the rule is one mode byte (PlayerCollisionMode), the session's only synced field.
            Assert.Contains("sync Worktown.Game.Net.SessionPhysics.PlayerCollision:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>", lines);
            Assert.AreEqual(1, lines.Count(l => l.StartsWith("sync Worktown.Game.Net.SessionPhysics.", StringComparison.Ordinal)));
            Assert.IsFalse(lines.Any(l => l.StartsWith("sync Worktown.Game.Net.PlayerController.CollidesWithPlayers", StringComparison.Ordinal)));
            Assert.IsFalse(lines.Any(l => l.Contains("SessionPhysics") && l.Contains("Rpc")), "no RPC on the session object");
            // Spec 054b (D48, replaces the 053 review's pass-through default): players block each other softly by default;
            // -player-collision hard|off is the A/B.
            Type session = typeof(NetBootstrap).Assembly.GetType("Worktown.Game.Net.SessionPhysics");
            Assert.AreEqual((byte)PlayerCollisionMode.Soft, Convert.ToByte(session.GetField("DefaultMode").GetRawConstantValue()), "soft by default");
            Assert.AreEqual(PlayerCollisionMode.Soft, LaunchArgs.Parse(new[] { "-host" }).PlayerCollision);
            Assert.AreEqual(PlayerCollisionMode.Hard, LaunchArgs.Parse(new[] { "-host", "-player-collision", "hard" }).PlayerCollision);
            Assert.AreEqual((byte)0, (byte)PlayerCollisionMode.Off, "the wire values are pinned");
            Assert.AreEqual((byte)1, (byte)PlayerCollisionMode.Hard);
            Assert.AreEqual((byte)2, (byte)PlayerCollisionMode.Soft);
        }

        [Test]
        public void TheShopBalance_IsSyncedOnce_OnTheLedger_NotOnEveryWallet()
        {
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.Contains("sync Worktown.Game.Economy.ShopLedger.Balance:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>", lines);
            Assert.IsFalse(lines.Any(l => l.StartsWith("sync Worktown.Game.Economy.Wallet.", StringComparison.Ordinal)), "a Wallet syncs nothing");
            Assert.IsFalse(lines.Any(l => l.Contains("ShopLedger") && l.Contains("Rpc")), "no RPC on the ledger");
        }

        [Test]
        public void Items_TeleportPastHalfAMetre_PlayersAndCustomersDoNot()
        {
            Assert.AreEqual(0.5f, NetRateRules.ItemTeleportMetres);
            int items = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null) continue;
                Component nt = Find(go, "NetworkTransform");
                if (nt == null) continue;
                bool walker = go.name == "Player" || go.name == "Customer";
                Assert.AreEqual(!walker, Prop(nt, "_enableTeleport").boolValue, go.name);
                if (walker) continue;
                items++;
                Assert.AreEqual(NetRateRules.ItemTeleportMetres, Prop(nt, "_teleportThreshold").floatValue, 1e-4f, go.name);
            }
            Assert.Greater(items, 8);
        }

        [Test]
        public void ThePlayer_ExtrapolatesOneUpdate()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Player.prefab");
            Assert.IsNotNull(player);
            Assert.AreEqual(1, NetRateRules.RemoteExtrapolation);
            Assert.AreEqual(NetRateRules.RemoteExtrapolation, Prop(Find(player, "NetworkTransform"), "_extrapolation").intValue);
        }

        [Test]
        public void APickUp_CanClearTheTransformsStaleGoals()
        {
            Assert.IsTrue(TransformGoals.Available, "FishNet's NetworkTransform still has " + TransformGoals.QueueField + " and "
                + TransformGoals.CurrentField + " (an upgrade renamed them?)");
            // 053 review: the shapes the helper relies on.
            Assert.IsTrue(typeof(System.Collections.ICollection).IsAssignableFrom(TransformGoals.QueueType), "the queue is a collection");
            Assert.IsFalse(TransformGoals.CurrentType.IsValueType, "the current goal is a reference (it is set to null)");
            // A real NetworkTransform (by reflection, as the other wire tests).
            Type ntType = Type.GetType("FishNet.Component.Transforming.NetworkTransform, FishNet.Runtime");
            Assert.IsNotNull(ntType);
            var go = new GameObject("TransformGoalsTest");
            try
            {
                Component nt = go.AddComponent(ntType);
                System.Reflection.MethodInfo clear = typeof(TransformGoals).GetMethod(nameof(TransformGoals.Clear));
                Assert.IsTrue((bool)clear.Invoke(null, new object[] { nt }), "cleared on a real NetworkTransform");
                Assert.IsFalse((bool)clear.Invoke(null, new object[] { null }), "nothing to clear");
                Assert.IsTrue(TransformGoals.Available, "still on after a good clear");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheCorrectionBins_AreReadOnly()
        {
            Assert.IsFalse(typeof(RemoteMotionStats).GetField(nameof(RemoteMotionStats.CorrectionBinEdgesCm)).FieldType.IsArray);
            Assert.Throws<NotSupportedException>(() => ((IList<float>)RemoteMotionStats.CorrectionBinEdgesCm)[0] = 1f);
        }
    }
}
