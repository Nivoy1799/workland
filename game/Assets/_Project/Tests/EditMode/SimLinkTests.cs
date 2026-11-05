using NUnit.Framework;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 003 AC6: a /sim type is callable from game code, same hash as the /sim unit test.</summary>
    public class SimLinkTests
    {
        // Pinned in sim/Sim.Tests~/Cooking/ItemIdentityTests.cs (AC9_PinnedVectors): sha256("v1|b|potato").
        private const string PinnedPotatoHash = "2a4e461a0b881d570bbee19bc9318cf890af884214314e73116f237a26d9abc6";

        [Test]
        public void PotatoHash_MatchesPinnedSimValue()
        {
            Assert.AreEqual(PinnedPotatoHash, SimLink.PotatoHash());
        }
    }
}
