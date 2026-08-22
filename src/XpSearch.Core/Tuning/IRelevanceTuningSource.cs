namespace XpSearch.Core.Tuning;

/// <summary>How a rule's pattern is compared against the normalized query (spec §8.2).</summary>
public enum RuleCondition
{
    /// <summary>The query contains the pattern.</summary>
    Contains,

    /// <summary>The query equals the pattern.</summary>
    Exact,

    /// <summary>The query starts with the pattern.</summary>
    StartsWith,

    /// <summary>The rule applies to every query, whatever the pattern.</summary>
    Always
}

/// <summary>What a matching rule does (spec §8.2).</summary>
public enum RuleConsequence
{
    /// <summary>Move a document to a fixed position.</summary>
    Pin,

    /// <summary>Push a document out of the results.</summary>
    Bury,

    /// <summary>Raise the score of a document or of a group of documents.</summary>
    Boost,

    /// <summary>Restrict the results to documents matching an expression.</summary>
    Filter,

    /// <summary>Send the visitor to a URL instead of showing results. Not surfaced; see ADR-0014.</summary>
    Redirect
}

/// <summary>Whether a synonym expands in both directions or only from input to output (spec §8.2).</summary>
public enum SynonymDirection
{
    /// <summary>Every listed term expands to every other listed term.</summary>
    TwoWay,

    /// <summary>Each input term expands to the output terms, but not the other way round.</summary>
    OneWay
}

/// <summary>
/// One relevance rule, as stored by the Search tuning application (spec §8.2).
/// </summary>
/// <param name="Id">Database identifier; the tie-breaker of the precedence order.</param>
/// <param name="Name">Display name, echoed in the <c>ranking.boosts</c> explanation.</param>
/// <param name="Enabled">Whether the rule is considered at all.</param>
/// <param name="Condition">How <paramref name="Pattern"/> is matched.</param>
/// <param name="Pattern">The query pattern, compared case-insensitively.</param>
/// <param name="Consequence">What the rule does when it matches.</param>
/// <param name="TargetId">Result id of the document to pin, bury or boost.</param>
/// <param name="TargetPosition">One-based position for a pin.</param>
/// <param name="BoostValue">Score multiplier for a boost.</param>
/// <param name="FilterExpression">Comma-separated <c>field:value</c> pairs for a filter or an untargeted boost.</param>
/// <param name="RedirectUrl">Destination of a redirect rule.</param>
/// <param name="ValidFrom">First moment the rule applies, in UTC. Null means "already".</param>
/// <param name="ValidTo">Last moment the rule applies, in UTC. Null means "forever".</param>
/// <param name="Priority">Conflict resolution order; lower runs first.</param>
public sealed record TuningRule(
    int Id,
    string Name,
    bool Enabled,
    RuleCondition Condition,
    string Pattern,
    RuleConsequence Consequence,
    string TargetId,
    int TargetPosition,
    double BoostValue,
    string FilterExpression,
    string RedirectUrl,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    int Priority);

/// <summary>One synonym group (spec §8.2).</summary>
/// <param name="Direction">Whether the group expands both ways.</param>
/// <param name="Input">The terms or phrases that trigger the expansion, already lowercased and trimmed.</param>
/// <param name="Output">What a one-way group expands to. Empty for a two-way group.</param>
public sealed record TuningSynonym(SynonymDirection Direction, IReadOnlyList<string> Input, IReadOnlyList<string> Output);

/// <summary>One per-field score multiplier (spec §8.2).</summary>
/// <param name="Field">Name of the schema field.</param>
/// <param name="Weight">The multiplier; 1.0 changes nothing.</param>
public sealed record FieldWeight(string Field, double Weight);

/// <summary>
/// Everything the tuning stages need for one index, loaded once per request by
/// <c>SynonymExpansionStage</c> and read by the later stages.
/// </summary>
/// <param name="Rules">Rules that are enabled, in schedule and matching the query, in precedence order.</param>
/// <param name="Synonyms">Enabled synonym groups.</param>
/// <param name="Stopwords">Words removed from the query before it is parsed.</param>
/// <param name="FieldWeights">Per-field score multipliers, keyed by schema field name.</param>
public sealed record TuningSet(
    IReadOnlyList<TuningRule> Rules,
    IReadOnlyList<TuningSynonym> Synonyms,
    IReadOnlyList<string> Stopwords,
    IReadOnlyDictionary<string, double> FieldWeights)
{
    /// <summary>Gets the tuning set of an index that has no configuration: the behaviour of Core alone.</summary>
    public static TuningSet Empty { get; } = new([], [], [], new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Where the query pipeline reads relevance tuning from (spec §8.3). Core ships an empty
/// implementation so search works without <c>XpSearch.Admin</c> installed (spec §2.2); the Admin
/// package replaces it with the database-backed, cached one.
/// </summary>
public interface IRelevanceTuningSource
{
    /// <summary>Gets every rule configured for an index, enabled or not.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rules.</returns>
    Task<IReadOnlyList<TuningRule>> GetRulesAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>Gets the enabled synonym groups of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synonym groups.</returns>
    Task<IReadOnlyList<TuningSynonym>> GetSynonymsAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>Gets the stopwords of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stopwords, lowercased.</returns>
    Task<IReadOnlyList<string>> GetStopwordsAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>Gets the per-field score multipliers of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The weights.</returns>
    Task<IReadOnlyList<FieldWeight>> GetFieldWeightsAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>
/// The default source: no rules, no synonyms, no stopwords, no weights. Registered by
/// <c>AddXpSearch</c> so the tuning stages are inert until <c>AddXpSearchAdmin</c> replaces it.
/// </summary>
public sealed class EmptyRelevanceTuningSource : IRelevanceTuningSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TuningRule>> GetRulesAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TuningRule>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<TuningSynonym>> GetSynonymsAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TuningSynonym>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStopwordsAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldWeight>> GetFieldWeightsAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FieldWeight>>([]);
}
