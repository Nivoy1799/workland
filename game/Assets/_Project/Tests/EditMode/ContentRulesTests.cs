using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 028 AC3 (D27): no animal-product word in any item id, display name, grocer entry, menu dish, name list or
    /// patch note. Art model file names and paths are not content and are not scanned.
    /// </summary>
    public class ContentRulesTests
    {
        private static List<string> Offenders(IEnumerable<string> texts)
        {
            var found = new List<string>();
            foreach (string text in texts)
                if (ContentRules.FindForbidden(text) != null) found.Add(text);
            return found;
        }

        [Test]
        public void ThePlantedWords_AreCaught_ButArtFileNamesAreNotContent()
        {
            CollectionAssert.AreEqual(new[] { "cheese", "Fried eggs" }, Offenders(new[] { "cheese", "Fried eggs", "eggplant", "potato" }));
            // The egg model stands in for the potato; model paths are never passed to the scan.
            Assert.IsNull(ContentRules.FindForbidden("potato"));
        }

        [Test]
        public void TheCrateIngredients_AreVegan()
        {
            var texts = new List<string>();
            foreach (BaseItemDef def in DemoIngredients.All) { texts.Add(def.Id); texts.Add(def.DisplayName); }
            CollectionAssert.IsEmpty(Offenders(texts));
        }

        [Test]
        public void TheMenu_IsVegan()
        {
            var texts = new List<string>();
            foreach (MenuDish dish in Menu.All)
            {
                texts.Add(dish.Name);
                texts.Add(dish.Main);
                foreach (DishNeed need in dish.Needs) texts.Add(need.BaseId);
            }
            CollectionAssert.IsEmpty(Offenders(texts));
        }

        [Test]
        public void TheGrocerList_IsVegan_AndHasEveryCrateItem()
        {
            EconomyCatalog economy = EconomyCatalog.CreateDefault();
            var texts = new List<string>();
            foreach (GrocerPrice g in economy.Grocer) texts.Add(g.BaseId);
            CollectionAssert.IsEmpty(Offenders(texts));
            foreach (BaseItemDef def in DemoIngredients.All)
                Assert.IsTrue(economy.TryGetGrocerPrice(def.Id, out _), def.Id + " has no grocer price");
        }

        [Test]
        public void WhatsNew_IsVegan()
        {
            var asset = Resources.Load<TextAsset>("WhatsNew");
            Assert.IsNotNull(asset);
            var texts = new List<string>();
            foreach (WhatsNewEntry entry in WhatsNewRules.Parse(asset.text))
            {
                texts.Add(entry.title);
                texts.AddRange(entry.items);
                texts.AddRange(entry.test);
            }
            CollectionAssert.IsEmpty(Offenders(texts));
        }

        [Test]
        public void WhatsNew_HasTheSpec028Entry()
        {
            var asset = Resources.Load<TextAsset>("WhatsNew");
            bool found = false;
            foreach (WhatsNewEntry entry in WhatsNewRules.Parse(asset.text)) found |= entry.id.EndsWith("spec-028");
            Assert.IsTrue(found);
        }
    }
}
