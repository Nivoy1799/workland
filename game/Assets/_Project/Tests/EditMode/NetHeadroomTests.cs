using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Dev;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 054, part B (cut): the generated prefabs (read from their serialized fields): the player sends no inputs to the others
    /// (state forwarding off) and they see it through a root NetworkTransform at <see cref="NetRateRules.RemotePlayerInterval"/>, not
    /// sent to the owner, no scale; every server-authoritative item and the customer without scale; the customer at
    /// <see cref="NetRateRules.CustomerInterval"/>. The held-item sync rule (off while held, on before the release pose is set).
    /// The feel guard's numbers on made-up tracks (RemoteMotionStats: a steady walk, a 1 m jump, a stop) and owner corrections.
    /// </summary>
    public class NetHeadroomTests
    {
        private const string Prefabs = "Assets/_Project/Prefabs";

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

        private static GameObject LoadPrefab(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + name + ".prefab");
            Assert.IsNotNull(go, name + ".prefab (run Worktown/Build Graybox Scene)");
            return go;
        }

        [Test]
        public void Rates_AreTheSpecsNumbers()
        {
            Assert.AreEqual(30, NetRateRules.TickRate);
            Assert.AreEqual(2, NetRateRules.RemotePlayerInterval);
            Assert.AreEqual(2, NetRateRules.CustomerInterval);
            Assert.AreEqual(15, NetRateRules.DefaultRemoteHz);
            Assert.AreEqual(1, NetRateRules.RemotePlayerIntervalFor(30));
            Assert.AreEqual(2, NetRateRules.RemotePlayerIntervalFor(15));
            Assert.AreEqual(2, NetRateRules.RemotePlayerIntervalFor(0), "not given: the default");
            Assert.AreEqual(133.3, NetRateRules.DisplayDelayMs(NetRateRules.RemotePlayerInterval, NetRateRules.RemoteInterpolation), 0.1,
                "the approved display delay of other players");
        }

        [Test]
        public void PlayerPrefab_ForwardsNoInputs_AndSendsItsRootAt15Hz()
        {
            GameObject player = LoadPrefab("Player");
            Component nob = Find(player, "NetworkObject");
            Assert.IsTrue(Prop(nob, "_enablePrediction").boolValue, "the owner still predicts");
            Assert.IsFalse(Prop(nob, "_enableStateForwarding").boolValue, "no inputs or states to the other players");
            Component nt = Find(player, "NetworkTransform");
            Assert.IsNotNull(nt, "a NetworkTransform on the player's root");
            Assert.AreSame(nt, Prop(nob, "_networkTransform").objectReferenceValue, "FishNet configures it for prediction");
            Assert.AreEqual(NetRateRules.RemotePlayerInterval, Prop(nt, "_interval").intValue);
            Assert.AreEqual(NetRateRules.RemoteInterpolation, Prop(nt, "_interpolation").intValue);
            Assert.IsFalse(Prop(nt, "_sendToOwner").boolValue, "the owner predicts itself");
            Assert.IsFalse(Prop(nt, "_clientAuthoritative").boolValue, "the server is the authority");
            Assert.IsFalse(Prop(nt, "_synchronizeScale").boolValue);
            Transform graphic = player.transform.Find("Graphic");
            Assert.IsNotNull(graphic);
            Component smoother = Find(graphic.gameObject, "NetworkTickSmoother");
            Assert.IsNotNull(smoother, "the owner's smoother stays");
            Assert.IsTrue(Prop(smoother, "_favorPredictionNetworkTransform").boolValue,
                "for the others the NetworkTransform smooths the root, the tick smoother stands aside");
        }

        [Test]
        public void ServerAuthoritativeItemsAndCustomers_SyncNoScale()
        {
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null) continue;
                Component nt = Find(go, "NetworkTransform");
                if (nt == null) continue;
                checkedCount++;
                Assert.IsFalse(Prop(nt, "_synchronizeScale").boolValue, go.name + ": no root is scaled at run time");
                Assert.IsFalse(Prop(nt, "_clientAuthoritative").boolValue, go.name);
                if (go.name == "Player" || go.name == "Customer") continue;
                Assert.AreEqual(NetRateRules.ItemInterval, Prop(nt, "_interval").intValue, go.name + ": items every tick while they move");
            }
            Assert.Greater(checkedCount, 8, "the player, the customer and the items");
            Component customer = Find(LoadPrefab("Customer"), "NetworkTransform");
            Assert.AreEqual(NetRateRules.CustomerInterval, Prop(customer, "_interval").intValue);
            Assert.AreEqual(NetRateRules.CustomerInterpolation, Prop(customer, "_interpolation").intValue);
        }

        [Test]
        public void HeldItems_SyncNothingWhileHeld_AndSyncAgainBeforeTheReleasePose()
        {
            Assert.IsTrue(NetRateRules.SyncsTransform(false));
            Assert.IsFalse(NetRateRules.SyncsTransform(true), "held: neither position nor rotation");
            GameObject go = Object.Instantiate(LoadPrefab("PickupCube"));
            try
            {
                Component nt = Find(go, "NetworkTransform");
                Assert.IsNotNull(nt);
                // Pickup is a FishNet NetworkBehaviour and this assembly does not reference FishNet: its two public helpers by name.
                System.Type pickup = System.Type.GetType("Worktown.Game.Net.Pickup, Worktown.Game");
                Assert.IsNotNull(pickup, "Worktown.Game.Net.Pickup");
                System.Reflection.MethodInfo apply = pickup.GetMethod("ApplyHeldSync");
                System.Reflection.MethodInfo release = pickup.GetMethod("ReleaseTo");
                Assert.IsNotNull(apply, "Pickup.ApplyHeldSync");
                Assert.IsNotNull(release, "Pickup.ReleaseTo");
                apply.Invoke(null, new object[] { go, true });
                Assert.IsFalse(Prop(nt, "_synchronizePosition").boolValue, "held: no position");
                Assert.IsFalse(Prop(nt, "_synchronizeRotation").boolValue, "held: no rotation");
                go.transform.hasChanged = false;
                var pose = new Pose(new Vector3(1f, 0.9f, 2f), Quaternion.Euler(0f, 40f, 0f));
                release.Invoke(null, new object[] { go, pose.position, pose.rotation });
                Assert.IsTrue(Prop(nt, "_synchronizePosition").boolValue, "released: sync is on again (before the pose: Pickup.ReleaseTo)");
                Assert.IsTrue(Prop(nt, "_synchronizeRotation").boolValue);
                Assert.IsFalse(Prop(nt, "_synchronizeScale").boolValue, "scale stays off");
                Assert.AreEqual(pose.position, go.transform.position);
                Assert.IsTrue(go.transform.hasChanged, "the pose is set after the sync, so the NetworkTransform sees the change");
                apply.Invoke(null, new object[] { go, false });
                Assert.IsTrue(Prop(nt, "_synchronizePosition").boolValue);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- the feel guard (B4) ----

        private static void Walk(RemoteMotionStats stats, int id, ref float x, float speed, int frames, float dt)
        {
            for (int i = 0; i < frames; i++)
            {
                stats.BeginFrame();
                x += speed * dt;
                stats.AddFrame(id, x, 0f, 0f, dt);
            }
        }

        [Test]
        public void RemoteMotion_SteadyWalk_HasNoJitter_AndNoTeleport()
        {
            var stats = new RemoteMotionStats();
            float x = 0f;
            Walk(stats, 7, ref x, 2.5f, 90, 1f / 30f); // 3 s at the walk speed, 30 fps
            stats.TakeSecond(out _, out _, out _, out _, out _);
            Walk(stats, 7, ref x, 2.5f, 30, 1f / 30f);
            stats.TakeSecond(out float jitter, out int moving, out int teleports, out _, out _);
            Assert.AreEqual(30, moving);
            Assert.Less(jitter, 0.01f);
            Assert.AreEqual(0, teleports);
        }

        [Test]
        public void RemoteMotion_A15HzStutter_ShowsAsJitter()
        {
            var stats = new RemoteMotionStats();
            float x = 0f;
            const float dt = 1f / 60f;
            for (int i = 0; i < 240; i++)
            {
                stats.BeginFrame();
                if (i % 4 == 0) x += 2.5f * dt * 4f; // moves only every 4th frame: what an unsmoothed 15 Hz root looks like at 60 fps
                stats.AddFrame(3, x, 0f, 0f, dt);
            }
            stats.TakeSecond(out float jitter, out int moving, out _, out _, out _);
            Assert.Greater(moving, 100);
            Assert.Greater(jitter, 0.5f, "a stepped track is far over the 0.15 guard");
        }

        [Test]
        public void RemoteMotion_A1mJump_IsATeleport_ButNotAtSpawnOrInALongFrame()
        {
            var stats = new RemoteMotionStats();
            float x = 0f;
            stats.BeginFrame();
            stats.AddFrame(5, 0f, 0f, 0f, 1f / 30f);
            stats.BeginFrame();
            stats.AddFrame(5, 1f, 0f, 0f, 1f / 30f); // within the first second: a spawn settling, not counted
            x = 1f;
            Walk(stats, 5, ref x, 0f, 60, 1f / 30f);
            stats.TakeSecond(out _, out _, out int atSpawn, out _, out _);
            Assert.AreEqual(0, atSpawn, "spawns are excluded");
            stats.BeginFrame();
            stats.AddFrame(5, 2f, 0f, 0f, 1f / 30f); // 1 m in one frame
            stats.BeginFrame();
            stats.AddFrame(5, 2.6f, 0f, 0f, 0.3f); // 0.6 m in a 0.3 s stall: 2 m/s, a walk, not a teleport
            stats.TakeSecond(out _, out _, out int teleports, out _, out _);
            Assert.AreEqual(1, teleports);
            Assert.IsTrue(RemoteMotionStats.IsTeleport(1f, 1f / 30f));
            Assert.IsFalse(RemoteMotionStats.IsTeleport(0.4f, 1f / 30f));
        }

        [Test]
        public void RemoteMotion_AStop_CountsOnlyWhileMoving_AndAGoneTrackIsDropped()
        {
            var stats = new RemoteMotionStats();
            float x = 0f;
            Walk(stats, 9, ref x, 2.5f, 60, 1f / 30f);
            Walk(stats, 9, ref x, 0f, 60, 1f / 30f); // stands still for 2 s
            stats.TakeSecond(out _, out _, out _, out _, out _);
            Walk(stats, 9, ref x, 0f, 30, 1f / 30f);
            stats.TakeSecond(out float jitter, out int moving, out _, out _, out _);
            Assert.AreEqual(0, moving, "standing still: no jitter samples");
            Assert.AreEqual(0f, jitter);
            Assert.AreEqual(1, stats.Tracked);
            stats.BeginFrame(); // nobody drawn this frame: gone
            stats.BeginFrame();
            Assert.AreEqual(0, stats.Tracked);
        }

        [Test]
        public void OwnerCorrections_CountReconcilesFarFromThePrediction()
        {
            var stats = new RemoteMotionStats();
            stats.RecordPredicted(100, 1f, 0f, 1f);
            stats.RecordPredicted(101, 1.1f, 0f, 1f);
            Assert.IsFalse(stats.CheckReconcile(100, 1.02f, 0f, 1f), "2 cm: the same");
            Assert.IsTrue(stats.CheckReconcile(101, 1.3f, 0f, 1f), "20 cm: corrected");
            Assert.IsFalse(stats.CheckReconcile(102, 9f, 0f, 9f), "a tick never predicted is not counted");
            stats.RecordPredicted(100 + RemoteMotionStats.RingTicks, 5f, 0f, 5f);
            Assert.IsFalse(stats.CheckReconcile(100, 9f, 0f, 9f), "overwritten in the ring: not counted");
            stats.TakeSecond(out _, out _, out _, out int corrections, out int reconciles);
            Assert.AreEqual(1, corrections);
            Assert.AreEqual(2, reconciles);
            var bins = new int[RemoteMotionStats.CorrectionBinEdgesCm.Count + 1];
            stats.TakeCorrectionSizes(bins, out float maxCm);
            Assert.AreEqual(20f, maxCm, 0.01f, "the 20 cm correction");
            Assert.AreEqual(1, bins[RemoteMotionStats.BinOf(maxCm)]);
            stats.TakeCorrectionSizes(bins, out maxCm);
            Assert.AreEqual(0f, maxCm, "a new window");
            foreach (int n in bins) Assert.AreEqual(0, n);
        }

        [Test]
        public void CorrectionSizes_FallIntoTheirBins()
        {
            Assert.AreEqual(0, RemoteMotionStats.BinOf(5.5f));
            Assert.AreEqual("owner_corr_cm_le_6", RemoteMotionStats.BinName(0));
            Assert.AreEqual(RemoteMotionStats.CorrectionBinEdgesCm.Count, RemoteMotionStats.BinOf(400f), "the open last bin");
            Assert.AreEqual("owner_corr_cm_gt_150", RemoteMotionStats.BinName(RemoteMotionStats.CorrectionBinEdgesCm.Count));
            Assert.AreEqual(RemoteMotionStats.BinOf(10f), RemoteMotionStats.BinOf(9.9f), "an edge belongs to its own bin");
        }
    }
}
