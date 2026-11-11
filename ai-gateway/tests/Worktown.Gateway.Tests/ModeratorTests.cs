using System.Text.Json;
using Worktown.Gateway.Moderation;
using Xunit;

namespace Worktown.Gateway.Tests;

public class ModeratorTests
{
    private static JsonElement Payload(string name, string description) =>
        JsonSerializer.SerializeToElement(new { name, description });

    [Fact]
    public async Task CleanPayloadIsAllowed()
    {
        var moderator = new PlaceholderDenyListModerator(new[] { "forbiddenword" });
        var result = await moderator.CheckAsync("item_name", Payload("Golden Spud Sticks", "Crisp sticks."), CancellationToken.None);
        Assert.True(result.Allowed);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("Forbiddenword Fries", "Crisp sticks.")]
    [InlineData("Golden Fries", "Truly FORBIDDENWORD good.")]
    [InlineData("Golden Fries", "Crisp, forbiddenword-level crunch.")]
    public async Task DenyListedWholeWordIsRejectedInAnyField(string name, string description)
    {
        var moderator = new PlaceholderDenyListModerator(new[] { "forbiddenword" });
        var result = await moderator.CheckAsync("item_name", Payload(name, description), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal(PlaceholderDenyListModerator.ReasonDenyListed, result.ReasonCode);
    }

    [Fact]
    public async Task SubstringIsNotAWholeWord()
    {
        var moderator = new PlaceholderDenyListModerator(new[] { "forbiddenword" });
        var result = await moderator.CheckAsync("item_name", Payload("Forbiddenwords Fries", "Crisp sticks."), CancellationToken.None);
        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task DefaultListRejectsObviousProfanity()
    {
        var moderator = new PlaceholderDenyListModerator();
        var rejected = await moderator.CheckAsync("item_name", Payload("Shit Fries", "Crisp sticks."), CancellationToken.None);
        var allowed = await moderator.CheckAsync("item_name", Payload("Golden Spud Sticks", "Crisp sticks."), CancellationToken.None);
        Assert.False(rejected.Allowed);
        Assert.True(allowed.Allowed);
    }
}
