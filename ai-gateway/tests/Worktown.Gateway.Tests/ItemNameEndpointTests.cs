using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Worktown.Gateway.Config;
using Xunit;

namespace Worktown.Gateway.Tests;

public class ItemNameEndpointTests
{
    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var json = await JsonOf(response);
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("error").GetProperty("message").GetString()));
    }

    [Fact]
    public async Task AC3_MissThenHit()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();

        var first = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var a = await JsonOf(first);
        Assert.False(a.GetProperty("cacheHit").GetBoolean());
        Assert.Equal(1, factory.Stub.CallCount);

        var second = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var b = await JsonOf(second);
        Assert.True(b.GetProperty("cacheHit").GetBoolean());
        Assert.Equal(1, factory.Stub.CallCount);

        Assert.Equal("Golden Spud Sticks", a.GetProperty("name").GetString());
        Assert.Equal(a.GetProperty("name").GetString(), b.GetProperty("name").GetString());
        Assert.Equal(a.GetProperty("description").GetString(), b.GetProperty("description").GetString());
        Assert.Equal(GatewayFactory.FriesHash, b.GetProperty("itemHash").GetString());
        Assert.Equal("item_name.s1.p1", b.GetProperty("contentVersion").GetString());
        Assert.Equal(GatewayFactory.Model, b.GetProperty("model").GetString());
        Assert.Equal(new Uri("https://upstream.test/v1/chat/completions"), Assert.Single(factory.Stub.RequestUris));
    }

    [Theory]
    [MemberData(nameof(SchemaValidatorTests.InvalidOutputs), MemberType = typeof(SchemaValidatorTests))]
    public async Task AC5_InvalidOutputFailsAfterTwoAttemptsAndIsNotCached(string content)
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => StubUpstreamHandler.Completion(content);
        using var client = factory.CreateAuthedClient();

        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_invalid_output");
        Assert.Equal(2, factory.Stub.CallCount);
        // The error body never echoes upstream output.
        if (content.Length > 2) Assert.DoesNotContain(content, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(4, factory.Stub.CallCount);
    }

    [Fact]
    public async Task InvalidThenValidOutputSucceedsOnSecondAttempt()
    {
        using var factory = new GatewayFactory();
        factory.Stub.EnqueueContent("{\"name\":\"Golden Spud Sticks\"}");
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task FinishReasonOtherThanStopIsInvalid()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => StubUpstreamHandler.Completion(StubUpstreamHandler.ValidContent, "length");
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_invalid_output");
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task AC6_ModerationRejectionIsFinalAndNotCached()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => StubUpstreamHandler.Completion(
            "{\"name\":\"Forbiddenword Fries\",\"description\":\"Crisp sticks of fried potato.\"}");
        using var client = factory.CreateAuthedClient();

        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.UnprocessableEntity, "content_rejected");
        Assert.Equal(1, factory.Stub.CallCount);

        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task D27_AnimalProductInOutputIsRejectedAsVeganRuleAndNotCached()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => StubUpstreamHandler.Completion(
            "{\"name\":\"Golden Spud Sticks\",\"description\":\"Crisp sticks with creamy butter.\"}");
        using var client = factory.CreateAuthedClient();

        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.UnprocessableEntity, "content_rejected");
        Assert.DoesNotContain("butter", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.Stub.CallCount);
        var line = Assert.Single(factory.Logs.Entries, e => e.Contains("item_name server="));
        Assert.Contains("reason=vegan_rule", line);

        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task D27_EggplantIsNotAVeganRuleHit()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => StubUpstreamHandler.Completion(
            "{\"name\":\"Smoky Eggplant Stack\",\"description\":\"Grilled eggplant with herbs.\"}");
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpstreamRedirectIsAnErrorAndNotRetried()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://upstream.test/v1/elsewhere");
            return redirect;
        };
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_error");
        Assert.Equal(1, factory.Stub.CallCount);
    }

    private static HttpResponseMessage PaddedCompletion(int totalBytes)
    {
        var json = $"{{\"id\":\"cmpl-test\",\"choices\":[{{\"index\":0,\"finish_reason\":\"stop\",\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(StubUpstreamHandler.ValidContent)}}}}}]}}";
        // Trailing whitespace keeps the envelope valid JSON, so only the size decides the outcome.
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json + new string(' ', totalBytes - json.Length), Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task OversizedUpstreamBodyIsAnError()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => PaddedCompletion(Upstream.OpenAiCompatibleClient.MaxResponseBytes + 1);
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_error");
        // HttpClient throws HttpRequestException past the buffer cap: handled like a network error (retried once).
        Assert.Equal(2, factory.Stub.CallCount);

        // Nothing was cached: the next request calls upstream again.
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(4, factory.Stub.CallCount);
    }

    [Fact]
    public async Task UpstreamBodyAtTheLimitIsAccepted()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => PaddedCompletion(Upstream.OpenAiCompatibleClient.MaxResponseBytes);
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void PrimaryHandlerDoesNotFollowRedirects()
    {
        using var handler = Upstream.OpenAiCompatibleClient.CreatePrimaryHandler();
        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.AllowAutoRedirect);
    }

    [Fact]
    public async Task Upstream5xxIsRetriedThenUpstreamError()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_error");
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task Upstream429IsRetriedOnceThenSucceeds()
    {
        using var factory = new GatewayFactory();
        factory.Stub.EnqueueStatus(HttpStatusCode.TooManyRequests);
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task Upstream4xxIsNotRetried()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_error");
        Assert.Equal(1, factory.Stub.CallCount);
    }

    [Fact]
    public async Task UpstreamTimeoutIsRetriedThen504()
    {
        using var factory = new GatewayFactory();
        // What HttpClient throws when the per-attempt timeout token fires.
        factory.Stub.Default = _ => throw new TaskCanceledException("timeout");
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.GatewayTimeout, "upstream_timeout");
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task NetworkErrorIsRetriedThenUpstreamError()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => throw new HttpRequestException("network down");
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_error");
        Assert.Equal(2, factory.Stub.CallCount);
    }

    [Fact]
    public async Task MalformedEnvelopeIsInvalidOutput()
    {
        using var factory = new GatewayFactory();
        factory.Stub.Default = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[]}") };
        using var client = factory.CreateAuthedClient();
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadGateway, "upstream_invalid_output");
        Assert.Equal(2, factory.Stub.CallCount);
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        $"{{\"itemHash\":\"{GatewayFactory.FriesHash}\",\"canonicalTree\":\"{GatewayFactory.FriesTree}\",\"fallbackName\":\"fries\",\"tags\":[],\"quality\":0.9}}",
        $"{{\"itemHash\":\"{GatewayFactory.FriesHash.ToUpperInvariant()}\",\"canonicalTree\":\"{GatewayFactory.FriesTree}\",\"fallbackName\":\"fries\",\"tags\":[]}}",
        $"{{\"itemHash\":\"{GatewayFactory.FriesHash}\",\"canonicalTree\":\"fry(temp=hot;potato\",\"fallbackName\":\"fries\",\"tags\":[]}}",
        $"{{\"itemHash\":\"{GatewayFactory.FriesHash}\",\"canonicalTree\":\"{GatewayFactory.FriesTree}\",\"fallbackName\":\"fries\",\"tags\":[\"{new string('a', 33)}\"]}}",
        $"{{\"itemHash\":\"{GatewayFactory.FriesHash}\",\"canonicalTree\":\"{GatewayFactory.FriesTree}\",\"fallbackName\":\"fries\"}}",
        "not json",
        "",
        "{\"itemHash\":\"" + new string('a', 64) + "\",\"canonicalTree\":\"" + new string('a', 9000) + "\",\"fallbackName\":\"x\",\"tags\":[]}",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task AC10_InvalidRequestIs400WithoutUpstreamCall(string body)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();
        var response = await client.PostAsync("/v1/content/item-name", new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertError(response, HttpStatusCode.BadRequest, "invalid_request");
        Assert.Equal(0, factory.Stub.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Server_1")]
    public async Task BadServerIdIs400WithoutUpstreamCall(string? serverId)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayFactory.ServiceToken);
        if (serverId is not null) client.DefaultRequestHeaders.Add("X-Worktown-Server-Id", serverId);
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        await AssertError(response, HttpStatusCode.BadRequest, "invalid_request");
        Assert.Equal(0, factory.Stub.CallCount);
    }

    [Fact]
    public async Task AC10_HitWithDifferentTreeIs409()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody())).StatusCode);
        var response = await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody("fry(temp=hot,time=long;potato)"));
        await AssertError(response, HttpStatusCode.Conflict, "tree_mismatch");
        Assert.Equal(1, factory.Stub.CallCount);
        Assert.Contains(factory.Logs.Entries, e => e.StartsWith("Warning") && e.Contains("tree mismatch"));
    }

    public static TheoryData<string, string[]> OtherPromptInputs => new()
    {
        { "golden fried potato sticks", new[] { "fried" } },
        { "crispy fried potato sticks", new[] { "fried", "salty" } },
        { "crispy fried potato sticks", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(OtherPromptInputs))]
    public async Task HitWithSameTreeButOtherFallbackNameOrTagsIs409(string fallbackName, string[] tags)
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateAuthedClient();
        Assert.Equal(HttpStatusCode.OK, (await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody())).StatusCode);
        var response = await GatewayFactory.PostItemNameAsync(client, new
        {
            itemHash = GatewayFactory.FriesHash,
            canonicalTree = GatewayFactory.FriesTree,
            fallbackName,
            tags,
        });
        await AssertError(response, HttpStatusCode.Conflict, "tree_mismatch");
        Assert.Equal(1, factory.Stub.CallCount);
    }

    [Fact]
    public async Task AC11_OpenRouterBodyShape()
    {
        using var factory = new GatewayFactory(new Dictionary<string, string>
        {
            [GatewayOptions.OpenRouterProvidersKey] = "alpha, beta",
        });
        using var client = factory.CreateAuthedClient();
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());

        var body = JsonNode.Parse(Assert.Single(factory.Stub.Bodies))!;
        var format = body["response_format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.True(format["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("item_name", format["json_schema"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain("pattern", format["json_schema"]!["schema"]!.ToJsonString());
        Assert.True(body["provider"]!["require_parameters"]!.GetValue<bool>());
        Assert.True(body["provider"]!["allow_fallbacks"]!.GetValue<bool>());
        Assert.Equal(new[] { "alpha", "beta" }, body["provider"]!["only"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("none", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Equal(GatewayFactory.Model, body["model"]!.GetValue<string>());
        Assert.Equal(300, body["max_tokens"]!.GetValue<int>());
        Assert.Equal(0.7, body["temperature"]!.GetValue<double>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal($"Process: {GatewayFactory.FriesTree}\nPlain name: crispy fried potato sticks\nTags: fried",
            messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task AC11_OpenAiFlavorHasNoProviderOrReasoning()
    {
        using var factory = new GatewayFactory(new Dictionary<string, string>
        {
            [GatewayOptions.UpstreamFlavorKey] = GatewayOptions.FlavorOpenAi,
        });
        using var client = factory.CreateAuthedClient();
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());

        var body = JsonNode.Parse(Assert.Single(factory.Stub.Bodies))!.AsObject();
        Assert.False(body.ContainsKey("provider"));
        Assert.False(body.ContainsKey("reasoning"));
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task EmptyReasoningEffortOmitsReasoning()
    {
        using var factory = new GatewayFactory(new Dictionary<string, string>
        {
            [GatewayOptions.ReasoningEffortKey] = "",
        });
        using var client = factory.CreateAuthedClient();
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        var body = JsonNode.Parse(Assert.Single(factory.Stub.Bodies))!.AsObject();
        Assert.False(body.ContainsKey("reasoning"));
        Assert.False(body["provider"]!.AsObject().ContainsKey("only"));
    }

    [Fact]
    public async Task UpstreamGetsTheApiKeyAsBearer()
    {
        using var factory = new GatewayFactory();
        string? auth = null;
        factory.Stub.Default = request =>
        {
            auth = request.Headers.Authorization?.ToString();
            return StubUpstreamHandler.Completion(StubUpstreamHandler.ValidContent);
        };
        using var client = factory.CreateAuthedClient();
        await GatewayFactory.PostItemNameAsync(client, GatewayFactory.FriesBody());
        Assert.Equal("Bearer " + GatewayFactory.ApiKey, auth);
    }
}
