using Worktown.Gateway.Api;
using Worktown.Gateway.Prompts;
using Xunit;

namespace Worktown.Gateway.Tests;

public class ItemNamePromptTests
{
    private static ItemNameRequest Request(string tree = GatewayFactory.FriesTree, string fallback = "crispy fried potato sticks",
        params string[] tags) => new(GatewayFactory.FriesHash, tree, fallback, tags.Length == 0 ? new[] { "fried" } : tags);

    [Fact]
    public void BuildPutsTheValidatedFieldsInTheUserMessage()
    {
        var messages = ItemNamePrompt.Build(Request());
        Assert.Equal(2, messages.Count);
        Assert.Equal(("system", ItemNamePrompt.SystemText), messages[0]);
        Assert.Equal($"Process: {GatewayFactory.FriesTree}\nPlain name: crispy fried potato sticks\nTags: fried", messages[1].Content);
    }

    [Fact]
    public void InputHashIsStableLowercaseHex()
    {
        var a = ItemNamePrompt.InputHash(Request());
        Assert.Equal(a, ItemNamePrompt.InputHash(Request()));
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void InputHashChangesWithEveryPromptInput()
    {
        var baseline = ItemNamePrompt.InputHash(Request());
        Assert.NotEqual(baseline, ItemNamePrompt.InputHash(Request(tree: "fry(temp=hot;potato)")));
        Assert.NotEqual(baseline, ItemNamePrompt.InputHash(Request(fallback: "golden fried potato sticks")));
        Assert.NotEqual(baseline, ItemNamePrompt.InputHash(Request(GatewayFactory.FriesTree, "crispy fried potato sticks", "fried", "salty")));
        Assert.NotEqual(baseline, ItemNamePrompt.InputHash(new ItemNameRequest(GatewayFactory.FriesHash, GatewayFactory.FriesTree,
            "crispy fried potato sticks", Array.Empty<string>())));
    }
}
