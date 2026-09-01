using CMS.DataEngine;
using CMS.Helpers;

namespace XpSearch.Core.Fuzzy;

/// <summary>
/// Reads the typo tolerance setting from <see cref="XpSearchFuzzyIndexInfo"/>, behind one cache entry
/// per index so a search never queries it (FZ-1).
/// </summary>
/// <remarks>
/// The entry depends on the object type, which touches its dummy cache keys, so the admin toggle
/// invalidates it at once
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
/// An index with no row - every index until someone turns the setting on - reads as off.
/// </remarks>
public sealed class InfoTypoToleranceSource : ITypoToleranceSource
{
    /// <summary>How long a setting entry survives without a change touching it.</summary>
    public const int CacheMinutes = 30;

    private readonly IInfoProvider<XpSearchFuzzyIndexInfo> indexes;
    private readonly IProgressiveCache cache;
    private readonly ICacheDependencyBuilderFactory dependencies;

    /// <summary>Initializes a new instance of the <see cref="InfoTypoToleranceSource"/> class.</summary>
    /// <param name="indexes">Provider of the per-index settings rows.</param>
    /// <param name="cache">The progressive cache.</param>
    /// <param name="dependencies">Factory of cache dependency builders.</param>
    public InfoTypoToleranceSource(
        IInfoProvider<XpSearchFuzzyIndexInfo> indexes,
        IProgressiveCache cache,
        ICacheDependencyBuilderFactory dependencies)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(dependencies);

        this.indexes = indexes;
        this.cache = cache;
        this.dependencies = dependencies;
    }

    /// <summary>Builds the cache key parts of one index's setting entry.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The parts, which the cache joins with <c>|</c>.</returns>
    public static string[] CacheKeyParts(string indexName) => ["xpsearch", "fuzzy", indexName ?? string.Empty];

    /// <inheritdoc />
    public Task<bool> IsEnabledAsync(string indexName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return Task.FromResult(false);
        }

        return cache.LoadAsync(
            async settings =>
            {
                settings.CacheDependency = dependencies.Create()
                    .ForInfoObjects<XpSearchFuzzyIndexInfo>().All().Builder()
                    .Build();

                var rows = await indexes.Get()
                    .WhereEquals(nameof(XpSearchFuzzyIndexInfo.FuzzyIndexName), indexName)
                    .TopN(1)
                    .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return rows.FirstOrDefault()?.FuzzyIndexEnabled ?? false;
            },
            new CacheSettings(CacheMinutes, CacheKeyParts(indexName)));
    }
}
