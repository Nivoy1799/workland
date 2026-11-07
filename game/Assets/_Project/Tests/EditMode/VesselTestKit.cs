using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 021 test helper: finds network components (Vessel, StationContents, Pickup) without a FishNet reference in
    /// this test assembly: by Type and through SerializedObject, never through generic GetComponent or a cast.
    /// </summary>
    internal static class VesselTestKit
    {
        public static readonly Type VesselType = typeof(StationRules).Assembly.GetType("Worktown.Game.Cooking.Vessel");

        /// <summary>Every component of <paramref name="type"/> in the scene, inactive ones included.</summary>
        public static List<Component> AllOf(Scene scene, Type type)
        {
            var found = new List<Component>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren(type, true));
            return found;
        }

        /// <summary>The serialized kind of a Vessel component.</summary>
        public static VesselKind KindOf(Component vessel)
        {
            return (VesselKind)new SerializedObject(vessel).FindProperty("_kind").intValue;
        }

        /// <summary>The Vessel on a game object, or null.</summary>
        public static Component VesselOn(GameObject go) => go != null ? go.GetComponent(VesselType) : null;
    }
}
