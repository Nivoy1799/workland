using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 028 AC3 (D27): no animal-product word in the sim's content, and the check itself works.</summary>
    public class ContentRulesTests
    {
        [Fact]
        public void TheForbiddenList_HasTheSpecWords()
        {
            foreach (string word in new[] { "cheese", "milk", "butter", "egg", "meat", "beef", "pork", "chicken", "fish", "bacon", "ham", "gelatin", "honey", "cream", "yoghurt" })
                Assert.Contains(word, ContentRules.ForbiddenWords);
        }

        [Theory]
        [InlineData("cheese", "cheese")]
        [InlineData("Cheese", "cheese")]
        [InlineData("fried eggs", "egg")]
        [InlineData("vegan_cheese", "cheese")]
        [InlineData("egg-free sauce", "egg")]
        [InlineData("Chicken soup", "chicken")]
        [InlineData("ice cream", "cream")]
        [InlineData("fishes", "fish")]
        [InlineData("ham", "ham")]
        public void PlantedWords_AreFound(string text, string word)
        {
            Assert.Equal(word, ContentRules.FindForbidden(text));
        }

        [Theory]
        [InlineData("eggplant")]
        [InlineData("hamburger")]
        [InlineData("buttercup squash")]
        [InlineData("chamomile")]
        [InlineData("potato")]
        [InlineData("")]
        [InlineData(null)]
        public void WordsInsideOtherWords_AreFine(string? text)
        {
            Assert.Null(ContentRules.FindForbidden(text));
        }

        [Fact]
        public void TheCheck_FailsOnAPlantedWord_InAnyCatalog()
        {
            // Meta-test: the same scan the real-content test uses must report a planted item and grocer entry.
            var planted = new BaseItemDef("cheese", "cheese", StreetFoodItems.Lemon.Stats, StreetFoodItems.Lemon.IdealDoneness, new string[0]);
            Assert.NotEmpty(Scan(new[] { planted.Id, planted.DisplayName }));
            Assert.NotEmpty(Scan(new[] { new GrocerPrice("fish", 5).BaseId }));
        }

        [Fact]
        public void TheRealContent_HasNoAnimalProductWord()
        {
            var texts = new List<string>();
            foreach (BaseItemDef def in StreetFoodItems.All) { texts.Add(def.Id); texts.Add(def.DisplayName); foreach (string tag in def.Tags) texts.Add(tag); }
            EconomyCatalog economy = EconomyCatalog.CreateDefault();
            foreach (GrocerPrice g in economy.Grocer) texts.Add(g.BaseId);
            foreach (MarketDef m in economy.Markets) texts.Add(m.Id);
            Assert.Empty(Scan(texts));
        }

        private static List<string> Scan(IEnumerable<string> texts)
            => texts.Where(t => ContentRules.FindForbidden(t) != null).ToList();
    }
}
