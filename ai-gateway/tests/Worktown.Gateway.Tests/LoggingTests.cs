using System.Net;
using Worktown.Gateway.Prompts;
using Xunit;

namespace Worktown.Gateway.Tests;

public class LoggingTests
{
    [Fact]
    public async Task AC9_LogsHoldNoSecretsPromptsOrUpstreamOutput()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody())).StatusCode);

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Worktown-Server-Id", GatewayFactory.ServerId);
        anonymous.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await GatewayFactory.PostItemNameAsync(anonymous, GatewayFactory.FriesBody())).StatusCode);

        var logs = factory.Logs.AllText;
        Assert.NotEmpty(factory.Logs.Entries);
        Assert.Contains("cache=miss", logs);
        Assert.Contains("cache=hit", logs);
        Assert.Contains(GatewayFactory.FriesHash, logs);

        Assert.DoesNotContain(GatewayFactory.ApiKey, logs);
        Assert.DoesNotContain(GatewayFactory.ServiceToken, logs);
        Assert.DoesNotContain(ItemNamePrompt.SystemText, logs);
        Assert.DoesNotContain("cozy, low-poly town", logs);
        Assert.DoesNotContain("Plain name:", logs);
        Assert.DoesNotContain("Golden Spud Sticks", logs);
        Assert.DoesNotContain("Crisp sticks of fried potato", logs);
    }

    [Fact]
    public async Task LogLineCarriesIdsCodesAndTimings()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        var line = Assert.Single(factory.Logs.Entries, e => e.Contains("item_name server="));
        Assert.Contains("server=server-1", line);
        Assert.Contains("attempts=1", line);
        Assert.Contains("outcome=ok", line);
        Assert.Contains("latency_ms=", line);
        Assert.Matches("key=[0-9a-f]{12} ", line);
    }
}
