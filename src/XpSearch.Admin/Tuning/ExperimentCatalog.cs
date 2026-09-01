using CMS.DataEngine;

using XpSearch.Admin.Persistence;

namespace XpSearch.Admin.Tuning;

/// <summary>One experiment as the admin pages need to know it (XP-1).</summary>
/// <param name="Id">Database identifier; the variant-B tuning rows carry it.</param>
/// <param name="IndexName">Code name of the index it tests.</param>
/// <param name="DisplayName">What the editor called it.</param>
/// <param name="SplitPercent">Percentage of traffic sent to variant B.</param>
/// <param name="State">Draft, Running or Concluded.</param>
/// <param name="Outcome">How it ended, or <see cref="ExperimentOutcome.None"/> while it has not.</param>
/// <param name="Started">When it started splitting traffic, in UTC, or <see langword="null"/>.</param>
/// <param name="Ended">When it was concluded, in UTC, or <see langword="null"/>.</param>
public sealed record ExperimentSummary(
    int Id,
    string IndexName,
    string DisplayName,
    int SplitPercent,
    ExperimentState State,
    ExperimentOutcome Outcome,
    DateTime? Started,
    DateTime? Ended);

/// <summary>
/// Reads stored experiments for the admin pages (XP-1). A seam of its own so a page can be exercised
/// without a database: an <c>Info</c> object cannot be built outside Kentico's container, and the
/// "which experiment is unfinished" lookup is a query.
/// </summary>
public interface IExperimentCatalog
{
    /// <summary>Gets one experiment.</summary>
    /// <param name="experimentId">Identifier of the experiment.</param>
    /// <returns>The experiment, or <see langword="null"/> when there is no such row.</returns>
    ExperimentSummary? Get(int experimentId);

    /// <summary>Gets the index's draft or running experiment.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The experiment, or <see langword="null"/> when the index has none.</returns>
    Task<ExperimentSummary?> GetUnfinishedAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>The default catalog, over the stored experiments.</summary>
public sealed class ExperimentCatalog : IExperimentCatalog
{
    private readonly IInfoProvider<XpSearchExperimentInfo> experiments;

    /// <summary>Initializes a new instance of the <see cref="ExperimentCatalog"/> class.</summary>
    /// <param name="experiments">Provider of experiment objects.</param>
    public ExperimentCatalog(IInfoProvider<XpSearchExperimentInfo> experiments)
    {
        ArgumentNullException.ThrowIfNull(experiments);

        this.experiments = experiments;
    }

    /// <summary>Reads one stored row into the summary the pages use.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The summary.</returns>
    public static ExperimentSummary Read(XpSearchExperimentInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new ExperimentSummary(
            row.ExperimentID,
            row.ExperimentIndexName,
            row.ExperimentDisplayName,
            row.ExperimentSplitPercent,
            (ExperimentState)row.ExperimentState,
            (ExperimentOutcome)row.ExperimentConcludedOutcome,
            row.ExperimentStarted,
            row.ExperimentEnded);
    }

    /// <inheritdoc />
    public ExperimentSummary? Get(int experimentId) =>
        experimentId > 0 && experiments.Get(experimentId) is { } row ? Read(row) : null;

    /// <inheritdoc />
    public async Task<ExperimentSummary?> GetUnfinishedAsync(string indexName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return null;
        }

        var rows = await experiments.Get()
            .WhereEquals(nameof(XpSearchExperimentInfo.ExperimentIndexName), indexName)
            .WhereNotEquals(nameof(XpSearchExperimentInfo.ExperimentState), (int)ExperimentState.Concluded)
            .TopN(1)
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.FirstOrDefault() is { } row ? Read(row) : null;
    }
}
