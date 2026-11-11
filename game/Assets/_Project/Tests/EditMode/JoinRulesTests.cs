using System.Linq;
using NUnit.Framework;
using Worktown.Game.Net.Rules;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 047 AC2: the version check, the places, the kick rule, the words of the screens, Steam's connect string.</summary>
    public class JoinRulesTests
    {
        [Test]
        public void TheSameBuild_AndAFreePlace_IsAdmitted()
        {
            Assert.AreEqual(DisconnectReason.None, JoinRules.Decide("abc", "abc", 0, 16));
            Assert.AreEqual(DisconnectReason.None, JoinRules.Decide("abc", "abc", 15, 16), "the 16th player");
        }

        [TestCase("abd")]
        [TestCase("")]
        [TestCase(null)]
        public void AnotherBuild_IsRefused_AsAVersionMismatch(string client)
        {
            Assert.AreEqual(DisconnectReason.VersionMismatch, JoinRules.Decide(client, "abc", 0, 16));
        }

        [Test]
        public void AFullShop_IsRefused_AndTheVersionComesFirst()
        {
            Assert.AreEqual(DisconnectReason.ServerFull, JoinRules.Decide("abc", "abc", 16, 16));
            Assert.AreEqual(DisconnectReason.ServerFull, JoinRules.Decide("abc", "abc", 3, 3), "-max-players");
            Assert.AreEqual(DisconnectReason.VersionMismatch, JoinRules.Decide("x", "abc", 16, 16));
            Assert.AreEqual(DisconnectReason.ServerFull, JoinRules.Decide("abc", "abc", JoinRules.MaxPlayers, 0), "a bad cap falls back to D36");
            Assert.AreEqual(16, JoinRules.MaxPlayers, "D36");
        }

        [Test]
        public void OnlyTheHost_Kicks_AndNeverItself()
        {
            Assert.IsTrue(JoinRules.CanKick(asServer: true, targetIsHost: false));
            Assert.IsFalse(JoinRules.CanKick(asServer: true, targetIsHost: true));
            Assert.IsFalse(JoinRules.CanKick(asServer: false, targetIsHost: false));
        }

        [Test]
        public void Names_AreCleanedAndCut()
        {
            Assert.AreEqual("Ann", JoinRules.CleanName("  Ann ", 0));
            Assert.AreEqual("Ann Bo", JoinRules.CleanName("Ann \t\n  Bo", 0));
            Assert.AreEqual("AnnBo", JoinRules.CleanName("Ann\u0007Bo", 0), "control characters dropped");
            Assert.AreEqual("Cook 4", JoinRules.CleanName("   ", 3));
            Assert.AreEqual("Cook 1", JoinRules.CleanName(null, 0));
            Assert.AreEqual(JoinRules.MaxNameLength, JoinRules.CleanName(new string('x', 100), 0).Length);
            Assert.AreEqual("e\u0301x", JoinRules.CleanName("e\u0301\u0362\u0362\u0362x", 0), "one combining mark per letter");
            Assert.AreEqual("a", JoinRules.CleanName("\u0301a", 0), "no mark at the start");
            Assert.AreEqual("size=400X", JoinRules.CleanName("<size=400>X", 0), "no rich-text tag survives");
            Assert.AreEqual("bAnn/b", JoinRules.CleanName("<b>Ann</b>", 0));
            Assert.AreEqual("AnnBo", JoinRules.CleanName("Ann\u202eBo", 0), "a bidi override is removed");
            Assert.AreEqual("AnnBo", JoinRules.CleanName("Ann\u200bBo\u2066", 0), "zero-width and a bidi isolate are removed");
            Assert.AreEqual("Cook 1", JoinRules.CleanName("\u200d\ufeff", 0), "only format characters is empty");
        }

        [Test]
        public void EveryReason_HasATitleAndAText()
        {
            foreach (DisconnectReason reason in System.Enum.GetValues(typeof(DisconnectReason)).Cast<DisconnectReason>())
            {
                if (reason == DisconnectReason.None) continue;
                Assert.IsNotEmpty(ConnectionMessages.Title(reason), reason.ToString());
                if (reason != DisconnectReason.Left) Assert.IsNotEmpty(ConnectionMessages.Text(reason, string.Empty), reason.ToString());
            }
            StringAssert.Contains("host build 1234, yours 5678", ConnectionMessages.Text(DisconnectReason.VersionMismatch, "host build 1234, yours 5678"));
            StringAssert.Contains("All 16 places", ConnectionMessages.Text(DisconnectReason.ServerFull, "16"));
            StringAssert.Contains("10.0.0.2:7770", ConnectionMessages.Text(DisconnectReason.Unreachable, "10.0.0.2:7770"));
            StringAssert.Contains("30 s", ConnectionMessages.Text(DisconnectReason.ConnectionLost, string.Empty));
        }

        [Test]
        public void TheRejoinLine_CountsWholeSecondsDown()
        {
            Assert.AreEqual("Connection lost. Rejoining... 30 s", ConnectionMessages.Rejoining(29.2f));
            Assert.AreEqual("Connection lost. Rejoining... 1 s", ConnectionMessages.Rejoining(0.1f));
            Assert.AreEqual("Connection lost. Rejoining... 0 s", ConnectionMessages.Rejoining(-3f));
            StringAssert.Contains("10.0.0.2:7770", ConnectionMessages.Stage(JoinStage.Connecting, "10.0.0.2:7770"));
            StringAssert.Contains("version", ConnectionMessages.Stage(JoinStage.CheckingVersion, string.Empty));
        }

        [Test]
        public void TheSteamConnectString_IsTheCommandLineFlag()
        {
            Assert.AreEqual("-steam 76561197960287930", RichPresenceRules.ConnectString(76561197960287930UL));
            Assert.IsTrue(RichPresenceRules.TryParseConnect("-steam 76561197960287930", out ulong id));
            Assert.AreEqual(76561197960287930UL, id);
            Assert.IsTrue(RichPresenceRules.TryParseConnect("+foo -steam  76561197960287930 -x", out id), "extra words around it");
            Assert.IsFalse(RichPresenceRules.TryParseConnect("-steam", out _));
            Assert.IsFalse(RichPresenceRules.TryParseConnect("-steam abc", out _));
            Assert.IsFalse(RichPresenceRules.TryParseConnect("-steam 0", out _));
            Assert.IsFalse(RichPresenceRules.TryParseConnect(null, out _));
            Assert.AreEqual("Running a shop (3/16)", RichPresenceRules.Status(3, 16));
            LaunchArgs args = LaunchArgs.Parse(("Worktown.exe " + RichPresenceRules.ConnectString(5UL)).Split(' '));
            Assert.AreEqual(LaunchMode.ClientSteam, args.Mode, "Steam starts the game with the same flag");
            Assert.AreEqual("5", args.SteamTarget);
        }

        [Test]
        public void TheLobby_ListsTheHostFirst_AndKicksOnlyForTheHost()
        {
            LobbyLine[] host = LobbyLine.Build(new[] { 4, 0, 2 }, new[] { "Cy", "Ann", "Bo" }, hostId: 0, ownId: 0, asServer: true);
            CollectionAssert.AreEqual(new[] { 0, 2, 4 }, host.Select(l => l.ClientId).ToArray());
            Assert.AreEqual("Ann (host) (you)", host[0].Text);
            CollectionAssert.AreEqual(new[] { false, true, true }, host.Select(l => l.CanKick).ToArray());

            LobbyLine[] guest = LobbyLine.Build(new[] { 4, 0, 2 }, new[] { "Cy", "Ann", "Bo" }, hostId: 0, ownId: 2, asServer: false);
            Assert.AreEqual("Bo (you)", guest[1].Text);
            Assert.IsTrue(guest.All(l => !l.CanKick), "only the host kicks");

            Assert.IsEmpty(LobbyLine.Build(new[] { 1 }, new string[0], 1, 1, true), "a broken roster shows nobody");
            Assert.IsEmpty(LobbyLine.Build(null, null, -1, -1, false));
            Assert.AreEqual("Players 3/16", LobbyLine.Heading(3, 16));
        }

        [Test]
        public void TheTitleScreen_OffersContinue_OnlyWithASave()
        {
            CollectionAssert.AreEqual(new[] { "New shop" }, MenuRules.HostChoices(false));
            CollectionAssert.AreEqual(new[] { "Continue", "New shop" }, MenuRules.HostChoices(true));
            Assert.AreEqual("build 0.1.0 (a1b2c3d4)", MenuRules.BuildLine("0.1.0", "a1b2c3d4"));
        }

        [Test]
        public void TheMenuCamera_LooksAtTheShop_FromTheStreet_AndSwaysSlowly()
        {
            UnityEngine.Pose a = MenuRules.CameraPose(0d);
            UnityEngine.Pose b = MenuRules.CameraPose(MenuRules.SwaySeconds * 0.25d);
            foreach (UnityEngine.Pose p in new[] { a, b })
            {
                UnityEngine.Vector3 toTarget = (MenuRules.CameraTarget - p.position).normalized;
                Assert.Greater(UnityEngine.Vector3.Dot(p.rotation * UnityEngine.Vector3.forward, toTarget), 0.999f);
                Assert.Less(p.position.z, MenuRules.CameraTarget.z, "south of the shop, on the street side");
                Assert.Greater(p.position.y, 2f);
            }
            Assert.Greater(UnityEngine.Vector3.Distance(a.position, b.position), 1f, "it moves");
            Assert.Less(UnityEngine.Vector3.Distance(a.position, MenuRules.CameraPose(0.1d).position), 0.05f, "slowly");
        }

        [Test]
        public void TheLeavingHoldersItem_GoesBack_OnlyToAFreePlace()
        {
            Assert.IsTrue(PickupOrigin.GoesBack(allowed: true, spotTaken: false, occupied: false));
            Assert.IsFalse(PickupOrigin.GoesBack(true, spotTaken: true, occupied: false));
            Assert.IsFalse(PickupOrigin.GoesBack(true, false, occupied: true));
            Assert.IsFalse(PickupOrigin.GoesBack(allowed: false, false, false), "the kitchen's veto: dropped");
            var origin = new PickupOrigin(false, 3, 4, UnityEngine.Vector3.one, UnityEngine.Quaternion.identity);
            Assert.AreEqual(PlaceSpots.None, origin.Surface, "not on a spot: no spot");
            Assert.AreEqual(PlaceSpots.None, origin.Spot);
        }

        private sealed class FreeWallet : Worktown.Game.Cooking.IDrinkWallet
        {
            public Worktown.Game.Cooking.CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost)
            {
                cost = 0;
                return Worktown.Game.Cooking.CookOutcome.Ok;
            }

            public bool CanAfford(int owner, string drinkId, int percent, bool withCup) => true;
            public Worktown.Game.Cooking.CookOutcome Short => Worktown.Game.Cooking.CookOutcome.NotEnoughMoney;
            public void Refund(int owner, string drinkId, int percent, bool withCup, long cost) { }
        }

        /// <summary>
        /// 047 review: the leaving holder's cup is not put back on a tap's spot the kitchen no longer allows it on (filled there, then
        /// lidded in the hands: a lidded cup does not go back under a tap), the same veto as a place; it drops instead.
        /// </summary>
        [Test]
        public void ALeavingHoldersCup_IsNotReturned_ToASpotItIsNotAllowedOn()
        {
            const int dispenser = 10, cup = 50, lemonade = 0, owner = 3;
            var kitchen = Worktown.Game.Cooking.KitchenState.CreateDemo();
            Assert.AreEqual(Worktown.Game.Cooking.CookOutcome.Ok, kitchen.TryCreateCup(out var item));
            kitchen.Register(cup, item);
            var desk = new Worktown.Game.Cooking.DrinkDesk(kitchen);
            var wallet = new FreeWallet();
            Assert.IsTrue(kitchen.PutCupOnSpot(dispenser, lemonade, cup));
            desk.Press(dispenser, lemonade, owner, HeatTestKit.StartTick, wallet);
            desk.Release(dispenser, lemonade, owner, HeatTestKit.StartTick + 54, HeatTestKit.TickRate, wallet, out _);
            Assert.IsTrue(kitchen.TakeCupOffSpot(cup, out _, out _, out _), "picked up from the lemonade tap's spot");
            var origin = new PickupOrigin(true, 2, lemonade, UnityEngine.Vector3.one, UnityEngine.Quaternion.identity);
            int asked = 0;
            System.Func<bool> canPlace = () =>
            {
                asked++;
                return desk.CanPlace(cup, lemonade) == Worktown.Game.Cooking.CookOutcome.Ok;
            };

            Assert.IsTrue(Worktown.Game.Net.PickupService.PlanReturn(origin, canPlace, false, false), "a filled cup: back to its tap");
            Assert.AreEqual(Worktown.Game.Cooking.CookOutcome.Ok, desk.Lid(cup, out _));
            Assert.IsFalse(Worktown.Game.Net.PickupService.PlanReturn(origin, canPlace, false, false), "lidded: not under the tap");
            Assert.AreEqual(2, asked, "the kitchen is asked, as for a place");

            var floor = new PickupOrigin(false, 0, 0, UnityEngine.Vector3.one, UnityEngine.Quaternion.identity);
            Assert.IsTrue(Worktown.Game.Net.PickupService.PlanReturn(floor, canPlace, false, false), "off the spot table: no veto");
            Assert.AreEqual(2, asked);
            Assert.IsFalse(Worktown.Game.Net.PickupService.PlanReturn(floor, null, false, occupied: true));
        }

        [Test]
        public void PlaceSpots_KnowsWhichSpotAnItemLiesOn()
        {
            var spots = new PlaceSpots();
            Assert.IsFalse(spots.TryGetSpotOf(7, out int surface, out int spot));
            Assert.AreEqual(PlaceSpots.None, surface);
            Assert.AreEqual(PlaceSpots.None, spot);
            Assert.IsTrue(spots.TryTake(12, 5, 7));
            Assert.IsTrue(spots.TryGetSpotOf(7, out surface, out spot));
            Assert.AreEqual(12, surface);
            Assert.AreEqual(5, spot);
            spots.Free(7);
            Assert.IsFalse(spots.TryGetSpotOf(7, out _, out _));
        }
    }
}
