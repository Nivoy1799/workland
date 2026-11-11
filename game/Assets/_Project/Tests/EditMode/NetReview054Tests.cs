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
            Assert.AreEqual(2, ProtocolSurface.Revision, "spec 054 changed the wire (bump on every wire change the lines cannot see)");
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.AreEqual(1, lines.Count(l => l == "revision 2"));
            List<string> before = lines.Where(l => !l.StartsWith("revision ", StringComparison.Ordinal)).ToList();
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before), BuildHash.Compute("0.1.0", lines), "a build without the revision is refused");
            Assert.AreNotEqual(BuildHash.Compute("0.1.0", before.Append("revision 1")), BuildHash.Compute("0.1.0", lines));
        }

        [Test]
        public void TheCollisionRule_IsOneSessionField_NotOnEveryPlayer()
        {
            List<string> lines = ProtocolSurface.Lines(Game);
            Assert.Contains("sync Worktown.Game.Net.SessionPhysics.PlayersCollide:FishNet.Object.Synchronizing.SyncVar`1<System.Boolean>", lines);
            Assert.IsFalse(lines.Any(l => l.StartsWith("sync Worktown.Game.Net.PlayerController.CollidesWithPlayers", StringComparison.Ordinal)));
            Assert.IsFalse(lines.Any(l => l.Contains("SessionPhysics") && l.Contains("Rpc")), "no RPC on the session object");
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
        }

        [Test]
        public void TheCorrectionBins_AreReadOnly()
        {
            Assert.IsFalse(typeof(RemoteMotionStats).GetField(nameof(RemoteMotionStats.CorrectionBinEdgesCm)).FieldType.IsArray);
            Assert.Throws<NotSupportedException>(() => ((IList<float>)RemoteMotionStats.CorrectionBinEdgesCm)[0] = 1f);
        }
    }
}
