using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Worktown.Game.Layout;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a: a place surface made by a test is part of a piece, like every surface in the game: <see cref="Attach"/> puts a
    /// PieceBehaviour on its object that lists it at its local number and gives it the surface id <paramref name="surfaceId"/>.
    /// </summary>
    public static class TestPieces
    {
        public static PieceBehaviour Attach(GameObject go, PlaceSurface surface, int surfaceId)
        {
            Assert.IsTrue(SurfaceId.TryUnpack(surfaceId, out int piece, out int local), "a valid surface id");
            PieceBehaviour behaviour = go.GetComponent<PieceBehaviour>() ?? go.AddComponent<PieceBehaviour>();
            var list = new List<PlaceSurface>();
            for (int i = 0; i < local; i++) list.Add(null);
            list.Add(surface);
            var so = new SerializedObject(behaviour);
            SerializedProperty surfaces = so.FindProperty("_surfaces");
            surfaces.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++) surfaces.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            behaviour.SetIdOffline(piece);
            Assert.AreEqual(surfaceId, surface.Id);
            return behaviour;
        }
    }
}
