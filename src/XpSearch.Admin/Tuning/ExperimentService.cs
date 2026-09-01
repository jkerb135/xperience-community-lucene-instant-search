using CMS.DataEngine;

using XpSearch.Admin.Persistence;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tuning;

/// <summary>
/// The state machine of an experiment (XP-1). Draft to Running to Concluded, one way, and the split
/// is fixed once traffic is being divided by it - a split that moved mid-experiment would make every
/// number collected before the change meaningless.
/// </summary>
/// <remarks>
/// Separate from <see cref="ExperimentService"/> because the rules are the part worth testing without
/// a database.
/// </remarks>
public static class ExperimentRules
{
    /// <summary>The smallest share of traffic variant B can get.</summary>
    public const int MinSplit = 1;

    /// <summary>The largest share of traffic variant B can get.</summary>
    public const int MaxSplit = 99;

    /// <summary>Tells whether a split percentage is one an experiment can run with.</summary>
    /// <param name="splitPercent">The percentage of traffic for variant B.</param>
    /// <returns><see langword="true"/> when both variants get traffic.</returns>
    public static bool IsValidSplit(int splitPercent) => splitPercent is >= MinSplit and <= MaxSplit;

    /// <summary>Tells whether an experiment in this state can be started.</summary>
    /// <param name="state">The current state.</param>
    /// <returns><see langword="true"/> when the transition is allowed.</returns>
    public static bool CanStart(ExperimentState state) => state == ExperimentState.Draft;

    /// <summary>Tells whether an experiment in this state can be concluded.</summary>
    /// <param name="state">The current state.</param>
    /// <returns><see langword="true"/> when the transition is allowed.</returns>
    public static bool CanConclude(ExperimentState state) => state == ExperimentState.Running;

    /// <summary>Tells whether the split of an experiment in this state can still be changed.</summary>
    /// <param name="state">The current state.</param>
    /// <returns><see langword="true"/> when no traffic has been divided by it yet.</returns>
    public static bool CanChangeSplit(ExperimentState state) => state == ExperimentState.Draft;

    /// <summary>Tells whether an experiment in this state blocks a new one on the same index.</summary>
    /// <param name="state">The current state.</param>
    /// <returns><see langword="true"/> for anything that is not over.</returns>
    public static bool BlocksNewExperiment(ExperimentState state) => state != ExperimentState.Concluded;
}

/// <summary>
/// Creating, starting and concluding an index's experiment, including the cloning and the promotion
/// of its variant-B tuning rows (XP-1).
/// </summary>
public interface IExperimentService
{
    /// <summary>
    /// Creates a draft experiment for an index and clones every live tuning row of that index into
    /// variant-B copies of it.
    /// </summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="displayName">What the editor calls the experiment.</param>
    /// <param name="splitPercent">Percentage of traffic for variant B, 1 to 99.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created experiment.</returns>
    /// <exception cref="InvalidOperationException">The index already has an experiment that is not concluded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The split would leave a variant without traffic.</exception>
    Task<XpSearchExperimentInfo> CreateDraftAsync(string indexName, string displayName, int splitPercent, CancellationToken cancellationToken);

    /// <summary>Changes the traffic split of a draft experiment.</summary>
    /// <param name="experimentId">Identifier of the experiment.</param>
    /// <param name="splitPercent">Percentage of traffic for variant B, 1 to 99.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="InvalidOperationException">The experiment is already running or concluded.</exception>
    Task SetSplitAsync(int experimentId, int splitPercent, CancellationToken cancellationToken);

    /// <summary>Starts splitting the index's traffic between the live tuning and the draft.</summary>
    /// <param name="experimentId">Identifier of the experiment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the experiment is running.</returns>
    /// <exception cref="InvalidOperationException">The experiment is not a draft.</exception>
    Task StartAsync(int experimentId, CancellationToken cancellationToken);

    /// <summary>Ends the experiment, either promoting variant B to live or throwing it away.</summary>
    /// <param name="experimentId">Identifier of the experiment.</param>
    /// <param name="promote"><see langword="true"/> to replace the live tuning with variant B.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the experiment is concluded and the caches are dropped.</returns>
    /// <exception cref="InvalidOperationException">The experiment is not running.</exception>
    Task ConcludeAsync(int experimentId, bool promote, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IExperimentService"/>, over the tuning module classes.
/// </summary>
/// <remarks>
/// <para>
/// Every write goes through an info provider, whose object types touch their dummy cache keys, so the
/// cached tuning of the index and the cached "which experiment is running" lookup both drop by
/// themselves. The response cache does not depend on those object types, so it is evicted explicitly:
/// promoting a variant changes what a search returns, and waiting out the TTL would serve the losing
/// tuning after the editor was told the winner is live.
/// </para>
/// <para>
/// Nothing here is transactional. A crash halfway through a promotion leaves some rows promoted; the
/// experiment is then still Running and the operation can be repeated. See KNOWN-LIMITATIONS.
/// </para>
/// </remarks>
public sealed class ExperimentService : IExperimentService
{
    private readonly IInfoProvider<XpSearchExperimentInfo> experiments;
    private readonly IInfoProvider<XpSearchRuleInfo> rules;
    private readonly IInfoProvider<XpSearchSynonymInfo> synonyms;
    private readonly IInfoProvider<XpSearchStopwordListInfo> stopwords;
    private readonly IInfoProvider<XpSearchFieldWeightInfo> weights;
    private readonly ISearchCache cache;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="ExperimentService"/> class.</summary>
    /// <param name="experiments">Provider of experiment objects.</param>
    /// <param name="rules">Provider of rule objects.</param>
    /// <param name="synonyms">Provider of synonym objects.</param>
    /// <param name="stopwords">Provider of stopword list objects.</param>
    /// <param name="weights">Provider of field weight objects.</param>
    /// <param name="cache">The response cache, evicted when the tuning an index answers with changes.</param>
    /// <param name="time">Clock used for the started and ended stamps.</param>
    public ExperimentService(
        IInfoProvider<XpSearchExperimentInfo> experiments,
        IInfoProvider<XpSearchRuleInfo> rules,
        IInfoProvider<XpSearchSynonymInfo> synonyms,
        IInfoProvider<XpSearchStopwordListInfo> stopwords,
        IInfoProvider<XpSearchFieldWeightInfo> weights,
        ISearchCache cache,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(synonyms);
        ArgumentNullException.ThrowIfNull(stopwords);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(time);

        this.experiments = experiments;
        this.rules = rules;
        this.synonyms = synonyms;
        this.stopwords = stopwords;
        this.weights = weights;
        this.cache = cache;
        this.time = time;
    }

    /// <inheritdoc />
    public async Task<XpSearchExperimentInfo> CreateDraftAsync(
        string indexName,
        string displayName,
        int splitPercent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        if (!ExperimentRules.IsValidSplit(splitPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(splitPercent),
                splitPercent,
                $"The traffic split must be between {ExperimentRules.MinSplit} and {ExperimentRules.MaxSplit}: both variants need traffic.");
        }

        var existing = await experiments.Get()
            .WhereEquals(nameof(XpSearchExperimentInfo.ExperimentIndexName), indexName)
            .WhereNotEquals(nameof(XpSearchExperimentInfo.ExperimentState), (int)ExperimentState.Concluded)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (existing.Any())
        {
            throw new InvalidOperationException($"The index '{indexName}' already has an experiment that is not concluded. Conclude it first.");
        }

        var experiment = new XpSearchExperimentInfo
        {
            ExperimentGuid = Guid.NewGuid(),
            ExperimentIndexName = indexName,
            ExperimentDisplayName = displayName ?? string.Empty,
            ExperimentSplitPercent = splitPercent,
            ExperimentState = (int)ExperimentState.Draft,
            ExperimentConcludedOutcome = (int)ExperimentOutcome.None
        };

        experiments.Set(experiment);

        await CloneLiveTuningAsync(indexName, experiment.ExperimentID, cancellationToken).ConfigureAwait(false);

        return experiment;
    }

    /// <inheritdoc />
    public async Task SetSplitAsync(int experimentId, int splitPercent, CancellationToken cancellationToken)
    {
        var experiment = Require(experimentId);

        if (!ExperimentRules.CanChangeSplit((ExperimentState)experiment.ExperimentState))
        {
            throw new InvalidOperationException("The traffic split of an experiment that has started cannot be changed.");
        }

        if (!ExperimentRules.IsValidSplit(splitPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(splitPercent),
                splitPercent,
                $"The traffic split must be between {ExperimentRules.MinSplit} and {ExperimentRules.MaxSplit}: both variants need traffic.");
        }

        experiment.ExperimentSplitPercent = splitPercent;
        experiments.Set(experiment);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAsync(int experimentId, CancellationToken cancellationToken)
    {
        var experiment = Require(experimentId);

        if (!ExperimentRules.CanStart((ExperimentState)experiment.ExperimentState))
        {
            throw new InvalidOperationException("Only a draft experiment can be started.");
        }

        experiment.ExperimentState = (int)ExperimentState.Running;
        experiment.ExperimentStarted = time.GetUtcNow().UtcDateTime;

        experiments.Set(experiment);

        // Half the traffic is about to be answered from the draft tuning; entries cached from before
        // the split are not valid for it.
        cache.Evict(experiment.ExperimentIndexName);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ConcludeAsync(int experimentId, bool promote, CancellationToken cancellationToken)
    {
        var experiment = Require(experimentId);

        if (!ExperimentRules.CanConclude((ExperimentState)experiment.ExperimentState))
        {
            throw new InvalidOperationException("Only a running experiment can be concluded.");
        }

        if (promote)
        {
            await PromoteAsync(experiment, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeleteVariantRowsAsync(experiment.ExperimentID, cancellationToken).ConfigureAwait(false);
        }

        experiment.ExperimentState = (int)ExperimentState.Concluded;
        experiment.ExperimentEnded = time.GetUtcNow().UtcDateTime;
        experiment.ExperimentConcludedOutcome = (int)(promote ? ExperimentOutcome.Promoted : ExperimentOutcome.Discarded);

        experiments.Set(experiment);

        cache.Evict(experiment.ExperimentIndexName);
    }

    private XpSearchExperimentInfo Require(int experimentId) =>
        experiments.Get(experimentId) ?? throw new InvalidOperationException($"Experiment {experimentId} does not exist.");

    /// <summary>Copies every live tuning row of the index into a variant-B row of the experiment.</summary>
    private async Task CloneLiveTuningAsync(string indexName, int experimentId, CancellationToken cancellationToken)
    {
        foreach (var row in await LiveAsync(rules, nameof(XpSearchRuleInfo.RuleIndexName), nameof(XpSearchRuleInfo.RuleExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            var copy = (XpSearchRuleInfo)row.Clone();
            copy.RuleID = 0;
            copy.RuleGuid = Guid.NewGuid();
            copy.RuleExperimentID = experimentId;
            rules.Set(copy);
        }

        foreach (var row in await LiveAsync(synonyms, nameof(XpSearchSynonymInfo.SynonymIndexName), nameof(XpSearchSynonymInfo.SynonymExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            var copy = (XpSearchSynonymInfo)row.Clone();
            copy.SynonymID = 0;
            copy.SynonymGuid = Guid.NewGuid();
            copy.SynonymExperimentID = experimentId;
            synonyms.Set(copy);
        }

        foreach (var row in await LiveAsync(stopwords, nameof(XpSearchStopwordListInfo.StopwordListIndexName), nameof(XpSearchStopwordListInfo.StopwordListExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            var copy = (XpSearchStopwordListInfo)row.Clone();
            copy.StopwordListID = 0;
            copy.StopwordListGuid = Guid.NewGuid();
            copy.StopwordListExperimentID = experimentId;
            stopwords.Set(copy);
        }

        foreach (var row in await LiveAsync(weights, nameof(XpSearchFieldWeightInfo.WeightIndexName), nameof(XpSearchFieldWeightInfo.WeightExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            var copy = (XpSearchFieldWeightInfo)row.Clone();
            copy.WeightID = 0;
            copy.WeightGuid = Guid.NewGuid();
            copy.WeightExperimentID = experimentId;
            weights.Set(copy);
        }
    }

    /// <summary>Deletes the index's live rows and turns the experiment's rows into the live ones.</summary>
    private async Task PromoteAsync(XpSearchExperimentInfo experiment, CancellationToken cancellationToken)
    {
        string indexName = experiment.ExperimentIndexName;

        foreach (var row in await LiveAsync(rules, nameof(XpSearchRuleInfo.RuleIndexName), nameof(XpSearchRuleInfo.RuleExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            rules.Delete(row);
        }

        foreach (var row in await LiveAsync(synonyms, nameof(XpSearchSynonymInfo.SynonymIndexName), nameof(XpSearchSynonymInfo.SynonymExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            synonyms.Delete(row);
        }

        foreach (var row in await LiveAsync(stopwords, nameof(XpSearchStopwordListInfo.StopwordListIndexName), nameof(XpSearchStopwordListInfo.StopwordListExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            stopwords.Delete(row);
        }

        foreach (var row in await LiveAsync(weights, nameof(XpSearchFieldWeightInfo.WeightIndexName), nameof(XpSearchFieldWeightInfo.WeightExperimentID), indexName, cancellationToken).ConfigureAwait(false))
        {
            weights.Delete(row);
        }

        foreach (var row in await VariantAsync(rules, nameof(XpSearchRuleInfo.RuleExperimentID), experiment.ExperimentID, cancellationToken).ConfigureAwait(false))
        {
            row.RuleExperimentID = null;
            rules.Set(row);
        }

        foreach (var row in await VariantAsync(synonyms, nameof(XpSearchSynonymInfo.SynonymExperimentID), experiment.ExperimentID, cancellationToken).ConfigureAwait(false))
        {
            row.SynonymExperimentID = null;
            synonyms.Set(row);
        }

        foreach (var row in await VariantAsync(stopwords, nameof(XpSearchStopwordListInfo.StopwordListExperimentID), experiment.ExperimentID, cancellationToken).ConfigureAwait(false))
        {
            row.StopwordListExperimentID = null;
            stopwords.Set(row);
        }

        foreach (var row in await VariantAsync(weights, nameof(XpSearchFieldWeightInfo.WeightExperimentID), experiment.ExperimentID, cancellationToken).ConfigureAwait(false))
        {
            row.WeightExperimentID = null;
            weights.Set(row);
        }
    }

    /// <summary>Deletes every tuning row of one experiment's variant B.</summary>
    private async Task DeleteVariantRowsAsync(int experimentId, CancellationToken cancellationToken)
    {
        foreach (var row in await VariantAsync(rules, nameof(XpSearchRuleInfo.RuleExperimentID), experimentId, cancellationToken).ConfigureAwait(false))
        {
            rules.Delete(row);
        }

        foreach (var row in await VariantAsync(synonyms, nameof(XpSearchSynonymInfo.SynonymExperimentID), experimentId, cancellationToken).ConfigureAwait(false))
        {
            synonyms.Delete(row);
        }

        foreach (var row in await VariantAsync(stopwords, nameof(XpSearchStopwordListInfo.StopwordListExperimentID), experimentId, cancellationToken).ConfigureAwait(false))
        {
            stopwords.Delete(row);
        }

        foreach (var row in await VariantAsync(weights, nameof(XpSearchFieldWeightInfo.WeightExperimentID), experimentId, cancellationToken).ConfigureAwait(false))
        {
            weights.Delete(row);
        }
    }

    private static async Task<IEnumerable<TInfo>> LiveAsync<TInfo>(
        IInfoProvider<TInfo> provider,
        string indexColumn,
        string experimentColumn,
        string indexName,
        CancellationToken cancellationToken)
        where TInfo : AbstractInfoBase<TInfo>, new() =>
        await provider.Get()
            .WhereEquals(indexColumn, indexName)
            .Where(VariantScope.Condition(experimentColumn, TuningVariant.Live))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    private static async Task<IEnumerable<TInfo>> VariantAsync<TInfo>(
        IInfoProvider<TInfo> provider,
        string experimentColumn,
        int experimentId,
        CancellationToken cancellationToken)
        where TInfo : AbstractInfoBase<TInfo>, new() =>
        await provider.Get()
            .Where(VariantScope.Condition(experimentColumn, new TuningVariant(experimentId)))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
}
