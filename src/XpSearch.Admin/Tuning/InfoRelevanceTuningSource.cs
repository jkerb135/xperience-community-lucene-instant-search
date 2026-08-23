using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tuning;

/// <summary>
/// Reads relevance tuning from the module classes the Search tuning application writes, through one
/// cache entry per index (spec §8.5: "Never hit the database per search request").
/// </summary>
/// <remarks>
/// Caching uses <c>IProgressiveCache</c>
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/data-caching)
/// with a dependency on all four object types
/// (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies),
/// so saving a rule in the admin UI touches the dummy key <c>xpsearch.rule|all</c> and the next query
/// reloads. That is the same "object change invalidates the cache" contract as an explicit object
/// event handler, minus the handler.
/// </remarks>
public sealed class InfoRelevanceTuningSource : IRelevanceTuningSource
{
    /// <summary>How long a tuning entry survives without a change touching it.</summary>
    public const int CacheMinutes = 30;

    private readonly IInfoProvider<XpSearchRuleInfo> rules;
    private readonly IInfoProvider<XpSearchSynonymInfo> synonyms;
    private readonly IInfoProvider<XpSearchStopwordListInfo> stopwords;
    private readonly IInfoProvider<XpSearchFieldWeightInfo> weights;
    private readonly IProgressiveCache cache;
    private readonly ICacheDependencyBuilderFactory dependencies;

    /// <summary>Initializes a new instance of the <see cref="InfoRelevanceTuningSource"/> class.</summary>
    /// <param name="rules">Provider of rule objects.</param>
    /// <param name="synonyms">Provider of synonym objects.</param>
    /// <param name="stopwords">Provider of stopword list objects.</param>
    /// <param name="weights">Provider of field weight objects.</param>
    /// <param name="cache">The progressive cache.</param>
    /// <param name="dependencies">Factory of cache dependency builders.</param>
    public InfoRelevanceTuningSource(
        IInfoProvider<XpSearchRuleInfo> rules,
        IInfoProvider<XpSearchSynonymInfo> synonyms,
        IInfoProvider<XpSearchStopwordListInfo> stopwords,
        IInfoProvider<XpSearchFieldWeightInfo> weights,
        IProgressiveCache cache,
        ICacheDependencyBuilderFactory dependencies)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(synonyms);
        ArgumentNullException.ThrowIfNull(stopwords);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(dependencies);

        this.rules = rules;
        this.synonyms = synonyms;
        this.stopwords = stopwords;
        this.weights = weights;
        this.cache = cache;
        this.dependencies = dependencies;
    }

    /// <summary>
    /// The object types a cached tuning entry depends on. Adding a fifth kind of tuning data without
    /// adding it here would leave a stale cache, so the list is public and asserted in tests.
    /// </summary>
    public static IReadOnlyList<string> DependencyObjectTypes { get; } =
    [
        XpSearchRuleInfo.OBJECT_TYPE,
        XpSearchSynonymInfo.OBJECT_TYPE,
        XpSearchStopwordListInfo.OBJECT_TYPE,
        XpSearchFieldWeightInfo.OBJECT_TYPE
    ];

    /// <summary>Builds the cache key parts of one index's tuning entry.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="part">Which kind of tuning data is being cached.</param>
    /// <returns>The parts, which the cache joins with <c>|</c>.</returns>
    public static string[] CacheKeyParts(string indexName, string part) =>
        ["xpsearch", "tuning", part, indexName ?? string.Empty];

    /// <inheritdoc />
    public Task<IReadOnlyList<TuningRule>> GetRulesAsync(string indexName, CancellationToken cancellationToken) =>
        LoadAsync(indexName, "rules", cancellationToken, async token =>
        {
            var rows = await rules.Get()
                .WhereEquals(nameof(XpSearchRuleInfo.RuleIndexName), indexName)
                .GetEnumerableTypedResultAsync(cancellationToken: token)
                .ConfigureAwait(false);

            return (IReadOnlyList<TuningRule>)
            [
                .. rows.Select(row => new TuningRule(
                    row.RuleID,
                    row.RuleName,
                    row.RuleEnabled,
                    (RuleCondition)row.RuleConditionType,
                    row.RulePattern,
                    (RuleConsequence)row.RuleConsequenceType,
                    row.RuleTargetObjectID,
                    row.RuleTargetPosition,
                    (double)row.RuleBoostValue,
                    row.RuleFilterExpression,
                    row.RuleRedirectUrl,
                    row.RuleValidFrom,
                    row.RuleValidTo,
                    row.RulePriority,
                    row.RuleContactGroup))
            ];
        });

    /// <inheritdoc />
    public Task<IReadOnlyList<TuningSynonym>> GetSynonymsAsync(string indexName, CancellationToken cancellationToken) =>
        LoadAsync(indexName, "synonyms", cancellationToken, async token =>
        {
            var rows = await synonyms.Get()
                .WhereEquals(nameof(XpSearchSynonymInfo.SynonymIndexName), indexName)
                .WhereTrue(nameof(XpSearchSynonymInfo.SynonymEnabled))
                .GetEnumerableTypedResultAsync(cancellationToken: token)
                .ConfigureAwait(false);

            return (IReadOnlyList<TuningSynonym>)
            [
                .. rows
                    .Select(row => new TuningSynonym(
                        (SynonymDirection)row.SynonymType,
                        SynonymExpansion.SplitTerms(row.SynonymInput),
                        SynonymExpansion.SplitTerms(row.SynonymOutput)))
                    .Where(synonym => synonym.Input.Count > 0)
            ];
        });

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStopwordsAsync(string indexName, CancellationToken cancellationToken) =>
        LoadAsync(indexName, "stopwords", cancellationToken, async token =>
        {
            var rows = await stopwords.Get()
                .WhereEquals(nameof(XpSearchStopwordListInfo.StopwordListIndexName), indexName)
                .TopN(1)
                .GetEnumerableTypedResultAsync(cancellationToken: token)
                .ConfigureAwait(false);

            return (IReadOnlyList<string>)SplitStopwords(rows.FirstOrDefault()?.StopwordListWords);
        });

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldWeight>> GetFieldWeightsAsync(string indexName, CancellationToken cancellationToken) =>
        LoadAsync(indexName, "weights", cancellationToken, async token =>
        {
            var rows = await weights.Get()
                .WhereEquals(nameof(XpSearchFieldWeightInfo.WeightIndexName), indexName)
                .GetEnumerableTypedResultAsync(cancellationToken: token)
                .ConfigureAwait(false);

            return (IReadOnlyList<FieldWeight>)
            [
                .. rows.Select(row => new FieldWeight(row.WeightFieldName, (double)row.WeightValue))
            ];
        });

    /// <summary>Splits a stopword list as it is stored: one word per line, blanks ignored.</summary>
    /// <param name="value">The stored text.</param>
    /// <returns>The stopwords, trimmed and lowercased.</returns>
    public static IReadOnlyList<string> SplitStopwords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return
        [
            .. value
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
        ];
    }

    private Task<TResult> LoadAsync<TResult>(
        string indexName,
        string part,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<TResult>> load) =>
        cache.LoadAsync(
            async settings =>
            {
                settings.CacheDependency = dependencies.Create()
                    .ForInfoObjects<XpSearchRuleInfo>().All().Builder()
                    .ForInfoObjects<XpSearchSynonymInfo>().All().Builder()
                    .ForInfoObjects<XpSearchStopwordListInfo>().All().Builder()
                    .ForInfoObjects<XpSearchFieldWeightInfo>().All().Builder()
                    .Build();

                return await load(cancellationToken).ConfigureAwait(false);
            },
            new CacheSettings(CacheMinutes, CacheKeyParts(indexName, part)));
}
