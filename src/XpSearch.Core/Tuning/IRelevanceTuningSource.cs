using System.Text.Json.Serialization;

namespace XpSearch.Core.Tuning;

/// <summary>How a rule's pattern is compared against the query (ADR-0022).</summary>
public enum QueryOperator
{
    /// <summary>The query is exactly the pattern.</summary>
    Is,

    /// <summary>The query contains the pattern.</summary>
    Contains,

    /// <summary>The query starts with the pattern.</summary>
    StartsWith
}

/// <summary>
/// The "the visitor's search …" half of a rule's <c>if</c> (ADR-0022).
/// </summary>
/// <param name="Operator">How <paramref name="Pattern"/> is compared against the query.</param>
/// <param name="Pattern">
/// The pattern, compared case-insensitively after trimming. An empty pattern is not a wildcard for
/// <see cref="QueryOperator.Is"/> (it then matches an empty query only), but it is for the other two
/// operators, which is how "any query at all" is expressed.
/// </param>
/// <param name="MatchAnalyzed">
/// <see langword="true"/> to compare against the analyzed query - what the index's analyzer makes of
/// it, so plurals and stems match - with the configured synonyms folded in.
/// <see langword="false"/> compares the raw query text. Neither has any typo tolerance.
/// </param>
public sealed record QueryCondition(QueryOperator Operator, string Pattern, bool MatchAnalyzed);

/// <summary>One <c>attribute is value</c> condition, checked against the request's facet filters.</summary>
/// <param name="Attribute">Attribute name, as it appears in <c>filters.facets</c>.</param>
/// <param name="Value">The value that must be selected on it.</param>
public sealed record AttributeIs(string Attribute, string Value);

/// <summary>
/// The <c>if</c> of a rule: every condition given must hold (ADR-0022). A
/// <see cref="RuleConditions"/> with nothing set at all never fires - a source must not emit one.
/// </summary>
/// <param name="Query">The query condition, or <see langword="null"/> for "any query".</param>
/// <param name="Filters">Facet refinements that must all be selected on the request. Empty for "any".</param>
/// <param name="ContactGroup">Code name of the contact group the rule is scoped to; empty for "anyone" (ADR-0021).</param>
/// <param name="Language">Language the request must ask for; empty for "any language".</param>
public sealed record RuleConditions(
    QueryCondition? Query,
    IReadOnlyList<AttributeIs> Filters,
    string ContactGroup,
    string Language)
{
    /// <summary>Gets a value indicating whether the conditions say nothing, in which case the rule never fires.</summary>
    /// <remarks>Derived, so it is never stored: see <c>XpSearch.Admin.Persistence.RuleJson</c>.</remarks>
    [JsonIgnore]
    public bool IsEmpty =>
        Query is null
        && Filters.Count == 0
        && string.IsNullOrWhiteSpace(ContactGroup)
        && string.IsNullOrWhiteSpace(Language);
}

/// <summary>
/// One <c>then</c> of a rule (ADR-0022). The nested records are the whole closed set; they are
/// applied in the order the rule lists them.
/// </summary>
/// <remarks>
/// The discriminators are the stored contract of the <c>RuleActions</c> column (ADR-0022
/// addendum): renaming one silently reinterprets every stored rule, so they are spelled out here
/// rather than derived from the type name.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Pin), "pin")]
[JsonDerivedType(typeof(Hide), "hide")]
[JsonDerivedType(typeof(Boost), "boost")]
[JsonDerivedType(typeof(Bury), "bury")]
[JsonDerivedType(typeof(FilterResults), "filterResults")]
[JsonDerivedType(typeof(RemoveWord), "removeWord")]
[JsonDerivedType(typeof(ReplaceWord), "replaceWord")]
[JsonDerivedType(typeof(ReplaceQuery), "replaceQuery")]
[JsonDerivedType(typeof(Redirect), "redirect")]
[JsonDerivedType(typeof(CustomData), "customData")]
public abstract record RuleAction
{
    /// <summary>Moves a document to a fixed position.</summary>
    /// <param name="TargetId">Result id of the document.</param>
    /// <param name="Position">One-based position, counted across pages.</param>
    public sealed record Pin(string TargetId, int Position) : RuleAction;

    /// <summary>Removes a document from the results entirely; the total excludes it.</summary>
    /// <param name="TargetId">Result id of the document.</param>
    public sealed record Hide(string TargetId) : RuleAction;

    /// <summary>Raises (or lowers) the score of one document, or of everything an expression selects.</summary>
    /// <param name="TargetId">Result id of the document, or empty to use <paramref name="FilterExpression"/>.</param>
    /// <param name="FilterExpression">Comma-separated <c>attribute:value</c> pairs, used when there is no target id.</param>
    /// <param name="Multiplier">The score multiplier; 1.0 changes nothing, 0 or less disables the rule.</param>
    public sealed record Boost(string TargetId, string FilterExpression, double Multiplier) : RuleAction;

    /// <summary>Pushes a document out of the page that was returned.</summary>
    /// <param name="TargetId">Result id of the document.</param>
    /// <param name="FilterExpression">Reserved for a future group bury; not applied today.</param>
    public sealed record Bury(string TargetId, string FilterExpression) : RuleAction;

    /// <summary>Restricts the results to the documents an expression selects.</summary>
    /// <param name="FilterExpression">Comma-separated <c>attribute:value</c> pairs.</param>
    public sealed record FilterResults(string FilterExpression) : RuleAction;

    /// <summary>Drops a word from the query before it is parsed.</summary>
    /// <param name="Word">The word to remove.</param>
    public sealed record RemoveWord(string Word) : RuleAction;

    /// <summary>Swaps a word in the query for another before it is parsed.</summary>
    /// <param name="Word">The word to replace.</param>
    /// <param name="Replacement">What to put in its place.</param>
    public sealed record ReplaceWord(string Word, string Replacement) : RuleAction;

    /// <summary>Replaces the whole query text before it is parsed.</summary>
    /// <param name="Query">The query to search for instead.</param>
    public sealed record ReplaceQuery(string Query) : RuleAction;

    /// <summary>Sends the visitor to a URL. Surfaced as <c>SearchResponse.redirect</c>; the results are returned alongside it.</summary>
    /// <param name="Url">The destination. A redirect with no URL does nothing.</param>
    public sealed record Redirect(string Url) : RuleAction;

    /// <summary>Attaches editor-authored data to the response, as <c>SearchResponse.ruleData</c>.</summary>
    /// <param name="Json">A JSON object. Anything else is ignored.</param>
    public sealed record CustomData(string Json) : RuleAction;
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
/// One relevance rule: an <c>if</c> of conditions that must all hold and a <c>then</c> of
/// actions applied in order (ADR-0022).
/// </summary>
/// <param name="Id">Database identifier; the tie-breaker of the precedence order.</param>
/// <param name="Name">Display name, echoed in the <c>ranking.boosts</c> explanation.</param>
/// <param name="Enabled">Whether the rule is considered at all.</param>
/// <param name="Priority">Conflict resolution order; lower runs first.</param>
/// <param name="ValidFrom">First moment the rule applies, in UTC. Null means "already".</param>
/// <param name="ValidTo">Last moment the rule applies, in UTC. Null means "forever".</param>
/// <param name="Conditions">The conditions; all of them must hold.</param>
/// <param name="Actions">What the rule does, applied in the order listed.</param>
public sealed record TuningRule(
    int Id,
    string Name,
    bool Enabled,
    int Priority,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    RuleConditions Conditions,
    IReadOnlyList<RuleAction> Actions);

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
/// <param name="Rules">Rules that are enabled, in schedule, in the visitor's contact groups and matching the query, in precedence order.</param>
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
