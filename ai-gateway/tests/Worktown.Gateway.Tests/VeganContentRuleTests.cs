using System.Text.Json;
using Worktown.Gateway.Moderation;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Gateway.Tests;

public class VeganContentRuleTests
{
    private static JsonElement Payload(string name, string description) =>
        JsonSerializer.SerializeToElement(new { name, description });

    public static TheoryData<string> Words()
    {
        var data = new TheoryData<string>();
        foreach (var word in VeganContentRule.ForbiddenWords) data.Add(word);
        return data;
    }

    [Fact]
    public void ButterIsRejected()
    {
        var result = VeganContentRule.Check(Payload("Golden Spud Sticks", "Crisp sticks with creamy butter."));
        Assert.False(result.Allowed);
        Assert.Equal("vegan_rule", result.ReasonCode);
    }

    [Fact]
    public void EggplantPasses()
    {
        var result = VeganContentRule.Check(Payload("Smoky Eggplant Stack", "Grilled eggplant with herbs."));
        Assert.True(result.Allowed);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [MemberData(nameof(Words))]
    public void EveryListedWordIsCaughtInEitherFieldAndAsPlural(string word)
    {
        var title = char.ToUpperInvariant(word[0]) + word[1..];
        Assert.False(VeganContentRule.Check(Payload(title + " Fries", "Crisp sticks of fried potato.")).Allowed);
        Assert.False(VeganContentRule.Check(Payload("Golden Fries", "Fried with " + word.ToUpperInvariant() + ".")).Allowed);
        Assert.False(VeganContentRule.Check(Payload("Golden Fries", "Made with " + word + "s.")).Allowed);
        Assert.False(VeganContentRule.Check(Payload("Golden Fries", "Made with " + word + "es.")).Allowed);
        Assert.False(VeganContentRule.Check(Payload("Golden Fries", "Totally " + word + "-free.")).Allowed);
        Assert.Equal(word, VeganContentRule.FindForbidden(word));
    }

    [Theory]
    [InlineData("Creamy Golden Fries")]
    [InlineData("Hamburger Style Stack")]
    [InlineData("Fishy Potato Sticks")]
    public void LongerWordsThatOnlyStartWithAListedWordPass(string name)
    {
        Assert.True(VeganContentRule.Check(Payload(name, "Crisp sticks of fried potato.")).Allowed);
    }

    /// <summary>Drift test: the gateway's copy must equal /sim's list (spec 028, D27).</summary>
    [Fact]
    public void WordListEqualsSimContentRules()
    {
        Assert.Equal(ContentRules.ForbiddenWords, VeganContentRule.ForbiddenWords);
    }

    [Theory]
    [InlineData("Crisp sticks with creamy butter.")]
    [InlineData("Eggplant and egg-free batter.")]
    [InlineData("Smoky eggplant stack.")]
    [InlineData("Fishes, hams and honeys.")]
    [InlineData("Creamy Hamburger")]
    [InlineData("vegan_cheese")]
    [InlineData("")]
    public void MatchingAgreesWithSimContentRules(string text)
    {
        Assert.Equal(ContentRules.FindForbidden(text), VeganContentRule.FindForbidden(text));
    }
}
