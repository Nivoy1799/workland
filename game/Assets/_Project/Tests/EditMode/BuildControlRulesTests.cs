using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Layout;
using Worktown.Sim.Cooking;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062b AC6: each build mode input gives the table's action; the right click never picks up; who may enter; the texts.</summary>
    public class BuildControlRulesTests
    {
        [Test]
        public void EachInput_GivesTheTablesAction()
        {
            Assert.AreEqual(BuildAction.PickUp, BuildControlRules.ActionFor(BuildInput.LeftClick, false, true, false));
            Assert.AreEqual(BuildAction.TakeOut, BuildControlRules.ActionFor(BuildInput.LeftClick, false, false, true));
            Assert.AreEqual(BuildAction.None, BuildControlRules.ActionFor(BuildInput.LeftClick, false, false, false));
            Assert.AreEqual(BuildAction.None, BuildControlRules.ActionFor(BuildInput.LeftClick, true, true, false), "full hands pick up nothing");
            Assert.AreEqual(BuildAction.Place, BuildControlRules.ActionFor(BuildInput.RightClick, true, false, false));
            Assert.AreEqual(BuildAction.Place, BuildControlRules.ActionFor(BuildInput.RightClick, true, true, false));
            Assert.AreEqual(BuildAction.Store, BuildControlRules.ActionFor(BuildInput.RightClick, true, false, true));
            Assert.AreEqual(BuildAction.Turn, BuildControlRules.ActionFor(BuildInput.Wheel, true, false, false));
            Assert.AreEqual(BuildAction.Turn, BuildControlRules.ActionFor(BuildInput.Wheel, true, false, true));
            Assert.AreEqual(BuildAction.NextStored, BuildControlRules.ActionFor(BuildInput.Wheel, false, false, true));
            Assert.AreEqual(BuildAction.None, BuildControlRules.ActionFor(BuildInput.Wheel, false, true, false));
            Assert.AreEqual(BuildAction.Undo, BuildControlRules.ActionFor(BuildInput.Undo, false, false, false));
            Assert.AreEqual(BuildAction.Leave, BuildControlRules.ActionFor(BuildInput.Leave, true, true, true));
        }

        [Test]
        public void TheRightClick_NeverPicksUp()
        {
            foreach (bool piece in new[] { false, true })
                foreach (bool pallet in new[] { false, true })
                {
                    BuildAction a = BuildControlRules.ActionFor(BuildInput.RightClick, false, piece, pallet);
                    Assert.AreEqual(BuildAction.None, a, "empty hands, piece " + piece + ", pallet " + pallet);
                    Assert.AreNotEqual(BuildAction.PickUp, BuildControlRules.ActionFor(BuildInput.RightClick, true, piece, pallet));
                    Assert.AreNotEqual(BuildAction.TakeOut, BuildControlRules.ActionFor(BuildInput.RightClick, true, piece, pallet));
                }
        }

        [Test]
        public void Entering_WantsTheStage_AClosedShop_AndEmptyHands()
        {
            Assert.AreEqual(BuildControlRules.StageText, BuildControlRules.EntryRefusal(ShopDoorState.Closed, ShopStageRules.FoodCorner, true),
                "question 1: the food corner is a fixed cart");
            Assert.AreEqual(BuildControlRules.CloseFirstText, BuildControlRules.EntryRefusal(ShopDoorState.Open, ShopStageRules.FirstRestaurant, true));
            Assert.AreEqual(BuildControlRules.CloseFirstText, BuildControlRules.EntryRefusal(ShopDoorState.ClosingAfterService, ShopStageRules.FirstRestaurant, true));
            Assert.AreEqual(BuildControlRules.HandsFullText, BuildControlRules.EntryRefusal(ShopDoorState.Closed, ShopStageRules.FirstRestaurant, false));
            Assert.IsNull(BuildControlRules.EntryRefusal(ShopDoorState.Closed, ShopStageRules.FirstRestaurant, true));
            Assert.IsNull(BuildControlRules.EntryRefusal(ShopDoorState.Closed, ShopStageRules.OpenKitchen, true), "the open kitchen too (dev, tours)");
        }

        [Test]
        public void ThePrompts_NameTheButton()
        {
            Assert.AreEqual("Left click · pick up fryer", BuildControlRules.Prompt(false, BuildControlRules.NameOf(PieceCatalog.Fryer), false, false, null));
            Assert.AreEqual("Right click · place here   Wheel · turn", BuildControlRules.Prompt(true, null, false, true, null));
            Assert.AreEqual("Right click · store   Wheel · turn", BuildControlRules.Prompt(true, null, true, false, null));
            StringAssert.StartsWith("The fryer overlaps", BuildControlRules.Prompt(true, null, false, false, "The fryer overlaps the griddle"));
            StringAssert.StartsWith("Left click · take out", BuildControlRules.Prompt(false, null, true, false, null));
        }

        [Test]
        public void EveryText_PassesTheD27WordCheck_AndEveryRefusalHasOne()
        {
            var texts = new List<string> { BuildControlRules.CloseFirstText, BuildControlRules.HandsFullText, BuildControlRules.StageText,
                BuildControlRules.TightAisleText, StoragePallet.SignText };
            foreach (BuildOutcome o in System.Enum.GetValues(typeof(BuildOutcome)).Cast<BuildOutcome>())
            {
                string t = BuildControlRules.RefusalText(o, null);
                if (o == BuildOutcome.Ok || o == BuildOutcome.Busy) Assert.IsNull(t, o.ToString());
                else texts.Add(t);
            }
            foreach (LayoutProblemKind k in System.Enum.GetValues(typeof(LayoutProblemKind)).Cast<LayoutProblemKind>())
                texts.Add(BuildControlRules.ProblemText(k));
            foreach (string text in texts)
            {
                Assert.IsFalse(string.IsNullOrEmpty(text));
                Assert.IsNull(ContentRules.FindForbidden(text), text);
            }
        }
    }
}
