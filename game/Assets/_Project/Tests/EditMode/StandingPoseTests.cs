using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Worktown.Game.Art;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027b, finding of the first visual check: "the remote cook keeps a stride pose while standing and turning".
    /// Measured in the visual check (observer log): the standing player is in the Idle state with zero speed, so it is not
    /// the walk state; the library's Idle_Loop itself stands with the left foot 0.29 m ahead of the right, which from the
    /// side reads as a frozen stride (and turning in place shows it from every angle). CharacterVisual blends the legs of a
    /// feet-together pose over the idle. These tests sample the player prefab's real clips on its real body.
    /// </summary>
    public class StandingPoseTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        private static AnimationClip PrefabClip(string field)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, "Player.prefab missing; run Worktown/Build Graybox Scene");
            var visual = player.GetComponent<CharacterVisual>();
            var clip = new SerializedObject(visual).FindProperty(field).objectReferenceValue as AnimationClip;
            Assert.IsNotNull(clip, field + " is not set on the player prefab");
            return clip;
        }

        /// <summary>The largest front-to-back distance between the feet over the idle loop (m), with the legs layer at
        /// <paramref name="legsWeight"/>.</summary>
        private static float FeetApart(float legsWeight)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsTrue(catalog.TryGet(ArtCatalogRules.PlayerCharacterKeys()[0], out GameObject wrapper));
            AnimationClip idle = PrefabClip("_idle"), stand = PrefabClip("_stand");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper);
            PlayableGraph graph = PlayableGraph.Create("StandingPoseTests");
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                var output = AnimationPlayableOutput.Create(graph, "pose", animator);
                AnimationClipPlayable idlePlayable = AnimationClipPlayable.Create(graph, idle);
                idlePlayable.SetApplyFootIK(true);
                AnimationClipPlayable standPlayable = AnimationClipPlayable.Create(graph, stand);
                standPlayable.SetApplyFootIK(true);
                AnimationLayerMixerPlayable layers = AnimationLayerMixerPlayable.Create(graph, 2);
                graph.Connect(idlePlayable, 0, layers, 0);
                graph.Connect(standPlayable, 0, layers, 1);
                layers.SetInputWeight(0, 1f);
                layers.SetLayerMaskFromAvatarMask(1, CharacterVisual.LegsMask());
                layers.SetInputWeight(1, legsWeight);
                output.SetSourcePlayable(layers);
                float apart = 0f;
                for (int i = 0; i <= 8; i++)
                {
                    idlePlayable.SetTime(idle.length * i / 8f);
                    graph.Evaluate(0f);
                    Vector3 l = instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
                    Vector3 r = instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightFoot).position);
                    apart = Mathf.Max(apart, Mathf.Abs(l.z - r.z));
                }
                return apart;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void IdleClipAlone_StandsInAStride()
        {
            // The cause, documented: one foot well ahead of the other (a walking stride is about 0.44 m).
            Assert.Greater(FeetApart(0f), 0.25f);
        }

        [Test]
        public void StandingCharacter_HasItsFeetTogether()
        {
            Assert.Less(FeetApart(CharacterAnimRules.StandLegsWeight), 0.12f);
        }

        [Test]
        public void StandingOrTurningInPlace_IsIdle_AndOnlyTheIdleGetsTheStandingLegs()
        {
            // Turning moves only the yaw: no speed, so the state is Idle (from a walk too), and the legs layer follows the
            // idle's weight, so walking and crouching keep their own legs.
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0f, false, CharacterAnimState.Walk));
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimRules.StateFor(0.2f, false, CharacterAnimState.Walk));
            Assert.That(CharacterAnimRules.StandLegsWeight, Is.InRange(0.5f, 0.9f));
            var mask = CharacterVisual.LegsMask();
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body), "the hips and the upper body keep the idle");
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm));
        }
    }
}
