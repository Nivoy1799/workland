using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Layout;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062a AC8: the save's layout part (format version 3), its migration from version 2 and its codec.</summary>
    public class LayoutSaveTests
    {
        private const string Id = "0123456789abcdef0123456789abcdef";
        private const string Time1 = "2026-11-10T12:00:00.0000000Z";

        private static string V2 => "{\"format\":\"worktown-shop-save\",\"formatVersion\":2,\"saveId\":\"" + Id + "\",\"createdUtc\":\"" + Time1
            + "\",\"savedUtc\":\"" + Time1 + "\",\"money\":{\"balance\":12345}}";

        [Test]
        public void AVersion2Save_MigratesToTheDefaultLayout()
        {
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(V2);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(5, r.Save.formatVersion); // spec 057: then v3 to v4; spec 059: v4 to v5
            Assert.AreEqual(12345, r.Save.money.balance, "nothing else changes");
            ShopLayout migrated = LayoutCodec.ToLayout(r.Save.layout, PieceCatalog.CreateDefault());
            Assert.AreEqual(DefaultLayout.Load(), migrated);
            Assert.AreEqual(29, migrated.Pieces.Count); // spec 057: the goal board; spec 059: the office terminal
        }

        [Test]
        public void AFullLayout_RoundTripsEqual_ThroughTheSaveFile()
        {
            ShopLayout standard = DefaultLayout.Load();
            var pieces = new List<LayoutPiece>(standard.Pieces.Select(p => p.Id == 7 ? p with { XMm = p.XMm + 250, Tier = 2, PaidCents = 4999 } : p));
            var layout = new ShopLayout(ShopLayout.CurrentVersion, pieces, new[] { 40 }, standard.Homes, 41);
            ShopSave save = ShopSaveCodecTests.Full();
            save.layout = LayoutCodec.ToPart(layout);
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.IsFalse(r.Notes.Any(n => n.Contains("piece")), string.Join(", ", r.Notes)); // nothing of the layout was clamped
            Assert.AreEqual(layout, LayoutCodec.ToLayout(r.Save.layout, PieceCatalog.CreateDefault()));
        }

        [Test]
        public void AnUnknownType_IsKeptInTheFile()
        {
            ShopLayout standard = DefaultLayout.Load();
            var pieces = new List<LayoutPiece>(standard.Pieces) { new LayoutPiece(90, "hot_tub", 1000, 2000, 1, 0, 1, 0) };
            ShopSave save = ShopSaveCodecTests.Full();
            save.layout = LayoutCodec.ToPart(standard.WithPieces(pieces));
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok);
            ShopSave.PiecePart kept = r.Save.layout.pieces.Single(p => p.id == 90);
            Assert.AreEqual("hot_tub", kept.type);
            Assert.AreEqual(1000, kept.xMm);
            Assert.AreEqual(1, kept.turns);
            ShopLayout chosen = LayoutSpawner.Choose(LayoutCodec.ToLayout(r.Save.layout, PieceCatalog.CreateDefault()), standard,
                PieceCatalog.CreateDefault(), LayoutScene.Prefabs, out bool reset, out _);
            Assert.IsFalse(reset, "an unknown type does not reset the layout");
            Assert.IsNotNull(chosen.Piece(90), "and stays in it for the next save");
        }

        [Test]
        public void Version99_IsNewer()
        {
            ShopSave save = ShopSaveCodecTests.Full();
            string json = ShopSaveCodec.Default.ToJson(save).Replace("\"formatVersion\": 5", "\"formatVersion\": 99");
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(json).Error);
        }

        [Test]
        public void TheCodec_ClampsOutOfRangeValues_AndNotesThem()
        {
            var part = new ShopSave.LayoutPart
            {
                version = 1,
                nextId = 2,
                pieces = new[]
                {
                    new ShopSave.PiecePart { id = 1, type = PieceCatalog.Worktop, xMm = 99999999, zMm = 0, turns = 6, lengthMm = 50, tier = 0, paidCents = -5 },
                    new ShopSave.PiecePart { id = 1, type = PieceCatalog.Sink }, // the id twice
                    new ShopSave.PiecePart { id = 0, type = PieceCatalog.Sink },
                    new ShopSave.PiecePart { id = 3, type = PieceCatalog.Stove, lengthMm = 700 },
                },
                homes = new[] { new ShopSave.HomePart { item = "pan", piece = 3, local = 99, spot = 0 } },
            };
            var notes = new List<string>();
            ShopLayout layout = LayoutCodec.ToLayout(part, PieceCatalog.CreateDefault(), notes);
            Assert.AreEqual(2, layout.Pieces.Count);
            LayoutPiece worktop = layout.Piece(1);
            Assert.AreEqual(PosePacking.MaxCoordinate, worktop.XMm);
            Assert.AreEqual(2, worktop.Turns);
            Assert.AreEqual(300, worktop.LengthMm, "the worktop's minimum");
            Assert.AreEqual(1, worktop.Tier);
            Assert.AreEqual(0, worktop.PaidCents);
            Assert.AreEqual(0, layout.Piece(3).LengthMm, "a fixed piece has no length");
            Assert.IsEmpty(layout.Homes, "a home on local 99 is dropped");
            Assert.AreEqual(4, layout.NextId, "above every id in use");
            part.nextId = int.MaxValue;
            Assert.AreEqual(ShopLayout.MaxPieceId, LayoutCodec.ToLayout(part, PieceCatalog.CreateDefault()).NextId, "062a review: clamped to the largest id");
            part.nextId = -7;
            Assert.AreEqual(4, LayoutCodec.ToLayout(part, PieceCatalog.CreateDefault()).NextId);
            Assert.GreaterOrEqual(notes.Count, 5, string.Join(", ", notes));
        }

        [Test]
        public void Choose_TakesTheStandardForNoLayout_AndBackfillsARequiredPiece()
        {
            PieceCatalog catalog = PieceCatalog.CreateDefault();
            ShopLayout standard = DefaultLayout.Load();
            Assert.AreSame(standard, LayoutSpawner.Choose(null, standard, catalog, null, out bool reset, out _));
            Assert.IsFalse(reset);
            Assert.AreSame(standard, LayoutSpawner.Choose(new ShopLayout(1, null, null, null, 1), standard, catalog, null, out _, out _));

            // Without its sink, the sink comes back at its standard place with a new id.
            ShopLayout noSink = standard.WithPieces(standard.Pieces.Where(p => p.TypeId != PieceCatalog.Sink));
            ShopLayout filled = LayoutSpawner.Choose(noSink, standard, catalog, null, out reset, out List<string> notes);
            Assert.IsFalse(reset);
            LayoutPiece sink = filled.Pieces.Single(p => p.TypeId == PieceCatalog.Sink);
            LayoutPiece old = standard.Pieces.Single(p => p.TypeId == PieceCatalog.Sink);
            Assert.AreEqual(old.XMm, sink.XMm);
            Assert.AreEqual(standard.NextId, sink.Id, "a new id, never a reused one");
            StringAssert.Contains("added sink", string.Join(", ", notes));

            // With its place taken, the sink is stored and the layout misses a required piece: the standard layout instead.
            var blocked = new List<LayoutPiece>(noSink.Pieces) { new LayoutPiece(77, PieceCatalog.Stove, old.XMm, old.ZMm, 0, 0, 1, 0) };
            ShopLayout taken = LayoutSpawner.Choose(noSink.WithPieces(blocked), standard, catalog, null, out reset, out notes);
            Assert.IsTrue(reset, string.Join(", ", notes));
            Assert.AreSame(standard, taken);
        }

        /// <summary>062a review: with no piece id left, a missing required piece is not added (the layout then falls back).</summary>
        [Test]
        public void Backfill_RefusesAnIdPastTheLimit()
        {
            PieceCatalog catalog = PieceCatalog.CreateDefault();
            ShopLayout standard = DefaultLayout.Load();
            ShopLayout noSink = new ShopLayout(1, standard.Pieces.Where(p => p.TypeId != PieceCatalog.Sink), null, standard.Homes, ShopLayout.MaxPieceId + 1);
            Assert.Greater(noSink.NextId, ShopLayout.MaxPieceId);
            var notes = new List<string>();
            ShopLayout filled = DefaultLayout.Backfill(noSink, standard, catalog, null, notes);
            Assert.AreEqual(0, filled.CountOf(PieceCatalog.Sink));
            StringAssert.Contains("no piece id left for sink", string.Join(", ", notes));
            LayoutSpawner.Choose(noSink, standard, catalog, null, out bool reset, out _);
            Assert.IsTrue(reset, "a layout without a sink falls back to the standard one");
        }

        [Test]
        public void ANewShop_AndTheOpenKitchen_SaveTheLayoutTheySpawned()
        {
            ShopSave save = SaveService.NewSave(Id, 7, 1000, Time1, "0.1", "hash");
            Assert.AreEqual(5, save.formatVersion); // spec 057: 4; spec 059: 5
            Assert.IsNotNull(save.layout);
            ShopSave.LayoutPart part = DefaultLayout.Part();
            Assert.AreEqual(29, part.pieces.Length); // spec 057: the goal board; spec 059: the office terminal
            Assert.AreEqual(9, part.homes.Length);
            Assert.AreEqual(30, part.nextId); // spec 059: the office terminal took 29
        }
    }
}
