using System.Net;
using System.Text.Json;
using Worktown.Gateway.Auth;
using Worktown.Gateway.Config;
using Xunit;

namespace Worktown.Gateway.Tests;

public class AuthRateLimitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token-wrong-token-wrong-token-00")]
    [InlineData("Basic " + GatewayFactory.ServiceToken)]
    [InlineData("Bearer " + GatewayFactory.ServiceToken + "x")]
    public async Task AC7_MissingOrWrongTokenIs401WithoutUpstreamCall(string? authorization)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Worktown-Server-Id", GatewayFactory.ServerId);
        if (authorization is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);

        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("unauthorized", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, factory.Stub.CallCount);
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("Bearer")]
    public async Task BearerSchemeIsCaseInsensitive(string scheme)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Worktown-Server-Id", GatewayFactory.ServerId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", scheme + " " + GatewayFactory.ServiceToken);
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenItselfIsCaseSensitive()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Worktown-Server-Id", GatewayFactory.ServerId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + GatewayFactory.ServiceToken.ToUpperInvariant());
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC7_HealthzNeedsNoTokenAndNoUpstream()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal(0, factory.Stub.CallCount);
    }

    private static GatewayFactory TightLimit() => new(new Dictionary<string, string>
    {
        [GatewayOptions.RateLimitBurstKey] = "2",
        [GatewayOptions.RateLimitPerMinuteKey] = "1",
    });

    [Fact]
    public async Task AC8_ThirdRequestFromSameServerIs429AndOtherServerStillServed()
    {
        using var factory = TightLimit();
        using var serverA = factory.CreateAuthedClient("server-a");
        using var serverB = factory.CreateAuthedClient("server-b");

        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(serverA, GatewayFactory.FriesBody())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(serverA, GatewayFactory.FriesBody())).StatusCode);
        var third = await GatewayFactory.PostItemNameAsync(serverA, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter?.Delta?.TotalSeconds >= 1);
        var json = JsonDocument.Parse(await third.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("rate_limited", json.GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(serverB, GatewayFactory.FriesBody())).StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedTrafficDoesNotUseUpAServersQuota()
    {
        using var factory = TightLimit();
        using var attacker = factory.CreateClient();
        attacker.DefaultRequestHeaders.Add("X-Worktown-Server-Id", "server-a");
        attacker.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer not-the-token");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await GatewayFactory.PostItemNameAsync(attacker, GatewayFactory.FriesBody())).StatusCode);

        using var serverA = factory.CreateAuthedClient("server-a");
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(serverA, GatewayFactory.FriesBody())).StatusCode);
    }

    [Fact]
    public void TokenCompareMatchesOnlyTheExactToken()
    {
        Assert.True(ServiceTokenFilter.TokensMatch(GatewayFactory.ServiceToken, GatewayFactory.ServiceToken));
        Assert.False(ServiceTokenFilter.TokensMatch(GatewayFactory.ServiceToken + " ", GatewayFactory.ServiceToken));
        Assert.False(ServiceTokenFilter.TokensMatch("", GatewayFactory.ServiceToken));
        Assert.False(ServiceTokenFilter.TokensMatch("", ""));
    }

    [Fact]
    public void AC12_StartupWithoutServiceTokenFailsNamingTheVariable()
    {
        using var factory = new GatewayFactory(new Dictionary<string, string> { [GatewayOptions.ServiceTokenKey] = "" });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var text = error.ToString();
        Assert.Contains(GatewayOptions.ServiceTokenKey, text);
        Assert.DoesNotContain(GatewayFactory.ApiKey, text);
    }

    [Fact]
    public void ShortServiceTokenFailsWithoutEchoingIt()
    {
        const string shortToken = "short-token-value-1234";
        using var factory = new GatewayFactory(new Dictionary<string, string> { [GatewayOptions.ServiceTokenKey] = shortToken });
        var text = Assert.ThrowsAny<Exception>(() => factory.CreateClient()).ToString();
        Assert.Contains(GatewayOptions.ServiceTokenKey, text);
        Assert.DoesNotContain(shortToken, text);
    }

    [Theory]
    [InlineData(GatewayOptions.UpstreamBaseUrlKey, "http://upstream.test/v1")]
    [InlineData(GatewayOptions.UpstreamBaseUrlKey, "")]
    [InlineData(GatewayOptions.UpstreamApiKeyKey, "")]
    [InlineData(GatewayOptions.ModelKey, "")]
    [InlineData(GatewayOptions.UpstreamFlavorKey, "anthropic")]
    [InlineData(GatewayOptions.TemperatureKey, "2.5")]
    [InlineData(GatewayOptions.UpstreamTimeoutSecondsKey, "0")]
    [InlineData(GatewayOptions.RateLimitBurstKey, "many")]
    public void InvalidConfigFailsStartupNamingTheVariable(string key, string value)
    {
        using var factory = new GatewayFactory(new Dictionary<string, string> { [key] = value });
        var text = Assert.ThrowsAny<Exception>(() => factory.CreateClient()).ToString();
        Assert.Contains(key, text);
        Assert.DoesNotContain(GatewayFactory.ApiKey, text);
        Assert.DoesNotContain(GatewayFactory.ServiceToken, text);
    }
}
