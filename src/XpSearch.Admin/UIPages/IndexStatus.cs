using System.Globalization;

using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;
using Microsoft.Extensions.Options;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Options;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "status",
    uiPageType: typeof(IndexStatusPage),
    name: "Status",
    templateName: "@xperience-community/xperience-search/IndexStatus",
    order: 800)]

namespace XpSearch.Admin.UIPages;

/// <summary>Initial state of the index status client template.</summary>
public class IndexStatusClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code name of the index the page reports on.</summary>
    public string IndexName { get; set; } = string.Empty;
}

/// <summary>One row of the "Documents by source" table.</summary>
public class SourceCountDto
{
    /// <summary>Gets or sets the <c>_source</c> value.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable provenance of the source.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets how many live documents carry the source.</summary>
    public long Count { get; set; }

    /// <summary>Gets or sets the source's share of the index, between 0 and 1.</summary>
    public double Share { get; set; }
}

/// <summary>One row of the "Recent ingestion" table.</summary>
public class IngestionEntryDto
{
    /// <summary>Gets or sets when the operation happened, formatted for display.</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>Gets or sets who wrote: the API key prefix, or <c>in-process</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets what was asked for: <c>upsert</c>, <c>patch</c>, <c>delete</c>, <c>clear</c> or <c>rebuild</c>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Gets or sets how many documents the operation touched.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets whether the operation was accepted.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Gets or sets the outcome description.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Everything the status page renders, in the shape the client template consumes.</summary>
public class IndexStatusDto
{
    /// <summary>How the page formats a moment in time: unambiguous, UTC, no locale surprises.</summary>
    public const string TimestampFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Gets or sets the code name of the index.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the health of the index, <c>Healthy</c> or <c>Degraded</c>.</summary>
    public string Health { get; set; } = nameof(Ingestion.Contract.Health.Healthy);

    /// <summary>Gets or sets how many documents the index holds.</summary>
    public long Documents { get; set; }

    /// <summary>Gets or sets how many distinct sources contributed documents.</summary>
    public int Sources { get; set; }

    /// <summary>
    /// Gets or sets how many queued writes failed to reach Lucene. Never sent on the ingestion wire
    /// contract - it is the queue's counter, surfaced here because the page's warning needs a number.
    /// </summary>
    public int FailedWrites { get; set; }

    /// <summary>Gets or sets when an external document was last written, or an empty string when none ever was.</summary>
    public string LastWrite { get; set; } = string.Empty;

    /// <summary>Gets or sets the document counts per source, largest first.</summary>
    public IReadOnlyList<SourceCountDto> BySource { get; set; } = [];

    /// <summary>Gets or sets the last few ingestion log entries, failed ones first while degraded.</summary>
    public IReadOnlyList<IngestionEntryDto> RecentIngestion { get; set; } = [];

    /// <summary>
    /// Gets or sets where the last rebuild of this index is: <c>none</c>, <c>running</c>,
    /// <c>stuck</c> or <c>finished</c>. Derived from the ingestion log, so it survives a reload and
    /// is the same answer for every editor looking at the page.
    /// </summary>
    public string RebuildState { get; set; } = nameof(RebuildPhase.None).ToLowerInvariant();

    /// <summary>Gets or sets when the rebuild started, or an empty string when no start was recorded.</summary>
    public string RebuildStartedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets when the last rebuild finished, or an empty string while one runs.</summary>
    public string RebuildFinishedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets how many documents the index held when the last rebuild finished.</summary>
    public int RebuildDocuments { get; set; }

    /// <summary>Gets or sets why no status could be read, or an empty string when it could.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Builds a response that reports a failure instead of numbers.</summary>
    /// <param name="message">What went wrong.</param>
    /// <returns>The response.</returns>
    public static IndexStatusDto Failed(string message) => new() { Error = message };

    /// <summary>Formats a moment for display, as UTC.</summary>
    /// <param name="moment">The moment, or <see langword="null"/>.</param>
    /// <returns>The formatted moment, or an empty string.</returns>
    public static string Format(DateTimeOffset? moment) =>
        moment is { } value
            ? value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + " UTC"
            : string.Empty;
}

/// <summary>
/// What this index holds, how the last writes went and the rebuild trigger (spec §10.8).
/// </summary>
/// <remarks>
/// A custom client template, because the page is a dashboard of derived values - counts per source,
/// a health verdict and the ingestion log - rather than a listing of a registered object type or an
/// editable form
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
/// It replaces the read-only text area the EDIT template allowed before; see ADR-0020 (AD-4a).
/// </remarks>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class IndexStatusPage : Page<IndexStatusClientProperties>
{
    /// <summary>How many ingestion log entries the page shows.</summary>
    public const int RecentEntryCount = 10;

    /// <summary>The <c>_source</c> value the Xperience content indexer writes.</summary>
    public const string XperienceSource = "xperience";

    /// <summary>
    /// The permission the Lucene integration gates a rebuild behind on its own index listing. Its
    /// <c>LuceneIndexPermissions.REBUILD</c> constant is <c>internal</c>, so the literal is repeated
    /// here (https://github.com/Kentico/xperience-by-kentico-lucene/blob/v15.0.5/src/Kentico.Xperience.Lucene.Admin/LuceneIndexPermissions.cs).
    /// </summary>
    public const string RebuildPermission = "Rebuild";

    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IXpSearchIndexer indexer;
    private readonly IIngestionQueue queue;
    private readonly ILuceneClient client;
    private readonly IIngestionLog log;
    private readonly XpSearchIngestionOptions options;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="IndexStatusPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="indexer">Reads document counts and health per index.</param>
    /// <param name="queue">The ingestion queue, for the number of writes that failed to reach Lucene.</param>
    /// <param name="client">The integration's index writer, decorated so a rebuild replays external documents.</param>
    /// <param name="log">Reads the recent ingestion entries and the rebuild rows, and records the rebuild.</param>
    /// <param name="options">Ingestion configuration, for how long a rebuild may run before it is suspect.</param>
    /// <param name="time">Clock, so the rebuild's start time is the server's.</param>
    public IndexStatusPage(
        ILuceneConfigurationStorageService storageService,
        IXpSearchIndexer indexer,
        IIngestionQueue queue,
        ILuceneClient client,
        IIngestionLog log,
        IOptions<XpSearchIngestionOptions> options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        this.storageService = storageService;
        this.indexer = indexer;
        this.queue = queue;
        this.client = client;
        this.log = log;
        this.options = options.Value;
        this.time = time;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    private string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    public override Task<IndexStatusClientProperties> ConfigureTemplateProperties(IndexStatusClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        properties.IndexName = IndexName;

        return Task.FromResult(properties);
    }

    /// <summary>Reads the current status of the index.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<IndexStatusDto>> Load(CancellationToken cancellationToken) =>
        ResponseFrom(await BuildAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Empties the index and writes it again.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status after the rebuild was triggered, and a success message.</returns>
    [PageCommand(Permission = RebuildPermission)]
    public async Task<ICommandResponse<IndexStatusDto>> Rebuild(CancellationToken cancellationToken)
    {
        string indexName = IndexName;

        if (string.IsNullOrEmpty(indexName))
        {
            return ResponseFrom(IndexStatusDto.Failed("This index is not registered.")).AddErrorMessage("This index is not registered.");
        }

        var startedAt = time.GetUtcNow();

        // The registered ILuceneClient is the ingestion package's decorator, so externally pushed
        // documents are replayed after the integration wipes the index (spec §10.2).
        await client.Rebuild(indexName, cancellationToken).ConfigureAwait(false);

        // The started row is the only durable record that a rebuild is going on: the page reads it
        // back on every load, so the state outlives this response and this browser tab.
        await log.WriteAsync(
            new IngestionLogEntry(
                "admin-ui",
                indexName,
                RebuildProgress.StartedOperation,
                0,
                true,
                "Rebuild triggered from the index tuning pages.",
                startedAt.UtcDateTime),
            cancellationToken)
            .ConfigureAwait(false);

        var status = await BuildAsync(cancellationToken).ConfigureAwait(false);

        return ResponseFrom(status).AddSuccessMessage($"Rebuild of '{indexName}' triggered.");
    }

    private static string KindOf(string source) =>
        string.Equals(source, XperienceSource, StringComparison.OrdinalIgnoreCase)
            ? "Content indexed by the CMS"
            : "External system, pushed through the ingestion API";

    private async Task<IndexStatusDto> BuildAsync(CancellationToken cancellationToken)
    {
        string indexName = IndexName;

        if (string.IsNullOrEmpty(indexName))
        {
            return IndexStatusDto.Failed("This index is not registered.");
        }

        IndexStatus status;

        try
        {
            status = await indexer.GetStatusAsync(indexName, cancellationToken).ConfigureAwait(false);
        }
        catch (Core.Abstractions.IndexNotFoundException)
        {
            return IndexStatusDto.Failed($"The index '{indexName}' has no Lucene storage yet. Rebuild it to create one.");
        }

        long total = status.Documents?.Total ?? 0;
        var bySource = (status.Documents?.BySource ?? [])
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new SourceCountDto
            {
                Source = entry.Key,
                Kind = KindOf(entry.Key),
                Count = entry.Value,
                Share = total == 0 ? 0 : (double)entry.Value / total,
            })
            .ToList();

        var rebuild = await RebuildProgress
            .ReadAsync(log, indexName, time.GetUtcNow(), options.RebuildStuckAfter, cancellationToken)
            .ConfigureAwait(false);

        // A running rebuild makes the indexer report Degraded on the wire, which is the answer an
        // external poller needs. On this page the rebuild has its own state, so the failed-writes
        // warning stays about failed writes.
        bool degraded = status.Health == Health.Degraded && !rebuild.Running;
        var recent = await log.ReadRecentAsync(indexName, RecentEntryCount, cancellationToken).ConfigureAwait(false);

        return new IndexStatusDto
        {
            IndexName = indexName,
            Health = degraded ? nameof(Ingestion.Contract.Health.Degraded) : nameof(Ingestion.Contract.Health.Healthy),
            RebuildState = rebuild.Phase.ToString().ToLowerInvariant(),
            RebuildStartedAt = IndexStatusDto.Format(rebuild.StartedAt),
            RebuildFinishedAt = IndexStatusDto.Format(rebuild.FinishedAt),
            RebuildDocuments = (int)(rebuild.Documents ?? 0),
            Documents = total,
            Sources = bySource.Count,
            FailedWrites = queue.FailedCount,
            LastWrite = IndexStatusDto.Format(status.LastWrite),
            BySource = bySource,

            // Newest first, but while the index is degraded the failures are what the reader came for,
            // so they are lifted to the top of the same ten entries.
            RecentIngestion = (degraded
                    ? recent.OrderBy(entry => entry.Succeeded).ThenByDescending(entry => entry.At)
                    : recent.OrderByDescending(entry => entry.At))
                .Select(entry => new IngestionEntryDto
                {
                    Timestamp = IndexStatusDto.Format(new DateTimeOffset(DateTime.SpecifyKind(entry.At, DateTimeKind.Utc))),
                    Source = entry.KeyPrefix,
                    Operation = entry.Operation,
                    Count = entry.DocumentCount,
                    Succeeded = entry.Succeeded,
                    Message = entry.Message,
                })
                .ToList(),
        };
    }
}
