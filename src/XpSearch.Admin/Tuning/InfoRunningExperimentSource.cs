using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;
using XpSearch.Core.Experiments;

namespace XpSearch.Admin.Tuning;

/// <summary>
/// Reads the running experiment of an index from the module class, through one cache entry per index -
/// this runs on every search request, so it must never be a database round trip (spec §8.5).
/// </summary>
/// <remarks>
/// The entry depends on <see cref="XpSearchExperimentInfo"/>, whose type info touches its dummy cache
/// keys, so starting or concluding an experiment applies to the next request without a restart.
/// </remarks>
public sealed class InfoRunningExperimentSource : IRunningExperimentSource
{
    /// <summary>How long a lookup survives without an experiment change touching it.</summary>
    public const int CacheMinutes = 30;

    private readonly IInfoProvider<XpSearchExperimentInfo> experiments;
    private readonly IProgressiveCache cache;
    private readonly ICacheDependencyBuilderFactory dependencies;

    /// <summary>Initializes a new instance of the <see cref="InfoRunningExperimentSource"/> class.</summary>
    /// <param name="experiments">Provider of experiment objects.</param>
    /// <param name="cache">The progressive cache.</param>
    /// <param name="dependencies">Factory of cache dependency builders.</param>
    public InfoRunningExperimentSource(
        IInfoProvider<XpSearchExperimentInfo> experiments,
        IProgressiveCache cache,
        ICacheDependencyBuilderFactory dependencies)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(dependencies);

        this.experiments = experiments;
        this.cache = cache;
        this.dependencies = dependencies;
    }

    /// <summary>Builds the cache key parts of one index's lookup.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The parts, which the cache joins with <c>|</c>.</returns>
    public static string[] CacheKeyParts(string indexName) =>
        ["xpsearch", "experiment", "running", indexName ?? string.Empty];

    /// <summary>Reads one stored row into the model the pipeline uses.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The running experiment.</returns>
    public static RunningExperiment Read(XpSearchExperimentInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new RunningExperiment(row.ExperimentID, row.ExperimentGuid, row.ExperimentSplitPercent);
    }

    /// <inheritdoc />
    public Task<RunningExperiment?> GetRunningExperimentAsync(string indexName, CancellationToken cancellationToken) =>
        cache.LoadAsync(
            async settings =>
            {
                settings.CacheDependency = dependencies.Create()
                    .ForInfoObjects<XpSearchExperimentInfo>().All().Builder()
                    .Build();

                var rows = await experiments.Get()
                    .WhereEquals(nameof(XpSearchExperimentInfo.ExperimentIndexName), indexName)
                    .WhereEquals(nameof(XpSearchExperimentInfo.ExperimentState), (int)ExperimentState.Running)
                    .TopN(1)
                    .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return rows.FirstOrDefault() is { } row ? Read(row) : null;
            },
            new CacheSettings(CacheMinutes, CacheKeyParts(indexName)));
}
