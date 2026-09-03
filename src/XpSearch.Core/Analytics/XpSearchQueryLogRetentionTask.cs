using CMS.Scheduler;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Analytics;
using XpSearch.Core.Options;
using XpSearch.Core.Popularity;

[assembly: RegisterScheduledTask(XpSearchQueryLogRetentionTask.Identifier, typeof(XpSearchQueryLogRetentionTask))]

namespace XpSearch.Core.Analytics;

/// <summary>
/// Deletes search analytics older than the retention window: the query log, and the popularity and
/// synonym suggestions a human already answered (spec §9.2, AR-1).
/// </summary>
/// <remarks>
/// The window is <c>Analytics.RetentionDays</c>, which an administrator edits on the Settings page of
/// the Search ingestion application. Pending suggestions are never deleted - the mining task owns
/// them - and the popularity scores are replaced on every run, so nothing prunes them.
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
    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly ILogger<XpSearchQueryLogRetentionTask> logger;

    /// <summary>Initializes a new instance of the <see cref="XpSearchQueryLogRetentionTask"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="popularity">Where the popularity suggestions live.</param>
    /// <param name="synonyms">Where the synonym suggestions live.</param>
    /// <param name="options">The current search options.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchQueryLogRetentionTask(
        IQueryLogStore store,
        IPopularitySignalStore popularity,
        ISynonymSuggestionStore synonyms,
        IOptionsMonitor<XpSearchOptions> options,
        ILogger<XpSearchQueryLogRetentionTask> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(popularity);
        ArgumentNullException.ThrowIfNull(synonyms);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.popularity = popularity;
        this.synonyms = synonyms;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledTaskExecutionResult> Execute(ScheduledTaskConfigurationInfo task, CancellationToken cancellationToken)
    {
        var analytics = options.CurrentValue.Analytics;
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, analytics.RetentionDays));
        int batchSize = Math.Max(1, analytics.RetentionBatchSize);

        int logs = await DrainAsync(store.DeleteOlderThanAsync, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
        int boosts = await DrainAsync(popularity.DeleteAnsweredOlderThanAsync, cutoff, batchSize, cancellationToken).ConfigureAwait(false);
        int pairs = await DrainAsync(synonyms.DeleteAnsweredOlderThanAsync, cutoff, batchSize, cancellationToken).ConfigureAwait(false);

        string message =
            $"Deleted {Count(logs, "query log row")}, {Count(boosts, "popularity suggestion")}, {Count(pairs, "synonym suggestion")} older than {cutoff:u}.";

        logger.LogInformation("{Message}", message);

        return new ScheduledTaskExecutionResult(message);
    }

    /// <summary>Deletes in batches until a short batch says the store ran out of rows.</summary>
    private static async Task<int> DrainAsync(
        Func<DateTime, int, CancellationToken, Task<int>> delete,
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        int deleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int batch = await delete(cutoff, batchSize, cancellationToken).ConfigureAwait(false);
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
