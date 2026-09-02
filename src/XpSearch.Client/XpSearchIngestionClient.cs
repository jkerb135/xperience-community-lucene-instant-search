using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using XpSearch.Client.Contract;

namespace XpSearch.Client;

/// <summary>
/// Knobs of <see cref="XpSearchIngestionClient"/>. The two caps default to the ingestion API's own
/// server-side limits (<c>XpSearchIngestionOptions.MaxDocumentsPerRequest</c> and
/// <c>MaxRequestBytes</c>); lower them here if the host raised or lowered its own.
/// </summary>
public sealed class XpSearchIngestionClientOptions
{
    /// <summary>Gets or sets the documents one request may carry. Defaults to 1000, the server's cap.</summary>
    public int MaxDocumentsPerRequest { get; set; } = 1_000;

    /// <summary>Gets or sets the bytes one request body may carry. Defaults to 10 MB, the server's cap.</summary>
    public long MaxRequestBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets how many times a request is sent before it gives up, first try included. Defaults to 4.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Gets or sets the backoff before the first retry; it doubles per attempt. Defaults to 500ms.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the ceiling on one backoff, <c>Retry-After</c> included. Defaults to 30s.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The wait between attempts. Replaced by the tests so a backoff schedule can be asserted without sleeping.</summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = Task.Delay;

    /// <summary>The jitter factor source, in [0,1). Replaced by the tests to make the schedule deterministic.</summary>
    internal Func<double> NextDouble { get; set; } = () => Random.Shared.NextDouble();
}

/// <summary>
/// Typed client for the Xperience Search ingestion API (spec §10.5). Kentico-free by design: it is
/// meant for the code that pushes documents in — a PIM sync job, a console importer, a build
/// pipeline — which runs outside the Xperience application. Code running <em>inside</em> Xperience
/// should use <c>IXpSearchIndexer</c> from <c>XperienceCommunity.Search.Ingestion</c> instead and
/// skip HTTP entirely.
/// </summary>
/// <remarks>
/// The verbs mirror the endpoints one to one: <c>Index(name).UpsertAsync</c>, <c>PatchAsync</c>,
/// <c>DeleteAsync</c>, <c>DeleteManyAsync</c>, <c>ClearAsync</c>, <c>RebuildAsync</c>,
/// <c>GetStatusAsync</c>, plus <see cref="ListIndexesAsync"/> at the root.
/// </remarks>
public sealed class XpSearchIngestionClient : IDisposable
{
    /// <summary>Common prefix of every ingestion route; mirrors <c>IngestionContractConstants.RoutePrefix</c>.</summary>
    internal const string RoutePrefix = "api/xpsearch/admin";

    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Uri baseAddress;
    private readonly string apiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="XpSearchIngestionClient"/> class with its own
    /// <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL of the Xperience application, for example <c>https://example.com</c>.</param>
    /// <param name="apiKey">The ingestion API key. A server-side secret: never ship it to a browser.</param>
    /// <param name="options">Batching and retry settings; the defaults match the server.</param>
    public XpSearchIngestionClient(string baseUrl, string apiKey, XpSearchIngestionClientOptions? options = null)
        : this(new HttpClient(), baseUrl, apiKey, options, ownsHttp: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XpSearchIngestionClient"/> class over a supplied
    /// <see cref="HttpClient"/> — the <c>IHttpClientFactory</c> case. The client is not disposed with
    /// this instance and its <see cref="HttpClient.BaseAddress"/> must be set.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send on.</param>
    /// <param name="apiKey">The ingestion API key.</param>
    /// <param name="options">Batching and retry settings; the defaults match the server.</param>
    public XpSearchIngestionClient(HttpClient httpClient, string apiKey, XpSearchIngestionClientOptions? options = null)
        : this(
            httpClient,
            (httpClient ?? throw new ArgumentNullException(nameof(httpClient))).BaseAddress?.ToString()
                ?? throw new ArgumentException("The HttpClient has no BaseAddress; the ingestion client cannot build absolute URLs.", nameof(httpClient)),
            apiKey,
            options,
            ownsHttp: false)
    {
    }

    private XpSearchIngestionClient(HttpClient httpClient, string baseUrl, string apiKey, XpSearchIngestionClientOptions? options, bool ownsHttp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        http = httpClient;
        this.ownsHttp = ownsHttp;
        this.apiKey = apiKey;
        Options = options ?? new XpSearchIngestionClientOptions();

        if (Options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), Options.MaxAttempts, "MaxAttempts must be at least 1.");
        }

        // A trailing slash so a base URL with a path prefix (an app under /site) keeps it when the
        // relative route is resolved against it.
        baseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);
    }

    /// <summary>Gets the batching and retry settings in force.</summary>
    public XpSearchIngestionClientOptions Options { get; }

    /// <summary>
    /// Builds a <see cref="PushDocument"/> from any object or dictionary: every property of the
    /// serialized object becomes an attribute, validated server-side against the index schema.
    /// </summary>
    /// <param name="id">The caller-owned, stable document id. Re-pushing it replaces the document.</param>
    /// <param name="attributes">An object or dictionary whose properties are the document's attributes.</param>
    /// <param name="source">The document's <c>_source</c>; defaults to the server's (<c>external</c>).</param>
    /// <returns>The document, ready for <see cref="XpSearchIngestionIndexClient.UpsertAsync"/>.</returns>
    public static PushDocument Document(string id, object attributes, string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(attributes);

        var element = JsonSerializer.SerializeToElement(attributes, Converter.Settings);

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Attributes must serialize to a JSON object.", nameof(attributes));
        }

        var document = new PushDocument { Id = id, Source = source };

        foreach (var property in element.EnumerateObject())
        {
            document.Attributes[property.Name] = property.Value;
        }

        return document;
    }

    /// <summary>Returns the verbs scoped to one index.</summary>
    /// <param name="index">Code name of the index.</param>
    /// <returns>The index-scoped client.</returns>
    public XpSearchIngestionIndexClient Index(string index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(index);

        return new XpSearchIngestionIndexClient(this, index);
    }

    /// <summary>Lists every registered index and the schema pushed documents are validated against.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The index list.</returns>
    public async Task<IndexListResponse> ListIndexesAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<IndexListResponse>(HttpMethod.Get, $"{RoutePrefix}/indexes", body: null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttp)
        {
            http.Dispose();
        }
    }

    /// <summary>Serializes a request body once, so its size can be measured before it is sent.</summary>
    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Converter.Settings);

    /// <summary>
    /// Sends one request, retrying 408/429/5xx and transport failures with exponential backoff and
    /// jitter. Never retries another 4xx: a validation 400 sent again is the same 400, slower.
    /// </summary>
    internal async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string relativeUri, byte[]? body, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;

            try
            {
                using var request = new HttpRequestMessage(method, new Uri(baseAddress, relativeUri));

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                }

                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= Options.MaxAttempts)
                {
                    throw new XpSearchIngestionException($"{method} {relativeUri} failed after {attempt} attempt(s): {exception.Message}", exception);
                }

                await Options.DelayAsync(Backoff(attempt, retryAfter: null), cancellationToken).ConfigureAwait(false);

                continue;
            }

            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return text.Length == 0
                        ? throw new XpSearchIngestionException($"{method} {relativeUri} answered {(int)response.StatusCode} with an empty body.")
                        : JsonSerializer.Deserialize<TResponse>(text, Converter.Settings)
                            ?? throw new XpSearchIngestionException($"{method} {relativeUri} answered {(int)response.StatusCode} with a null body.");
                }

                if (IsRetryable(response.StatusCode) && attempt < Options.MaxAttempts)
                {
                    await Options.DelayAsync(Backoff(attempt, RetryAfter(response)), cancellationToken).ConfigureAwait(false);

                    continue;
                }

                throw Failure(method, relativeUri, response.StatusCode, text);
            }
        }
    }

    /// <summary>408, 429 and 5xx are worth another try; every other 4xx is the caller's own fault.</summary>
    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static XpSearchIngestionException Failure(HttpMethod method, string relativeUri, HttpStatusCode status, string body)
    {
        XpSearchProblemDetails? problem = null;

        try
        {
            problem = JsonSerializer.Deserialize<XpSearchProblemDetails>(body, Converter.Settings);
        }
        catch (JsonException)
        {
            // Not every failure is Problem Details (a proxy's HTML 502, for one); the raw body is kept.
        }

        string detail = problem?.Detail ?? problem?.Title ?? (body.Length > 200 ? body[..200] : body);

        return new XpSearchIngestionException(
            $"{method} {relativeUri} answered {(int)status}{(detail.Length == 0 ? string.Empty : $": {detail}")}",
            (int)status,
            problem?.Title is null && problem?.Detail is null && problem?.Status is null ? null : problem,
            body);
    }

    /// <summary>
    /// <c>Retry-After</c> when the server sent one, otherwise <c>base * 2^(attempt-1)</c> with half
    /// jitter (a uniform factor in [0.5, 1)), capped at <see cref="XpSearchIngestionClientOptions.MaxRetryDelay"/>.
    /// </summary>
    private TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } after)
        {
            return after < TimeSpan.Zero ? TimeSpan.Zero : Min(after, Options.MaxRetryDelay);
        }

        double milliseconds = Options.RetryBaseDelay.TotalMilliseconds
            * Math.Pow(2, attempt - 1)
            * (0.5 + (0.5 * Options.NextDouble()));

        return Min(TimeSpan.FromMilliseconds(milliseconds), Options.MaxRetryDelay);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    /// <summary>Escapes a route segment; an id may hold anything the caller owns.</summary>
    internal static string Segment(string value) => Uri.EscapeDataString(value);

    /// <summary>Formats a <c>waitForIndex</c> query string, empty when the write is not awaited.</summary>
    internal static string WaitQuery(bool waitForIndex) => waitForIndex ? "?waitForIndex=true" : string.Empty;
}
