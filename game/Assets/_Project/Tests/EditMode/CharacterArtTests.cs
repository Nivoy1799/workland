using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Npc;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 024 AC2, spec 025 AC4: character art keys, wrappers and the player and customer prefabs that use them.</summary>
    public class CharacterArtTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string CustomerPrefabPath = "Assets/_Project/Prefabs/Customer.prefab";
        // Customer is a NetworkBehaviour; the test assembly does not reference FishNet (same as TicketRailSceneTests).
        private static readonly Type CustomerType = typeof(CustomerRules).Assembly.GetType("Worktown.Game.Npc.Customer");

        private static ArtKey[] AllCharacterKeys()
        {
            var keys = new List<ArtKey>(ArtCatalogRules.PlayerCharacterKeys());
            keys.AddRange(ArtCatalogRules.CustomerCharacterKeys());
            return keys.ToArray();
        }

        [Test]
        public void CharacterVariant_IsTheSameForTheSameId_AndWraps()
        {
            Assert.AreEqual(0, ArtCatalogRules.CharacterVariant(0, 2));
            Assert.AreEqual(1, ArtCatalogRules.CharacterVariant(1, 2));
            Assert.AreEqual(0, ArtCatalogRules.CharacterVariant(4, 2));
            Assert.AreEqual(3, ArtCatalogRules.CharacterVariant(7, 4));
            Assert.AreEqual(3, ArtCatalogRules.CharacterVariant(-1, 4), "negative ids wrap too");
            Assert.AreEqual(0, ArtCatalogRules.CharacterVariant(5, 0), "no variants");
        }

        [Test]
        public void PlayerAndCustomerKeys_AreDistinctCharacters()
        {
            Assert.AreEqual(ArtKey.CharacterPlayerMale, ArtCatalogRules.PlayerCharacterKey(0), "even owner id");
            Assert.AreEqual(ArtKey.CharacterPlayerFemale, ArtCatalogRules.PlayerCharacterKey(1), "odd owner id");
            Assert.AreEqual(ArtKey.CharacterPlayerMale, ArtCatalogRules.PlayerCharacterKey(2));
            var customers = new HashSet<ArtKey>();
            for (int id = 0; id < 2; id++) customers.Add(ArtCatalogRules.CustomerCharacterKey(id));
            Assert.AreEqual(2, customers.Count, "two ids in a row show two different customers");
            Assert.AreEqual(ArtCatalogRules.CustomerCharacterKey(1), ArtCatalogRules.CustomerCharacterKey(5));
            foreach (ArtKey key in ArtCatalogRules.PlayerCharacterKeys())
                Assert.IsFalse(customers.Contains(key), key + " is a player character, not a customer");
            Assert.AreEqual(4, new HashSet<ArtKey>(AllCharacterKeys()).Count);
        }

        [Test]
        public void Catalog_HasEveryCharacterKey_WithAHumanoidWrapperOf1_75m()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "ArtCatalog.asset missing; run Worktown/Build Graybox Scene");
            Assert.IsEmpty(ArtCatalogRules.MissingKeys(catalog.ToPairs(), AllCharacterKeys()));
            foreach (ArtKey key in AllCharacterKeys())
            {
                Assert.IsTrue(catalog.TryGet(key, out GameObject wrapper));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper);
                try
                {
                    Assert.IsEmpty(instance.GetComponentsInChildren<Collider>(true), key + " has a collider");
                    Assert.IsEmpty(instance.GetComponentsInChildren<MonoBehaviour>(true), key + " has a script");
                    Animator animator = instance.GetComponentInChildren<Animator>(true);
                    Assert.IsNotNull(animator, key + " has no Animator");
                    Assert.IsNull(animator.runtimeAnimatorController, key + ": CharacterVisual plays the clips");
                    Assert.IsFalse(animator.applyRootMotion, key.ToString());
                    Assert.IsNotNull(animator.avatar, key + " has no avatar");
                    Assert.IsTrue(animator.avatar.isHuman, key + ": Humanoid, so the library clips retarget onto it");
                    Assert.Greater(instance.GetComponentsInChildren<Renderer>(true).Length, 0, key.ToString());
                    Bounds b = MeshBounds(instance);
                    Assert.AreEqual(1.75f, b.size.y, 0.05f, key + " height (D29)");
                    Assert.AreEqual(0f, b.min.y, 0.02f, key + " stands on its pivot");
                    Assert.AreEqual(0f, b.center.x, 0.05f, key + " centred");
                    foreach (SkinnedMeshRenderer r in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        Assert.IsNotNull(r.sharedMaterial.GetTexture("_BaseMap"), key + ": skin texture");

                    Transform hand = FindDeep(instance.transform, CharacterVisual.HandName);
                    Assert.IsNotNull(hand, key + " has no hand anchor");
                    Assert.AreSame(animator.GetBoneTransform(HumanBodyBones.RightHand), hand.parent, key + ": on the real right hand bone");
                    Assert.Greater(hand.position.x, 0.2f, key + ": the right hand is on the right (+X, model faces +Z)");
                    Assert.Greater(hand.position.y, 0.5f, key.ToString());
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void PlayerPrefab_ShowsAPersonInsteadOfTheCapsule_ControllerUnchanged()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab);
            var controller = prefab.GetComponent<CharacterController>();
            Assert.AreEqual(2f, controller.height);
            Assert.AreEqual(0.4f, controller.radius);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), controller.center);

            Transform graphic = prefab.transform.Find("Graphic");
            Assert.IsNotNull(graphic);
            Assert.IsNull(graphic.Find("Body"), "capsule removed");
            Assert.IsNull(graphic.Find("Facing"), "nose removed");
            Assert.IsEmpty(graphic.GetComponentsInChildren<MeshRenderer>(true), "only the skinned person is drawn");

            var visual = prefab.GetComponent<CharacterVisual>();
            Assert.IsNotNull(visual);
            var so = new SerializedObject(visual);
            AssertVariants(so, ArtCatalogRules.PlayerCharacterKeys());
            Assert.IsTrue(so.FindProperty("_variantByOwner").boolValue);
            Assert.AreSame(graphic, so.FindProperty("_tracked").objectReferenceValue, "animates from the smoothed graphic");
            Assert.IsNotNull(so.FindProperty("_player").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("_firstPerson").objectReferenceValue);
            foreach (string clip in new[] { "_idle", "_walk", "_run", "_crouch", "_hold" })
                Assert.IsNotNull(so.FindProperty(clip).objectReferenceValue, clip);

            // First person hides every renderer of the person (shadows only).
            SerializedProperty body = new SerializedObject(prefab.GetComponent<FirstPersonBody>()).FindProperty("_body");
            var hidden = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < body.arraySize; i++) hidden.Add(body.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (SkinnedMeshRenderer r in graphic.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Assert.IsTrue(hidden.Contains(r), r.name + " is not hidden in first person");
            Assert.AreEqual(graphic.GetComponentsInChildren<Renderer>(true).Length, body.arraySize);
        }

        [Test]
        public void CustomerPrefab_IsAPersonWithATint_AndNoCollider()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "customers never block players");
            Assert.IsNull(prefab.transform.Find("Body"), "capsule removed");
            Transform character = prefab.transform.Find("Character");
            Assert.IsNotNull(character);
            Assert.IsEmpty(character.GetComponentsInChildren<MeshRenderer>(true), "only the skinned person is drawn");
            var so = new SerializedObject(prefab.GetComponent<CharacterVisual>());
            AssertVariants(so, ArtCatalogRules.CustomerCharacterKeys());
            Assert.IsFalse(so.FindProperty("_variantByOwner").boolValue);

            SerializedProperty tinted = new SerializedObject(prefab.GetComponent(CustomerType)).FindProperty("_tinted");
            Assert.AreEqual(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, tinted.arraySize);
            Assert.Greater(tinted.arraySize, 0);
        }

        [Test]
        public void CustomerTint_KeepsTheCharacterColoursReadable()
        {
            Func<string, Color> tintOf = wish => (Color)CustomerType.GetMethod("TintOf").Invoke(null, new object[] { wish });
            float strength = (float)CustomerType.GetField("TintStrength").GetValue(null);
            Color meal = tintOf("meal");
            Assert.AreNotEqual(Color.white, meal);
            Assert.AreNotEqual(tintOf("snack"), meal);
            Assert.Greater(strength, 0f);
            Assert.GreaterOrEqual(Mathf.Min(meal.r, Mathf.Min(meal.g, meal.b)), 1f - strength - 0.001f,
                "a light tint, not a solid colour");
        }

        [TestCase(PlayerPrefabPath)]
        [TestCase(CustomerPrefabPath)]
        public void MeasuredClipSpeeds_PlayWalkAndSprintWithoutClamping(string prefabPath)
        {
            // Spec 025 AC4: the art builder measured the walk and run clips on the 1.75 m body (StrideRules); at full walk
            // and sprint speed the playback rate stays strictly inside the clamp range, so the feet do not slide.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);
            var visual = prefab.GetComponent<CharacterVisual>();
            float walk = visual.WalkClipSpeed;
            float run = visual.RunClipSpeed;
            // Deviation from the AC4 text (0.8 to 2.5 m/s): the walk state plays the library's jog (its walk covers only
            // about 0.7 m/s on this body), so the plausible range is wider. See the spec's Implementation notes.
            Assert.That(walk, Is.InRange(0.8f, 5f), "measured walk clip speed");
            Assert.That(run, Is.InRange(0.8f, 8f), "measured run clip speed");
            float walkRate = CharacterAnimRules.PlaybackRate(CharacterAnimState.Walk, MovementRules.WalkSpeed, walk);
            float runRate = CharacterAnimRules.PlaybackRate(CharacterAnimState.Run, MovementRules.SprintSpeed, run);
            Assert.IsTrue(CharacterAnimRules.FitsWithoutClamp(MovementRules.WalkSpeed, walk), "walk rate " + walkRate);
            Assert.IsTrue(CharacterAnimRules.FitsWithoutClamp(MovementRules.SprintSpeed, run), "sprint rate " + runRate);
            Assert.Greater(walkRate, CharacterAnimRules.MinRate);
            Assert.Less(walkRate, CharacterAnimRules.MaxRate);
            Assert.Greater(runRate, CharacterAnimRules.MinRate);
            Assert.Less(runRate, CharacterAnimRules.MaxRate);
            // Around the switch between the two states neither clip clamps either.
            Assert.IsTrue(CharacterAnimRules.FitsWithoutClamp(CharacterAnimRules.RunStopSpeed, run), "run clip at the stop speed");
            Assert.IsTrue(CharacterAnimRules.FitsWithoutClamp(CharacterAnimRules.RunStartSpeed, walk), "walk clip at the run start speed");
        }

        /// <summary>World bounds of the baked (posed) skinned meshes; the import's renderer bounds are loose.</summary>
        private static Bounds MeshBounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            var baked = new Mesh();
            try
            {
                foreach (SkinnedMeshRenderer skin in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(baked, true);
                    foreach (Vector3 v in baked.vertices)
                    {
                        Vector3 w = skin.transform.TransformPoint(v);
                        if (!any) b = new Bounds(w, Vector3.zero);
                        else b.Encapsulate(w);
                        any = true;
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            Assert.IsTrue(any, go.name + " has no skinned mesh");
            return b;
        }

        private static void AssertVariants(SerializedObject visual, ArtKey[] keys)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            SerializedProperty variants = visual.FindProperty("_variants");
            SerializedProperty hands = visual.FindProperty("_hands");
            Assert.AreEqual(keys.Length, variants.arraySize);
            Assert.AreEqual(keys.Length, hands.arraySize);
            for (int i = 0; i < keys.Length; i++)
            {
                var variant = (GameObject)variants.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.IsNotNull(variant, "variant " + i);
                Assert.IsTrue(catalog.TryGet(keys[i], out GameObject wrapper));
                Assert.AreSame(wrapper, PrefabUtility.GetCorrespondingObjectFromSource(variant), "variant " + i + " is " + keys[i]);
                Assert.AreEqual(i == 0, variant.activeSelf, "only the first variant is active before spawn");
                var hand = (Transform)hands.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.IsNotNull(hand);
                Assert.IsTrue(hand.IsChildOf(variant.transform), "hand " + i + " belongs to its variant");
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
