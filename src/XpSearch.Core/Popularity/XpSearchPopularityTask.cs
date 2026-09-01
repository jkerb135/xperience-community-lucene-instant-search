using CMS.Scheduler;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Analytics;
using XpSearch.Core.Options;
using XpSearch.Core.Popularity;

[assembly: RegisterScheduledTask(XpSearchPopularityTask.Identifier, typeof(XpSearchPopularityTask))]

namespace XpSearch.Core.Popularity;

/// <summary>
/// Aggregates the query log's clicks into the per-document popularity signal and its suggested rules
/// (RK-1) and mines its reformulations for suggested synonyms (SY-1), over the last
/// <c>XpSearchOptions.Analytics.PopularityLookbackDays</c> days.
/// </summary>
/// <remarks>
/// <para>
/// Registration only makes the task selectable; its schedule and enabled state have to be created
/// once in the administration's <em>Scheduled tasks</em> application, the same as
/// <see cref="XpSearchQueryLogRetentionTask"/>
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/scheduled-tasks).
/// See <c>docs/guides/popularity-boosts.md</c>.
/// </para>
/// <para>
/// The window's rows are read once and grouped by index; each group is handed to
/// <see cref="PopularityAggregator"/> and, next to it, to <see cref="SynonymMiner"/> for SY-1's
/// mined synonym candidates.
/// </para>
/// </remarks>
public sealed class XpSearchPopularityTask : IScheduledTask
{
    /// <summary>The identifier the task is registered and selected under.</summary>
    public const string Identifier = "XpSearch.PopularitySignal";

    private readonly IQueryLogStore log;
    private readonly IPopularitySignalStore store;
    private readonly ISynonymSuggestionStore synonyms;
    private readonly XpSearchOptions options;
    private readonly ILogger<XpSearchPopularityTask> logger;

    /// <summary>Initializes a new instance of the <see cref="XpSearchPopularityTask"/> class.</summary>
    /// <param name="log">Where the query log lives.</param>
    /// <param name="store">Where the signal is stored.</param>
    /// <param name="synonyms">Where the mined synonym candidates are stored (SY-1).</param>
    /// <param name="options">The configured search options.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchPopularityTask(
        IQueryLogStore log,
        IPopularitySignalStore store,
        ISynonymSuggestionStore synonyms,
        IOptions<XpSearchOptions> options,
        ILogger<XpSearchPopularityTask> logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(synonyms);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.log = log;
        this.store = store;
        this.synonyms = synonyms;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledTaskExecutionResult> Execute(ScheduledTaskConfigurationInfo task, CancellationToken cancellationToken)
    {
        var analytics = options.Analytics;
        var now = DateTime.UtcNow;
        var from = now.AddDays(-Math.Max(1, analytics.PopularityLookbackDays));

        var rows = await log.ReadAsync(string.Empty, from, now, cancellationToken).ConfigureAwait(false);

        int indexes = 0;
        int documents = 0;
        int suggested = 0;
        int mined = 0;

        foreach (var group in rows.GroupBy(row => row.IndexName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                continue;
            }

            var aggregate = PopularityAggregator.Aggregate(
                group,
                analytics.PopularityDocumentLimit,
                analytics.PopularitySuggestionQueries);

            await store.ReplaceAsync(group.Key, aggregate, now, cancellationToken).ConfigureAwait(false);

            var pairs = SynonymMiner.Mine(
                group,
                analytics.SynonymMinimumOccurrences,
                analytics.SynonymWindowSeconds);

            await synonyms.ReplaceAsync(group.Key, pairs, now, cancellationToken).ConfigureAwait(false);

            indexes++;
            documents += aggregate.Scores.Count;
            suggested += aggregate.Suggestions.Count;
            mined += pairs.Count;
        }

        logger.LogInformation(
            "Computed popularity for {Documents} documents across {Indexes} indexes since {From:u}, with {Suggested} suggested rules and {Mined} suggested synonyms.",
            documents,
            indexes,
            from,
            suggested,
            mined);

        return new ScheduledTaskExecutionResult(
            $"Popularity computed for {documents} documents across {indexes} indexes since {from:u}; {suggested} suggested rules, {mined} suggested synonyms.");
    }
}
