using System.Text.Json.Nodes;
using Worktown.Gateway.Upstream;
using Worktown.Gateway.Validation;
using Xunit;

namespace Worktown.Gateway.Tests;

public class UpstreamSchemaTests
{
    [Fact]
    public void StripsUnsupportedKeywordsAndKeepsStructure()
    {
        var full = SchemaValidator.ForItemNameV1().Schema;
        var upstream = UpstreamSchema.ForUpstream(full).AsObject();
        var text = upstream.ToJsonString();
        Assert.DoesNotContain("pattern", text);
        Assert.DoesNotContain("minLength", text);
        Assert.DoesNotContain("maxLength", text);
        Assert.DoesNotContain("$schema", text);
        Assert.DoesNotContain("$id", text);
        Assert.Equal("object", upstream["type"]!.GetValue<string>());
        Assert.False(upstream["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(2, upstream["required"]!.AsArray().Count);
        Assert.Equal("string", upstream["properties"]!["name"]!["type"]!.GetValue<string>());
        Assert.Equal("string", upstream["properties"]!["description"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void DoesNotChangeTheInput()
    {
        var full = SchemaValidator.ForItemNameV1().Schema;
        var before = full.ToJsonString();
        UpstreamSchema.ForUpstream(full);
        Assert.Equal(before, full.ToJsonString());
        Assert.Contains("pattern", before);
    }

    [Fact]
    public void PropertyNamesThatLookLikeKeywordsAreKept()
    {
        var schema = JsonNode.Parse("""
            {"type":"object","properties":{"pattern":{"type":"string","pattern":"^a$"},
             "maxLength":{"type":"integer","const":{"pattern":"data"}}}}
            """)!;
        var upstream = UpstreamSchema.ForUpstream(schema);
        Assert.NotNull(upstream["properties"]!["pattern"]);
        Assert.Null(upstream["properties"]!["pattern"]!["pattern"]);
        Assert.NotNull(upstream["properties"]!["maxLength"]);
        Assert.Equal("data", upstream["properties"]!["maxLength"]!["const"]!["pattern"]!.GetValue<string>());
    }
}
