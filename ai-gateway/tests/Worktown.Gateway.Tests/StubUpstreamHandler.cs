using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Worktown.Gateway.Tests;

/// <summary>
/// Fake upstream: serves queued responses (then <see cref="Default"/>), counts calls and keeps
/// request bodies. Only https://upstream.test is accepted, so no test can reach the network.
/// </summary>
public sealed class StubUpstreamHandler : HttpMessageHandler
{
    public const string Host = "upstream.test";
    public const string ValidContent = "{\"name\":\"Golden Spud Sticks\",\"description\":\"Crisp sticks of fried potato.\"}";

    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _queue = new();
    private readonly ConcurrentQueue<string> _bodies = new();
    private readonly ConcurrentQueue<Uri> _uris = new();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public IReadOnlyList<string> Bodies => _bodies.ToArray();

    public IReadOnlyList<Uri> RequestUris => _uris.ToArray();

    /// <summary>Used when the queue is empty. Returns a valid completion by default.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Default { get; set; } = _ => Completion(ValidContent);

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _queue.Enqueue(responder);

    public void EnqueueContent(string content, string finishReason = "stop") =>
        Enqueue(_ => Completion(content, finishReason));

    public void EnqueueStatus(HttpStatusCode status) => Enqueue(_ => new HttpResponseMessage(status));

    /// <summary>An OpenAI-style chat completion envelope around <paramref name="content"/>.</summary>
    public static HttpResponseMessage Completion(string content, string finishReason = "stop")
    {
        var envelope = new JsonObject
        {
            ["id"] = "cmpl-test",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["finish_reason"] = finishReason,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
            }),
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || request.RequestUri.Host != Host || request.RequestUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Stub only serves https://" + Host);
        Interlocked.Increment(ref _callCount);
        _uris.Enqueue(request.RequestUri);
        _bodies.Enqueue(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        var responder = _queue.TryDequeue(out var queued) ? queued : Default;
        return responder(request);
    }
}
