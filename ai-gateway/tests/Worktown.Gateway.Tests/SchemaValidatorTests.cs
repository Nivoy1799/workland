using Worktown.Gateway.Validation;
using Xunit;

namespace Worktown.Gateway.Tests;

public class SchemaValidatorTests
{
    private static readonly SchemaValidator Validator = SchemaValidator.ForItemNameV1();

    public static TheoryData<string> InvalidOutputs => new()
    {
        "{\"name\":\"Golden Spud Sticks\"}", // missing field
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"Crisp sticks of fried potato.\",\"price\":3}", // extra field
        "{\"name\":\"A" + new string('a', 40) + "\",\"description\":\"Crisp sticks of fried potato.\"}", // name 41 chars
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"See http://example.test for more.\"}", // URL
        "```json\n" + StubUpstreamHandler.ValidContent + "\n```", // fenced JSON
        StubUpstreamHandler.ValidContent + "\n{}", // trailing second document
        "{\"name\":\"Golden Spud Sticks\",\"name\":\"Other\",\"description\":\"Crisp sticks of fried potato.\"}", // duplicate key
        "{\"name\":\"Go\",\"description\":\"Crisp sticks of fried potato.\"}", // name too short
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"crisp sticks of fried potato.\"}", // lowercase start
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"Crisp sticks of fried potato\"}", // no end punctuation
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"Crisp sticks of fried potato. \\ud83c\\udf5f\"}", // emoji
        "{\"name\":\"Golden  Spud\",\"description\":\"Crisp sticks of fried potato.\"}", // double space
        "{\"name\":\"Golden Spud Sticks\",\"description\":\"Email chef@example.test now.\"}", // @
        "[]",
        "",
    };

    [Fact]
    public void ValidOutputPasses()
    {
        Assert.True(Validator.TryValidate(StubUpstreamHandler.ValidContent, out var payload));
        Assert.Equal("Golden Spud Sticks", payload.GetProperty("name").GetString());
    }

    [Theory]
    [MemberData(nameof(InvalidOutputs))]
    public void AC5_InvalidOutputIsRejected(string content) =>
        Assert.False(Validator.TryValidate(content, out _));

    [Fact]
    public void NullIsRejected() => Assert.False(Validator.TryValidate(null, out _));

    [Fact]
    public void BoundaryLengthsPass()
    {
        var name40 = "A" + new string('a', 39);
        Assert.True(Validator.TryValidate($"{{\"name\":\"{name40}\",\"description\":\"Crisp chip.\"}}", out _));
        Assert.True(Validator.TryValidate("{\"name\":\"Mom's Pan-Fried Spud\",\"description\":\"Crisp, golden and warm!\"}", out _));
    }

    [Fact]
    public void EmbeddedSchemaIsTheRepoFile()
    {
        Assert.Equal("item_name", Validator.ContentType);
        Assert.Equal(1, Validator.SchemaVersion);
        Assert.False(Validator.Schema["additionalProperties"]!.GetValue<bool>());
    }
}
