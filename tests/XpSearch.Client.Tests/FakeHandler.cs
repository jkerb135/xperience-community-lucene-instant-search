using System.Net;
using System.Text;

namespace XpSearch.Client.Tests;

/// <summary>One request the client sent, captured whole so a test can assert on it after the fact.</summary>
internal sealed record CapturedRequest(HttpMethod Method, string Uri, string? Body, string? Authorization);

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/>: each queued step either answers or throws, and every
/// request is recorded. Nothing touches the network.
/// </summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> steps = new();

    /// <summary>Answers every request after the scripted ones, when set.</summary>
    public Func<HttpResponseMessage>? Fallback { get; set; }

    public List<CapturedRequest> Requests { get; } = [];

    public FakeHandler Respond(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        steps.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

        return this;
    }

    public FakeHandler RespondOk(string body) => Respond(HttpStatusCode.OK, body);

    public FakeHandler Throw(Exception exception)
    {
        steps.Enqueue(() => throw exception);

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri!.AbsoluteUri,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.Authorization?.ToString()));

        if (steps.Count > 0)
        {
            return steps.Dequeue()();
        }

        return Fallback is null
            ? throw new InvalidOperationException($"Unscripted request: {request.Method} {request.RequestUri}")
            : Fallback();
    }
}
