using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062a: the default layout spawned offline (LayoutSpawner.InstantiateOffline), the registry, the pieces' numbers and lengths.</summary>
    public class LayoutSpawnTests
    {
        [TearDown]
        public void Clear() => LayoutRegistry.Clear();

        [Test]
        public void EveryPieceOfTheDefault_SpawnsAndRegisters_StationIdIsThePieceId()
        {
            ShopLayout layout = DefaultLayout.Load();
            Assert.AreEqual(28, layout.Pieces.Count); // spec 057: the goal board
            Scene scene = LayoutScene.Open(layout);
            try
            {
                Assert.AreEqual(layout.Pieces.Count, LayoutRegistry.Pieces.Count);
                foreach (LayoutPiece p in layout.Pieces)
                {
                    PieceBehaviour piece = LayoutRegistry.Piece(p.Id);
                    Assert.IsNotNull(piece, p.TypeId + " " + p.Id);
                    Assert.AreEqual(p.TypeId, piece.TypeId);
                    Assert.AreEqual(p.XMm, piece.XMm);
                    Assert.AreEqual(p.ZMm, piece.ZMm);
                    Assert.AreEqual(p.Turns, piece.Turns);
                    Assert.AreEqual(p.LengthMm, piece.LengthMm);
                    Assert.AreEqual(new Vector3(p.XMm / 1000f, 0f, p.ZMm / 1000f), piece.transform.position);
                    if (piece.Station != null) Assert.AreEqual(p.Id, piece.Station.Id, "station id = piece id");
                    for (int i = 0; i < piece.Surfaces.Count; i++)
                        if (piece.Surfaces[i] != null && piece.Surfaces[i].gameObject.activeSelf)
                        {
                            Assert.AreEqual(SurfaceId.Of(p.Id, i), piece.Surfaces[i].Id);
                            Assert.AreSame(piece.Surfaces[i], LayoutRegistry.Surface(piece.Surfaces[i].Id));
                        }
                }
                Assert.AreEqual(LayoutQuery.Station(StationKind.Stove, 1), LayoutRegistry.Station(3));
                Assert.AreEqual("StoveTop", LayoutQuery.Surface(PieceCatalog.Stove, 1, "StoveTop").name);
                Assert.AreEqual(SurfaceId.Of(4, 1), LayoutQuery.Surface(PieceCatalog.Stove, 2, "Burner 0").Id);
            }
            finally { LayoutScene.Close(scene); }
            Assert.AreEqual(0, LayoutRegistry.Pieces.Count, "closing forgets them");
        }

        [Test]
        public void ATypePlacedTwice_IsNumbered_ByAscendingId()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                Assert.AreEqual("Stove 1", LayoutRegistry.Station(3).DisplayName);
                Assert.AreEqual("Stove 2", LayoutRegistry.Station(4).DisplayName);
                Assert.AreEqual("Stove 2", LayoutRegistry.Station(4).name);
                Assert.IsTrue(LayoutRegistry.Station(4).GetComponentsInChildren<TextMesh>(true).Any(t => t.text == "Stove 2"), "its sign");
                Assert.AreEqual("Worktop 1", LayoutQuery.Surface(PieceCatalog.Worktop, 1, "Worktop").name);
                Assert.AreEqual("Counter top 4", LayoutQuery.Surface(PieceCatalog.FrontCounter, 4, "Counter top").name);
                Assert.AreEqual("Fryer", LayoutQuery.Station(StationKind.Fryer).DisplayName, "a single piece has no number");
                foreach (PieceBehaviour p in LayoutRegistry.Pieces)
                    foreach (Transform t in p.GetComponentsInChildren<Transform>(true))
                        Assert.IsFalse(t.name.Contains(PieceBehaviour.NumberMark.ToString()), t.name);
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void ACounter_TakesItsLength_AndANarrowOneHasNoTop()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                PieceBehaviour prep = LayoutQuery.Piece(PieceCatalog.Worktop, 2);
                Assert.AreEqual(740, prep.LengthMm);
                PlaceSurface top = prep.Surfaces[0];
                Assert.AreEqual(0.74f, top.Size.x, 0.0005f);
                Assert.AreEqual(0.74f, top.GetComponent<BoxCollider>().size.x, 0.0005f);
                int modules = top.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Module "));
                Assert.AreEqual(Worktown.Game.Art.ArtSizeTable.WorktopModules(0.74f).Count, modules, "its cabinet modules");
                PieceBehaviour filler = LayoutQuery.Pieces(PieceCatalog.FrontCounter).Single(p => p.LengthMm < 100);
                Assert.AreEqual(0, filler.Surfaces.Count(s => s != null), "a front counter under 0.1 m has no top");
                Assert.AreEqual(0.09f, filler.GetComponentInChildren<BoxCollider>().size.x, 0.0005f);
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void AnUnknownType_IsNotSpawned()
        {
            ShopLayout standard = DefaultLayout.Load();
            var pieces = new List<LayoutPiece>(standard.Pieces) { new LayoutPiece(500, "hot_tub", 0, 0, 0, 0, 1, 0) };
            Scene scene = LayoutScene.Open(standard.WithPieces(pieces));
            try
            {
                Assert.IsNull(LayoutRegistry.Piece(500));
                Assert.AreEqual(standard.Pieces.Count, LayoutRegistry.Pieces.Count);
            }
            finally { LayoutScene.Close(scene); }
        }

        /// <summary>AC5: no index member and no station or surface array is left.</summary>
        [Test]
        public void NoIndex_AndNoArrays_AreLeft()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.IsEmpty(typeof(PlaceSurface).GetMember("Index", all), "PlaceSurface.Index");
            Assert.IsEmpty(typeof(PlaceSurface).GetMember("_index", all));
            Assert.IsEmpty(typeof(Station).GetMember("Index", all), "Station.Index");
            Assert.IsEmpty(typeof(Station).GetMember("_index", all));
            Assert.IsEmpty(typeof(PlaceSurface).GetMember("StoveIndex", all));
            Assert.IsEmpty(typeof(PlaceSurface).GetMember("DispenserIndex", all));
            foreach (Type service in new[] { typeof(PickupService), typeof(KitchenService) })
                foreach (FieldInfo f in service.GetFields(all))
                {
                    Assert.AreNotEqual(typeof(PlaceSurface[]), f.FieldType, service.Name + "." + f.Name);
                    Assert.AreNotEqual(typeof(Station[]), f.FieldType, service.Name + "." + f.Name);
                    Assert.AreNotEqual(typeof(List<PlaceSurface>), f.FieldType, service.Name + "." + f.Name);
                    Assert.AreNotEqual(typeof(List<Station>), f.FieldType, service.Name + "." + f.Name);
                }
        }

        /// <summary>062a review: the services that held a scene reference to a station find the spawned one through the registry.</summary>
        [Test]
        public void TheServices_FindTheirStations_ThroughTheRegistry()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
                object Lookup(Type service, string property) => service.GetProperty(property, statics).GetValue(null);
                Station board = LayoutQuery.Station(StationKind.MenuBoard);
                Assert.IsNotNull(board);
                Assert.AreSame(board.GetComponent<Worktown.Game.Shop.MenuBoard>(), Lookup(typeof(Worktown.Game.Economy.EconomyService), "MenuBoardNow"), "the economy's board");
                Station register = LayoutQuery.Station(StationKind.Register);
                Assert.AreSame(register, Lookup(typeof(Worktown.Game.Npc.CustomerService), "RegisterStation"));
                Assert.AreSame(register.GetComponent<Worktown.Game.Shop.Register>(), Lookup(typeof(Worktown.Game.Npc.CustomerService), "RegisterNow"), "the guests' register");
                Type cleaning = typeof(Worktown.Game.Shop.CleaningService);
                Assert.AreSame(LayoutQuery.Station(StationKind.PlateShelf), Lookup(cleaning, "PlateShelf"));
                Assert.AreSame(LayoutQuery.Station(StationKind.DishReturn), Lookup(cleaning, "DishReturn"));
                Assert.AreSame(LayoutQuery.Station(StationKind.Sink), Lookup(cleaning, "SinkStation"));
                Assert.AreSame(LayoutQuery.Station(StationKind.TrashCan), Lookup(cleaning, "TrashCan"));
            }
            finally { LayoutScene.Close(scene); }
            Assert.IsNull(typeof(Worktown.Game.Economy.EconomyService).GetProperty("MenuBoardNow", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null),
                "no board once the layout is gone");
        }

        [Test]
        public void Numbered_WritesTheNumber_OrDropsTheMark()
        {
            Assert.AreEqual("Stove 2", PieceBehaviour.Numbered("Stove #", 2));
            Assert.AreEqual("Stove", PieceBehaviour.Numbered("Stove #", 0));
            Assert.AreEqual("Sink", PieceBehaviour.Numbered("Sink", 3));
        }

        [Test]
        public void ItemName_NumbersOnlyKindsWithMoreThanOneHome()
        {
            ShopLayout layout = DefaultLayout.Load();
            ItemHome[] plates = layout.Homes.Where(h => h.ItemKind == "plate").ToArray();
            Assert.AreEqual(2, plates.Length);
            Assert.AreEqual("Plate 1", LayoutSpawner.ItemName("plate", layout, plates[0]));
            Assert.AreEqual("Plate 2", LayoutSpawner.ItemName("plate", layout, plates[1]));
            Assert.AreEqual("Cutting board", LayoutSpawner.ItemName("board", layout, layout.Homes.First(h => h.ItemKind == "board")));
            Assert.AreEqual("Pan", LayoutSpawner.ItemName("pan", layout, layout.Homes.First(h => h.ItemKind == "pan")));
        }
    }
}
