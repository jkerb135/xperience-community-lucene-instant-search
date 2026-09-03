using CMS.Scheduler;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Options;
using XpSearch.Core.Popularity;

[assembly: RegisterScheduledTask(XpSearchQueryLogRetentionTask.Identifier, typeof(XpSearchQueryLogRetentionTask))]

namespace XpSearch.Core.Analytics;

/// <summary>
/// Deletes each index's search analytics older than that index's retention window: the query log, and
/// the popularity and synonym suggestions a human already answered (spec §9.2, AR-2).
/// </summary>
/// <remarks>
/// The window is the <em>Remove search analytics older than X days</em> setting on the index's Search
/// settings page. Rows left behind by an index nobody registers any more are pruned with the
/// code-configured defaults. Pending suggestions are never deleted - the mining task owns them - and
/// the popularity scores are replaced on every run, so nothing prunes them.
/// Registration only makes the task selectable; the task <em>configuration</em> - its schedule and
/// enabled state - has to be created once in the administration's <em>Scheduled tasks</em>
/// application, which is the only documented way to configure one
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/scheduled-tasks).
/// See <c>docs/guides/analytics.md</c> for the steps.
/// </remarks>
public sealed class XpSearchQueryLogRetentionTask : IScheduledTask
{
    /// <summary>The identifier the task is registered and selected under.</summary>
    public const string Identifier = "XpSearch.QueryLogRetention";

    private readonly IQueryLogStore store;
    private readonly IPopularitySignalStore popularity;
    private readonly ISynonymSuggestionStore synonyms;
    private readonly ILuceneIndexAccessor accessor;
    private readonly IOptionsMonitor<XpSearchIndexSettings> settings;
    private readonly ILogger<XpSearchQueryLogRetentionTask> logger;

    /// <summary>Initializes a new instance of the <see cref="XpSearchQueryLogRetentionTask"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="popularity">Where the popularity suggestions live.</param>
    /// <param name="synonyms">Where the synonym suggestions live.</param>
    /// <param name="accessor">The Lucene seam, which knows what is registered.</param>
    /// <param name="settings">The current per-index settings.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchQueryLogRetentionTask(
        IQueryLogStore store,
        IPopularitySignalStore popularity,
        ISynonymSuggestionStore synonyms,
        ILuceneIndexAccessor accessor,
        IOptionsMonitor<XpSearchIndexSettings> settings,
        ILogger<XpSearchQueryLogRetentionTask> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(popularity);
        ArgumentNullException.ThrowIfNull(synonyms);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.popularity = popularity;
        this.synonyms = synonyms;
        this.accessor = accessor;
        this.settings = settings;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledTaskExecutionResult> Execute(ScheduledTaskConfigurationInfo task, CancellationToken cancellationToken)
    {
        var registered = accessor.IndexNames();
        var reported = new List<string>();

        foreach (string index in registered)
        {
            reported.Add(await PruneAsync(index, settings.Get(index), cancellationToken).ConfigureAwait(false));
        }

        // Rows an index left behind after it was deleted or renamed have no settings of their own; the
        // unnamed options instance is the code-configured defaults.
        var defaults = settings.Get(Microsoft.Extensions.Options.Options.DefaultName);

        foreach (string orphan in await OrphansAsync(registered, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Search analytics of index {Index}, which is not registered any more, are pruned with the configured defaults.",
                orphan);

            reported.Add(await PruneAsync(orphan, defaults, cancellationToken).ConfigureAwait(false));
        }

        string message = reported.Count == 0
            ? "No search index holds analytics to prune."
            : string.Join("; ", reported);

        logger.LogInformation("{Message}", message);

        return new ScheduledTaskExecutionResult(message);
    }

    /// <summary>Index names present in the three tables that no registered index answers for.</summary>
    private async Task<IReadOnlyList<string>> OrphansAsync(IReadOnlyList<string> registered, CancellationToken cancellationToken)
    {
        var stored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        stored.UnionWith(await store.IndexNamesAsync(cancellationToken).ConfigureAwait(false));
        stored.UnionWith(await popularity.SuggestionIndexNamesAsync(cancellationToken).ConfigureAwait(false));
        stored.UnionWith(await synonyms.IndexNamesAsync(cancellationToken).ConfigureAwait(false));

        stored.ExceptWith(registered);

        return [.. stored.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<string> PruneAsync(string index, XpSearchIndexSettings indexSettings, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, indexSettings.RetentionDays));
        int batchSize = Math.Max(1, indexSettings.RetentionBatchSize);

        int logs = await DrainAsync(store.DeleteOlderThanAsync, index, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
        int boosts = await DrainAsync(popularity.DeleteAnsweredOlderThanAsync, index, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
        int pairs = await DrainAsync(synonyms.DeleteAnsweredOlderThanAsync, index, cutoff, batchSize, cancellationToken).ConfigureAwait(false);

        return $"{index}: {Count(logs, "query log row")}, {Count(boosts, "popularity suggestion")}, "
            + $"{Count(pairs, "synonym suggestion")} (older than {cutoff:yyyy-MM-dd})";
    }

    /// <summary>Deletes in batches until a short batch says the store ran out of rows.</summary>
    private static async Task<int> DrainAsync(
        Func<string, DateTime, int, CancellationToken, Task<int>> delete,
        string index,
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        int deleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int batch = await delete(index, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
            deleted += batch;

            if (batch < batchSize)
            {
                break;
            }
        }

        return deleted;
    }

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? string.Empty : "s")}";
}
