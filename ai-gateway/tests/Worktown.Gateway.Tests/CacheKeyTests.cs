using Worktown.Gateway.Cache;
using Xunit;

namespace Worktown.Gateway.Tests;

public class CacheKeyTests
{
    private static readonly string HashA = new('a', 64);

    [Fact]
    public void AC4_PinnedVector()
    {
        // printf '%s' 'gw1|item_name|1|1|qwen/test-model|aaaa...a' (64 x a) | sha256sum
        Assert.Equal("5919e2cb5ae647783127cc11ceb28c29bade3273fee8df53ef4d7f1889208fb8",
            CacheKey.Compute("item_name", 1, 1, "qwen/test-model", HashA));
    }

    [Theory]
    [InlineData("npc_line", 1, 1, "qwen/test-model", null)]
    [InlineData("item_name", 2, 1, "qwen/test-model", null)]
    [InlineData("item_name", 1, 2, "qwen/test-model", null)]
    [InlineData("item_name", 1, 1, "qwen/other-model", null)]
    [InlineData("item_name", 1, 1, "qwen/test-model", "b")]
    public void AC4_ChangingAnyInputChangesTheKey(string contentType, int schema, int prompt, string model, string? hashChar)
    {
        var hash = hashChar is null ? HashA : new string(hashChar[0], 64);
        Assert.NotEqual(CacheKey.Compute("item_name", 1, 1, "qwen/test-model", HashA),
            CacheKey.Compute(contentType, schema, prompt, model, hash));
    }

    [Fact]
    public void KeyIsLowercaseHexAndStable()
    {
        var key = CacheKey.Compute("item_name", 1, 1, "qwen/test-model", HashA);
        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.Equal(key, CacheKey.Compute("item_name", 1, 1, "qwen/test-model", HashA));
    }
}
