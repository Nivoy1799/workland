using NUnit.Framework;
using Worktown.Game.Npc;
using Worktown.Game.Office;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 059 AC8: the change log's entries pack and unpack; each action has its words; an unknown slot reads "A cook".</summary>
    public class ChangeLogTests
    {
        [Test]
        public void Entries_RoundTrip()
        {
            for (int slot = 0; slot <= ChangeLog.UnknownSlot; slot++)
                foreach (int action in new[] { ChangeLog.PriceSet, ChangeLog.SpecialSet, ChangeLog.DishOn, ChangeLog.DishOff, 63 })
                    foreach (int argument in new[] { 0, 1, 0x7FF, 0x8000, 0xFFFF })
                    {
                        ChangeLog.Unpack(ChangeLog.Pack(slot, action, argument), out int s, out int a, out int x);
                        Assert.AreEqual((slot, action, argument), (s, a, x));
                    }
            ChangeLog.Unpack(ChangeLog.Pack(40, 1, 0), out int unknown, out _, out _);
            Assert.AreEqual(ChangeLog.UnknownSlot, unknown, "a slot past 30 is unknown");
            ChangeLog.Unpack(ChangeLog.Pack(-1, 1, 0), out unknown, out _, out _);
            Assert.AreEqual(ChangeLog.UnknownSlot, unknown);
            Assert.AreEqual(ChangeLog.UnknownSlot, ChangeLog.SlotOf(31));
            Assert.AreEqual(7, ChangeLog.SlotOf(7));
            uint[] log = new uint[0];
            for (int i = 0; i < 11; i++) log = ChangeLog.Push(log, ChangeLog.Pack(1, ChangeLog.DishOn, i));
            Assert.AreEqual(ChangeLog.Kept, log.Length);
            ChangeLog.Unpack(log[0], out _, out _, out int oldest);
            Assert.AreEqual(3, oldest, "the oldest dropped");
        }

        [Test]
        public void EachAction_HasItsWords()
        {
            string Mira(int slot) => slot == 4 ? "Mira" : null;
            int fries = MenuPrices.LineOfDish(Menu.Fries);
            Assert.AreEqual("Mira: fries $4.50", ChangeLog.Text(ChangeLog.Price(4, fries, 450), Mira));
            Assert.AreEqual("Mira: falafel wrap is the special", ChangeLog.Text(ChangeLog.Special(4, Menu.FalafelWrap), Mira));
            Assert.AreEqual("Mira: no special today", ChangeLog.Text(ChangeLog.Special(4, Menu.None), Mira));
            Assert.AreEqual("Mira: falafel wrap on the menu next", ChangeLog.Text(ChangeLog.Dish(4, 0, true), Mira));
            Assert.AreEqual("Mira: drinks off the menu next", ChangeLog.Text(ChangeLog.Dish(4, 3, false), Mira));
            Assert.AreEqual("Mira: changed something", ChangeLog.Text(ChangeLog.Pack(4, 50, 0), Mira), "an action of a newer build");
            Assert.AreEqual("A cook: fries $4.50", ChangeLog.Text(ChangeLog.Price(5, fries, 450), Mira), "not in the roster");
            Assert.AreEqual("A cook: fries $4.50", ChangeLog.Text(ChangeLog.Price(ChangeLog.UnknownSlot, fries, 450), _ => "Nobody"), "the unknown slot");
            Assert.AreEqual("A cook: fries $4.50", ChangeLog.Text(ChangeLog.Price(4, fries, 450), null));
            Assert.AreEqual("Lu", ChangeLog.NameIn(2, new[] { 1, 2 }, new[] { "Mira", "Lu" }));
            Assert.IsNull(ChangeLog.NameIn(3, new[] { 1, 2 }, new[] { "Mira", "Lu" }));
            Assert.IsNull(ChangeLog.NameIn(3, null, null));
        }
    }
}
