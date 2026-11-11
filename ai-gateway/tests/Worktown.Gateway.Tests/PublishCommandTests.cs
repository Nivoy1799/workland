using System.Text.Json;
using Worktown.NameBake;
using Xunit;

namespace Worktown.Gateway.Tests;

/// <summary>Spec 056b AC7: publish writes the approved, reachable names, checked again, byte-identically.</summary>
public class PublishCommandTests
{
    [Fact]
    public async Task PublishWritesOnlyOkAndReachableRows()
    {
        using var f = new OfflineFixture(items: 4);
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok");
        f.Decide("boiled potato one", "no");
        // "boiled potato two" stays pending; "boiled potato three" is approved but then no longer reachable.
        f.Decide("boiled potato three", "ok");
        var reachable = ReachableFile.Load(f.ReachablePath).Items.Where(i => i.FallbackName != "boiled potato three")
            .Select(i => new Worktown.Sim.Cooking.ReachableItem(i.Hash, i.CanonicalTree, i.FallbackName, i.Tags, i.Routes, i.Target, i.LabelWords))
            .ToList();
        f.WriteReachable(reachable);

        var result = await f.PublishAsync();
        Assert.Equal(PublishCommand.ExitOk, result.ExitCode);
        Assert.Equal(1, result.Published);
        Assert.Equal(1, result.Pending);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.Stale);
        Assert.Contains("published=1 pending=1 rejected=1 stale=1", f.Out.ToString());

        var text = File.ReadAllText(f.BookPath);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("worktown-item-names", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("v1", root.GetProperty("hashVersion").GetString());
        Assert.Equal(ReachableFile.Load(f.ReachablePath).SetHash, root.GetProperty("reachableSet").GetString());
        Assert.Equal("item_name.s1.p1", root.GetProperty("contentVersion").GetString());
        Assert.Equal(GatewayFactory.Model, root.GetProperty("model").GetString());
        Assert.Equal("PlaceholderDenyListModerator", root.GetProperty("moderator").GetString());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(GatewayFactory.FriesHash, item.GetProperty("hash").GetString());
        Assert.Equal("Golden Spud Sticks", item.GetProperty("name").GetString());
        Assert.Equal("Crisp sticks of fried potato.", item.GetProperty("description").GetString());
        Assert.DoesNotContain("\r", text);
        Assert.False(File.Exists(f.BookPath + ".tmp"));
    }

    [Fact]
    public async Task TwoPublishesGiveByteIdenticalFiles()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok");
        f.Decide("boiled potato one", "ok");
        Assert.Equal(PublishCommand.ExitOk, (await f.PublishAsync()).ExitCode);
        var first = File.ReadAllBytes(f.BookPath);
        Assert.Equal(PublishCommand.ExitOk, (await f.PublishAsync()).ExitCode);
        Assert.Equal(first, File.ReadAllBytes(f.BookPath));
        using var doc = JsonDocument.Parse(first);
        var hashes = doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("hash").GetString()).ToList();
        Assert.Equal(hashes.OrderBy(h => h, StringComparer.Ordinal).ToList(), hashes);
    }

    [Fact]
    public async Task AHandEditThatFailsTheVeganRuleStopsWithExit2AndNoFileChange()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok");
        Assert.Equal(PublishCommand.ExitOk, (await f.PublishAsync()).ExitCode);
        var before = File.ReadAllBytes(f.BookPath);

        f.Decide(OfflineFixture.FriesName, "ok", name: "Cheesy Spud Sticks", description: "Crisp sticks with cheese.");
        var result = await f.PublishAsync();
        Assert.Equal(PublishCommand.ExitCheckFailed, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(f.BookPath));
        Assert.Contains("vegan_rule", f.Err.ToString());
        Assert.Contains("nothing written", f.Err.ToString());
    }

    [Theory]
    [InlineData("Spud 4 Sticks", "Crisp sticks of fried potato.")] // a digit in the name: not the schema
    [InlineData("Golden Spud Sticks", "no capital and no full stop")]
    [InlineData("Forbiddenword Sticks", "Crisp sticks of fried potato.")] // the registered moderator says no
    public async Task AHandEditThatFailsASchemaOrModerationCheckStopsWithExit2(string name, string description)
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok", name, description);
        var result = await f.PublishAsync();
        Assert.Equal(PublishCommand.ExitCheckFailed, result.ExitCode);
        Assert.False(File.Exists(f.BookPath));
    }

    [Fact]
    public async Task AnUnknownDecisionStopsWithExit2()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "maybe");
        Assert.Equal(PublishCommand.ExitCheckFailed, (await f.PublishAsync()).ExitCode);
        Assert.False(File.Exists(f.BookPath));
    }

    [Fact]
    public async Task PublishNeedsTheModelButNoKey()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok");
        var noModel = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        Assert.Equal(PublishCommand.ExitBadSetup, (await f.PublishAsync(noModel)).ExitCode);
        Assert.Equal(PublishCommand.ExitOk, (await f.PublishAsync(OfflineFixture.Config(upstream: false))).ExitCode);
        Assert.DoesNotContain(GatewayFactory.ApiKey, f.Out + "\n" + f.Err + "\n" + f.Logs.AllText);
    }

    [Fact]
    public async Task NoApprovedRowsWriteAnEmptyBook()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        var result = await f.PublishAsync();
        Assert.Equal(PublishCommand.ExitOk, result.ExitCode);
        Assert.Equal(3, result.Pending);
        Assert.EndsWith("\"items\": []\n}\n", File.ReadAllText(f.BookPath));
    }
}
