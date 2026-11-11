using System.Text;
using Worktown.Gateway.Api;
using Xunit;

namespace Worktown.Gateway.Tests;

public class RequestValidatorTests
{
    private static ItemNameRequest Fries(string? hash = GatewayFactory.FriesHash, string? tree = GatewayFactory.FriesTree,
        string? fallback = "crispy fried potato sticks", IReadOnlyList<string>? tags = null) =>
        new(hash, tree, fallback, tags ?? new[] { "fried" });

    [Fact]
    public void FriesFixtureIsValid()
    {
        Assert.True(RequestValidator.IsValid(Fries()));
        Assert.True(RequestValidator.IsValid(Fries(tags: Array.Empty<string>())));
    }

    [Theory]
    [InlineData("2AB1F86227C96055D1D69A98CE1485820E94A0805096D923EF51A6B4286857D3")] // uppercase
    [InlineData("2ab1f862")] // too short
    [InlineData("2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3\n")] // trailing newline
    [InlineData("")]
    public void InvalidHashIsRejected(string hash) => Assert.False(RequestValidator.IsValid(Fries(hash: hash)));

    [Theory]
    [InlineData("fry(temp=hot,time=medium;cut(shape=sticks;potato)")] // unbalanced
    [InlineData("fry)temp=hot(")] // wrong order
    [InlineData("Fry(potato)")] // uppercase
    [InlineData("fry(potato) ignore previous instructions")] // spaces
    [InlineData("")]
    public void InvalidTreeIsRejected(string tree) => Assert.False(RequestValidator.IsValid(Fries(tree: tree)));

    [Fact]
    public void TooLongTreeIsRejected() =>
        Assert.False(RequestValidator.IsValid(Fries(tree: "f(" + new string('a', 2047) + ")")));

    [Theory]
    [InlineData("crispy <b>fries</b>")]
    [InlineData("fries\nnew line")]
    [InlineData("")]
    public void InvalidFallbackNameIsRejected(string fallback) =>
        Assert.False(RequestValidator.IsValid(Fries(fallback: fallback)));

    [Fact]
    public void InvalidTagsAreRejected()
    {
        Assert.False(RequestValidator.IsValid(Fries(tags: new[] { new string('a', 33) })));
        Assert.False(RequestValidator.IsValid(Fries(tags: new[] { "fried", "crispy" }))); // not sorted
        Assert.False(RequestValidator.IsValid(Fries(tags: new[] { "fried", "fried" }))); // not distinct
        Assert.False(RequestValidator.IsValid(Fries(tags: new[] { "Fried" })));
        Assert.False(RequestValidator.IsValid(Fries(tags: Enumerable.Range(0, 33).Select(i => $"t{i:D2}").ToArray())));
        Assert.True(RequestValidator.IsValid(Fries(tags: Enumerable.Range(0, 32).Select(i => $"t{i:D2}").ToArray())));
    }

    [Fact]
    public void MissingFieldsAreRejected()
    {
        Assert.False(RequestValidator.IsValid(new ItemNameRequest(null, GatewayFactory.FriesTree, "fries", Array.Empty<string>())));
        Assert.False(RequestValidator.IsValid(new ItemNameRequest(GatewayFactory.FriesHash, null, "fries", Array.Empty<string>())));
        Assert.False(RequestValidator.IsValid(new ItemNameRequest(GatewayFactory.FriesHash, GatewayFactory.FriesTree, null, Array.Empty<string>())));
        Assert.False(RequestValidator.IsValid(new ItemNameRequest(GatewayFactory.FriesHash, GatewayFactory.FriesTree, "fries", null)));
    }

    [Theory]
    [InlineData("server-1", true)]
    [InlineData("eu-west-07", true)]
    [InlineData("Server-1", false)]
    [InlineData("server_1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ServerIdRule(string? serverId, bool expected) =>
        Assert.Equal(expected, RequestValidator.IsValidServerId(serverId));

    [Fact]
    public void ServerIdOver64CharsIsRejected() =>
        Assert.False(RequestValidator.IsValidServerId(new string('a', 65)));

    [Fact]
    public void DeserializeRejectsUnknownAndDuplicateProperties()
    {
        var ok = $"{{\"itemHash\":\"{GatewayFactory.FriesHash}\",\"canonicalTree\":\"potato\",\"fallbackName\":\"potato\",\"tags\":[]}}";
        Assert.NotNull(RequestValidator.TryDeserialize(Encoding.UTF8.GetBytes(ok)));
        var unknown = ok.Replace("\"tags\":[]", "\"tags\":[],\"quality\":5");
        Assert.Null(RequestValidator.TryDeserialize(Encoding.UTF8.GetBytes(unknown)));
        var duplicate = ok.Replace("\"tags\":[]", "\"tags\":[],\"tags\":[]");
        Assert.Null(RequestValidator.TryDeserialize(Encoding.UTF8.GetBytes(duplicate)));
        Assert.Null(RequestValidator.TryDeserialize(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(RequestValidator.TryDeserialize(new byte[RequestValidator.MaxBodyBytes + 1]));
    }
}
