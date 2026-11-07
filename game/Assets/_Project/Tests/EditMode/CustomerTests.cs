using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Npc;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 014 AC1.</summary>
    public class CustomerTests
    {
        private static IReadOnlyList<string> Desired(int meal, int snack, int drink)
        {
            var demand = new Dictionary<string, int> { ["meal"] = meal, ["snack"] = snack, ["drink"] = drink };
            return CustomerRules.DesiredWishes(m => demand.TryGetValue(m, out int d) ? d : 0);
        }

        [Test]
        public void Desired_FollowsDemand_InWishOrder()
        {
            CollectionAssert.AreEqual(new[] { "meal", "snack", "snack", "drink" }, Desired(1, 2, 1));
            CollectionAssert.IsEmpty(Desired(0, 0, 0));
            CollectionAssert.IsEmpty(Desired(-3, 0, 0));
        }

        [Test]
        public void Desired_IsCappedAtSix_MostValuableFirst()
        {
            IReadOnlyList<string> wishes = Desired(4, 7, 16);
            Assert.AreEqual(CustomerRules.MaxVisible, wishes.Count);
            CollectionAssert.AreEqual(new[] { "meal", "meal", "meal", "meal", "snack", "snack" }, wishes);
        }

        [Test]
        public void Reconcile_KeepsMatching_RemovesExtra_AddsMissing()
        {
            var queue = new CustomerQueue();
            queue.Add(1, "meal");
            queue.Add(2, "snack");
            queue.Add(3, "drink");
            var leaving = new List<QueuedCustomer>();
            var joining = new List<string>();

            queue.Reconcile(new[] { "meal", "meal", "drink" }, leaving, joining);

            CollectionAssert.AreEqual(new[] { 1, 3 }, IdsOf(queue.Entries));
            Assert.AreEqual(1, leaving.Count);
            Assert.AreEqual(2, leaving[0].Id, "the snack customer was served by an NPC business");
            CollectionAssert.AreEqual(new[] { "meal" }, joining);
        }

        [Test]
        public void Serve_PrefersMatchingWish_ElseFirst()
        {
            var queue = new CustomerQueue();
            queue.Add(1, "snack");
            queue.Add(2, "meal");
            queue.Add(3, "meal");

            Assert.IsTrue(queue.TryServe("meal", out QueuedCustomer served));
            Assert.AreEqual(2, served.Id, "first meal customer, not the first in line");
            Assert.IsTrue(queue.TryServe("drink", out served));
            Assert.AreEqual(1, served.Id, "nobody wants a drink: first in line takes it");
            CollectionAssert.AreEqual(new[] { 3 }, IdsOf(queue.Entries));

            queue.TryServe("meal", out _);
            Assert.IsFalse(queue.TryServe("meal", out _), "empty queue");
        }

        [Test]
        public void QueueSlots_AreOneMetreApart()
        {
            Vector3 head = new Vector3(8.5f, 0f, 11f);
            Assert.AreEqual(head, CustomerRules.QueueSlot(head, Vector3.right, 0));
            Assert.AreEqual(1f, Vector3.Distance(CustomerRules.QueueSlot(head, Vector3.right, 2), CustomerRules.QueueSlot(head, Vector3.right, 3)), 0.0001f);
            Assert.AreEqual(head, CustomerRules.QueueSlot(head, Vector3.right, -4), "negative index clamps to the head");
        }

        [Test]
        public void WishText()
        {
            Assert.AreEqual("Meal?", CustomerRules.WishText("meal"));
            Assert.AreEqual("?", CustomerRules.WishText(""));
        }

        private static int[] IdsOf(IReadOnlyList<QueuedCustomer> entries)
        {
            var ids = new int[entries.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = entries[i].Id;
            return ids;
        }
    }
}
