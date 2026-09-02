using System.Text.Json.Serialization;

using XpSearch.Client.Contract;

namespace XpSearch.Client;

/// <summary>
/// The aggregate of every request <see cref="XpSearchIngestionIndexClient.UpsertAsync"/> made: one
/// upsert call can become several HTTP requests, and this is the single answer they add up to.
/// </summary>
public sealed class UpsertResult
{
    private readonly List<IngestionError> errors = [];
    private readonly List<string> taskIds = [];

    /// <summary>Gets the total number of documents the server accepted across every batch.</summary>
    public long Indexed { get; private set; }

    /// <summary>Gets the total number of documents schema validation rejected across every batch.</summary>
    public long Failed { get; private set; }

    /// <summary>Gets the number of HTTP requests the call was split into.</summary>
    public int Batches { get; private set; }

    /// <summary>Gets every per-document error, in batch order.</summary>
    public IReadOnlyList<IngestionError> Errors => errors;

    /// <summary>Gets the task identifier of each batch that was queued rather than awaited, in batch order.</summary>
    public IReadOnlyList<string> TaskIds => taskIds;

    internal void Add(UpsertResponse response)
    {
        Batches++;
        Indexed += response.Indexed;
        Failed += response.Failed;

        if (response.Errors is not null)
        {
            errors.AddRange(response.Errors);
        }

        if (response.TaskId is not null)
        {
            taskIds.Add(response.TaskId);
        }
    }
}

/// <summary>
/// The aggregate of every request <see cref="XpSearchIngestionIndexClient.DeleteManyAsync"/> made.
/// </summary>
public sealed class DeleteResult
{
    private readonly List<string> taskIds = [];

    /// <summary>Gets the total number of stored documents removed across every batch.</summary>
    public long Deleted { get; private set; }

    /// <summary>Gets the number of HTTP requests the call was split into.</summary>
    public int Batches { get; private set; }

    /// <summary>Gets the task identifier of each batch that was queued rather than awaited, in batch order.</summary>
    public IReadOnlyList<string> TaskIds => taskIds;

    internal void Add(DeleteResponse response)
    {
        Batches++;
        Deleted += response.Deleted;

        if (response.TaskId is not null)
        {
            taskIds.Add(response.TaskId);
        }
    }
}

/// <summary>
/// An RFC 9457 Problem Details body, as the ingestion API produces for every failed request.
/// </summary>
public sealed class XpSearchProblemDetails
{
    /// <summary>Gets or sets the URI identifying the problem type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the short, human-readable summary.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the HTTP status code the problem was answered with.</summary>
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    /// <summary>Gets or sets the explanation specific to this occurrence.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    /// <summary>Gets or sets the URI identifying this occurrence.</summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; set; }

    /// <summary>Gets or sets the per-field messages of a validation problem, keyed by field name.</summary>
    [JsonPropertyName("errors")]
    public Dictionary<string, string[]>? Errors { get; set; }
}

/// <summary>
/// A failed ingestion request: a response the server refused, or a transport failure that outlived
/// the retries.
/// </summary>
public sealed class XpSearchIngestionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionException"/> class.</summary>
    /// <param name="message">The message.</param>
    public XpSearchIngestionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The transport failure underneath, if any.</param>
    public XpSearchIngestionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionException"/> class.</summary>
    public XpSearchIngestionException()
        : base("The ingestion request failed.")
    {
    }

    internal XpSearchIngestionException(string message, int statusCode, XpSearchProblemDetails? problem, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        Problem = problem;
        ResponseBody = responseBody;
    }

    /// <summary>Gets the HTTP status code, or <see langword="null"/> when the server never answered.</summary>
    public int? StatusCode { get; }

    /// <summary>Gets the parsed Problem Details body, when the answer carried one.</summary>
    public XpSearchProblemDetails? Problem { get; }

    /// <summary>Gets the raw response body, for a failure that was not Problem Details.</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Gets what a multi-batch <see cref="XpSearchIngestionIndexClient.UpsertAsync"/> had already
    /// written when the failing batch was reached, so a caller knows where to resume. Set only by
    /// <c>UpsertAsync</c>; <see langword="null"/> for every other verb.
    /// </summary>
    public UpsertResult? PartialUpsert { get; internal set; }
}
