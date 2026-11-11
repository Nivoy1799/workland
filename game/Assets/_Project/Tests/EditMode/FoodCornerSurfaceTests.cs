using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Save;
using Worktown.Game.Tutorial;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 053: the network surface grows only by ShopStage's three synced fields (AC1), the tutorial's stage 1 steps, the command line
    /// (-new-shop, -continue, -save-dir, the corner tour), the title screen's lines and the D27 word check over every new text (AC12).
    /// </summary>
    public class FoodCornerSurfaceTests
    {
        [Test]
        public void TheSurface_GrowsOnlyByShopStagesThreeSyncedFields()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            List<string> stage = lines.Where(l => l.Contains("Worktown.Game.Climb.")).ToList();
            CollectionAssert.AreEquivalent(new[]
            {
                "sync Worktown.Game.Climb.ShopStage.Stage:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
                "sync Worktown.Game.Climb.ShopStage.GoalMoney:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>",
                "sync Worktown.Game.Climb.ShopStage.GoalReached:FishNet.Object.Synchronizing.SyncVar`1<System.Boolean>",
            }, stage, "no RPC, no broadcast: three synced fields");
            Assert.IsFalse(lines.Any(l => l.Contains("Worktown.Game.Save.")), "the save is never on the wire (only the host reads and writes it)");
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
        }

        [Test]
        public void TheFoodCornerTutorial_KeepsOnlyTheFriesSteps()
        {
            CollectionAssert.AreEqual(new[]
            {
                TutorialStepId.MoveAndLook, TutorialStepId.TakePotato, TutorialStepId.CutSticks, TutorialStepId.FrySticks,
                TutorialStepId.PlateFries, TutorialStepId.ServeGuest, TutorialStepId.TakePayment, TutorialStepId.Done,
            }, TutorialSteps.ForStage(ShopStageRules.FoodCorner).Select(s => s.Id).ToArray());
            Assert.AreSame(TutorialSteps.Default, TutorialSteps.ForStage(ShopStageRules.OpenKitchen));
            IReadOnlyList<TutorialStep> corner = TutorialSteps.ForStage(ShopStageRules.FoodCorner);
            StringAssert.Contains("cash", corner[6].Text);
            StringAssert.DoesNotContain("Card", corner[6].Text, "no card at the food corner");
            StringAssert.DoesNotContain("wrap", corner[5].Text);
            _ = new TutorialProgress(corner); // a valid step list
        }

        [Test]
        public void TheCommandLine_NewShopContinueAndTheSaveFolder()
        {
            LaunchArgs n = LaunchArgs.Parse(new[] { "-new-shop" });
            Assert.IsTrue(n.IsValid, n.Error);
            Assert.AreEqual(LaunchMode.Host, n.Mode);
            Assert.IsTrue(n.NewShop && n.HostsSave);
            LaunchArgs c = LaunchArgs.Parse(new[] { "-continue", "-save-dir", "D:/saves" });
            Assert.IsTrue(c.IsValid, c.Error);
            Assert.IsTrue(c.ContinueShop);
            Assert.AreEqual("D:/saves", c.SaveDir);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host" }).HostsSave, "-host is the open kitchen");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-new-shop", "-host" }).IsValid, "one mode flag");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-save-dir" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-connect", "1.2.3.4", "-save-dir", "D:/x" }).IsValid, "a client has no saves");
            Assert.IsTrue(LaunchArgs.Parse(new[] { "-save-dir", "D:/x" }).IsValid, "the title screen with a folder");

            LaunchArgs tour = LaunchArgs.Parse(new[] { "-tour", "corner", "-save-dir", "D:/tour" });
            Assert.IsTrue(tour.IsValid, tour.Error);
            Assert.IsTrue(tour.TitleTour);
            Assert.IsTrue(tour.IsScripted);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-tour", "corner" }).IsValid, "the corner tour brings its own folder");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-tour", "corner", "-host", "-save-dir", "D:/t" }).IsValid, "it starts on the title screen");
            // Scripted runs never use the player's saves.
            Assert.IsNull(SaveStore.FolderFor(LaunchArgs.Parse(new[] { "-tour", "look", "-connect", "1.2.3.4" }).SaveDir, true, "C:/data"));
        }

        [Test]
        public void TheTitleScreen_ShowsContinueWithItsLine()
        {
            CollectionAssert.AreEqual(new[] { "New shop" }, MenuRules.HostChoices(false));
            CollectionAssert.AreEqual(new[] { "Continue", "New shop" }, MenuRules.HostChoices(true));
            Assert.AreEqual("Day 4 \u00b7 $2.30", MenuRules.ContinueLine(4, 230)); // spec 037 re-pin: cents (D39)
            Assert.AreEqual("This save could not be loaded. Your file is kept.", MenuRules.SaveErrorText(SaveError.NotJson));
            Assert.AreEqual("This save could not be loaded. Your file is kept.", MenuRules.SaveErrorText(SaveError.Invalid));
            Assert.AreEqual("This shop was saved by a newer version of Worktown.", MenuRules.SaveErrorText(SaveError.NewerVersion));
        }

        [Test]
        public void EveryNewText_PassesTheD27WordCheck()
        {
            var texts = new List<string>
            {
                ShopStageRules.NotOpenText, FoodCornerRules.GoalReachedText, FoodCornerRules.ShopName, FoodCornerRules.GoalLine(230, 400),
                FoodCornerRules.GoalProgressLine(230, 400), FoodCornerRules.StockGood, FoodCornerRules.DishId, FoodCornerRules.StartUnlock,
                TutorialSteps.CornerServeText, TutorialSteps.CornerPayText, TutorialSteps.CornerDoneText, MenuRules.ContinueLine(4, 230),
                MenuRules.BrokenContinueLine, MenuRules.SaveErrorTitle, MenuRules.SaveErrorText(SaveError.NotJson),
                MenuRules.SaveErrorText(SaveError.NewerVersion), HudView.GoalText(ShopStageRules.FoodCorner, 1, 2, true),
            };
            foreach (TutorialStep step in TutorialSteps.FoodCorner) texts.Add(step.Text);
            foreach (string text in texts) Assert.IsNull(ContentRules.FindForbidden(text), text);
        }
    }
}
