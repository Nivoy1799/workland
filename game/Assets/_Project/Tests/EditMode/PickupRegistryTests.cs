using NUnit.Framework;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 003 AC5: two clients requesting the same item, exactly one gets it.</summary>
    public class PickupRegistryTests
    {
        private const int Item = 7;
        private const int ClientA = 1;
        private const int ClientB = 2;

        [Test]
        public void SameItemRequestedByTwoClients_ExactlyOneGetsIt()
        {
            var registry = new PickupRegistry();

            bool a = registry.TryClaim(Item, ClientA);
            bool b = registry.TryClaim(Item, ClientB);

            Assert.AreEqual(1, (a ? 1 : 0) + (b ? 1 : 0));
            Assert.IsTrue(a, "first request processed by the server wins");
            Assert.AreEqual(ClientA, registry.HolderOf(Item));
        }

        [Test]
        public void ManyClientsRacingForOneItem_ExactlyOneWins()
        {
            var registry = new PickupRegistry();
            int winners = 0;
            for (int client = 0; client < 32; client++)
                if (registry.TryClaim(Item, client)) winners++;
            Assert.AreEqual(1, winners);
        }

        [Test]
        public void ClientCanHoldOnlyOneItem()
        {
            var registry = new PickupRegistry();
            Assert.IsTrue(registry.TryClaim(1, ClientA));
            Assert.IsFalse(registry.TryClaim(2, ClientA));
            Assert.AreEqual(PickupRegistry.NoHolder, registry.HolderOf(2));
        }

        [Test]
        public void OnlyTheHolderCanRelease()
        {
            var registry = new PickupRegistry();
            registry.TryClaim(Item, ClientA);

            Assert.IsFalse(registry.TryRelease(Item, ClientB));
            Assert.AreEqual(ClientA, registry.HolderOf(Item));

            Assert.IsTrue(registry.TryRelease(Item, ClientA));
            Assert.AreEqual(PickupRegistry.NoHolder, registry.HolderOf(Item));
        }

        [Test]
        public void ReleasedItemCanBeClaimedAgain()
        {
            var registry = new PickupRegistry();
            registry.TryClaim(Item, ClientA);
            registry.TryRelease(Item, ClientA);
            Assert.IsTrue(registry.TryClaim(Item, ClientB));
        }

        [Test]
        public void Disconnect_ReleasesHeldItem()
        {
            var registry = new PickupRegistry();
            registry.TryClaim(Item, ClientA);

            Assert.IsTrue(registry.ReleaseAllFor(ClientA, out int released));
            Assert.AreEqual(Item, released);
            Assert.AreEqual(PickupRegistry.NoHolder, registry.HolderOf(Item));
            Assert.IsFalse(registry.TryGetHeldItem(ClientA, out _));
            Assert.IsFalse(registry.ReleaseAllFor(ClientA, out _));
        }

        [Test]
        public void NegativeClientId_IsRefused()
        {
            Assert.IsFalse(new PickupRegistry().TryClaim(Item, -1));
        }
    }
}
