using System.Text.Json;
using System.Text.Json.Serialization;

using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;

namespace XpSearch.Admin.UIPages.QueryTester;

/// <summary>What the client asks the query tester to run (spec §8.4).</summary>
public sealed class QueryTesterRequest
{
    /// <summary>Gets or sets the query to test. An empty query matches everything.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Gets or sets the language code to restrict the search to, or an empty string for none.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Gets or sets how many results each side shows. Clamped server-side to 1..50.</summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the code name of the contact group to simulate, or an empty string to run as the
    /// admin's own contact would (ADR-0021).
    /// </summary>
    public string ContactGroup { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to answer from the variant-B tuning of the index's
    /// unfinished experiment rather than from the live tuning (XP-1).
    /// </summary>
    public bool VariantB { get; set; }
}

/// <summary>Which result a "Pin for this query" action was invoked on (QT-2).</summary>
public sealed class PinResultRequest
{
    /// <summary>Gets or sets the query the tester ran, which becomes the rule's condition.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Gets or sets the result id to pin.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-based position to pin the result to.</summary>
    public int Position { get; set; } = 1;
}

/// <summary>Which result a "Bury for this query" action was invoked on (QT-2).</summary>
public sealed class BuryResultRequest
{
    /// <summary>Gets or sets the query the tester ran, which becomes the rule's condition.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Gets or sets the result id to bury.</summary>
    public string TargetId { get; set; } = string.Empty;
}

/// <summary>Which rule an "Open rule" action was invoked on (QT-2).</summary>
public sealed class OpenRuleRequest
{
    /// <summary>Gets or sets the identifier of the rule to open.</summary>
    public int RuleId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run the rule came from used the experiment's
    /// variant-B tuning (XP-1). Those rules are edited from the experiment section, not from here.
    /// </summary>
    public bool VariantB { get; set; }
}

/// <summary>How a result moved when the relevance tuning was applied (spec §8.4).</summary>
/// <remarks>Serialized by name, so the client template switches on "MovedUp" rather than on an ordinal.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResultChange
{
    /// <summary>The result is in the same position on both sides.</summary>
    Unchanged,

    /// <summary>A rule moved the result up - a pin, or a boost above 1.</summary>
    MovedUp,

    /// <summary>A rule moved the result down - a bury, or a boost below 1.</summary>
    MovedDown,

    /// <summary>The result only appears with the rules applied - a pin injected it, or a boost lifted it onto the page.</summary>
    Injected,

    /// <summary>The result only appears without the rules - a bury or a filter rule removed it.</summary>
    Removed
}

/// <summary>One result of one side of the comparison.</summary>
/// <param name="Id">The document's result id.</param>
/// <param name="Title">The <c>title</c> attribute, or an empty string when the index has none.</param>
/// <param name="Url">The <c>url</c> attribute, or an empty string when the index has none.</param>
/// <param name="Score">The final score, after boosts and rules.</param>
/// <param name="Position">The one-based position in this side's ranking.</param>
/// <param name="BaseScore">The Lucene score before any boost rule.</param>
/// <param name="Boosts">The boosts and rules that applied to this hit alone, in application order.</param>
/// <param name="Steps">The score after each scoring stage, in application order (QT-2). Empty when the run carried no breakdown.</param>
/// <param name="Rules">The tuning rules that changed this hit's score or position (QT-2).</param>
/// <param name="Change">How the hit differs from the other side.</param>
public sealed record QueryTesterHit(
    string Id,
    string Title,
    string Url,
    double Score,
    int Position,
    double BaseScore,
    IReadOnlyList<string> Boosts,
    IReadOnlyList<ScoreStep> Steps,
    IReadOnlyList<HitRule> Rules,
    ResultChange Change);

/// <summary>One tuning rule that touched a hit, as the row detail panel lists it (QT-2).</summary>
/// <param name="Id">Identifier of the rule, which the "Open rule" action navigates by.</param>
/// <param name="Name">Display name of the rule.</param>
/// <param name="Effect">What it did: <c>boost</c>, <c>pin</c>, <c>bury</c> or <c>hide</c>.</param>
public sealed record HitRule(int Id, string Name, string Effect);

/// <summary>One side of the query tester: the results as that side ranks them.</summary>
/// <param name="Hits">The hits, in ranked order.</param>
/// <param name="Total">How many documents matched, across all pages.</param>
/// <param name="TookMs">How long the search took, in milliseconds.</param>
/// <param name="QueryExplanations">The rewrite of the query: synonyms, stopwords, weights and query-time rules.</param>
public sealed record QueryTesterSide(
    IReadOnlyList<QueryTesterHit> Hits,
    int Total,
    long TookMs,
    IReadOnlyList<string> QueryExplanations);

/// <summary>What the query tester sends back to the client.</summary>
/// <param name="WithRules">The results as a visitor would see them.</param>
/// <param name="WithoutRules">The results as they would be with no rules, synonyms, stopwords or weights.</param>
/// <param name="Error">A message to show instead of the results, or an empty string when the run succeeded.</param>
public sealed record QueryTesterResult(QueryTesterSide WithRules, QueryTesterSide WithoutRules, string Error)
{
    /// <summary>Gets the result of a run that could not execute.</summary>
    /// <param name="message">What to tell the user.</param>
    /// <returns>An empty result carrying the message.</returns>
    public static QueryTesterResult Failed(string message) =>
        new(new QueryTesterSide([], 0, 0, []), new QueryTesterSide([], 0, 0, []), message);
}

/// <summary>
/// Turns two search responses into the two marked lists the query tester renders. Pure, so the
/// marking §8.4 asks for is unit-testable without an index.
/// </summary>
public static class QueryTesterDiff
{
    /// <summary>Name of the attribute holding a result's display title.</summary>
    public const string TitleAttribute = "title";

    /// <summary>Name of the attribute holding a result's link.</summary>
    public const string UrlAttribute = "url";

    /// <summary>Builds the two sides and marks how each hit differs from the other side.</summary>
    /// <param name="withRules">The run with the index's relevance tuning applied.</param>
    /// <param name="withoutRules">The run with no tuning at all.</param>
    /// <returns>Both sides, marked.</returns>
    public static QueryTesterResult Compare(QueryTesterSideResult withRules, QueryTesterSideResult withoutRules)
    {
        ArgumentNullException.ThrowIfNull(withRules);
        ArgumentNullException.ThrowIfNull(withoutRules);

        var tuned = Hits(withRules);
        var plain = Hits(withoutRules);

        var tunedPositions = tuned.ToDictionary(hit => hit.Id, hit => hit.Position, StringComparer.Ordinal);
        var plainPositions = plain.ToDictionary(hit => hit.Id, hit => hit.Position, StringComparer.Ordinal);

        return new QueryTesterResult(
            new QueryTesterSide(
                [.. tuned.Select(hit => hit with { Change = Classify(hit, plainPositions, ResultChange.Injected) })],
                (int)withRules.Response.Total,
                withRules.Response.TookMs,
                withRules.QueryExplanations),
            new QueryTesterSide(
                [.. plain.Select(hit => hit with { Change = Classify(hit, tunedPositions, ResultChange.Removed) })],
                (int)withoutRules.Response.Total,
                withoutRules.Response.TookMs,
                withoutRules.QueryExplanations),
            string.Empty);
    }

    /// <summary>Marks one hit against the other side's ranking.</summary>
    /// <param name="hit">The hit to mark.</param>
    /// <param name="other">Positions of the other side's hits, keyed by result id.</param>
    /// <param name="missing">
    /// What to mark a hit the other side does not hold: <see cref="ResultChange.Injected"/> when
    /// marking the with-rules side, <see cref="ResultChange.Removed"/> when marking the other.
    /// </param>
    /// <returns>The marking.</returns>
    /// <remarks>
    /// "Up" always means "the rules moved it up", so both columns tell the same story: on the
    /// without-rules side a hit the rules lifted is the one with the higher position number.
    /// </remarks>
    private static ResultChange Classify(QueryTesterHit hit, IReadOnlyDictionary<string, int> other, ResultChange missing)
    {
        if (!other.TryGetValue(hit.Id, out int otherPosition))
        {
            return missing;
        }

        if (otherPosition == hit.Position)
        {
            return ResultChange.Unchanged;
        }

        bool up = missing == ResultChange.Injected ? otherPosition > hit.Position : hit.Position > otherPosition;

        return up ? ResultChange.MovedUp : ResultChange.MovedDown;
    }

    /// <summary>Reads a string attribute off a result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="attribute">Name of the attribute.</param>
    /// <returns>The value, or an empty string when the index does not project it.</returns>
    public static string Attribute(Result result, string attribute)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Attributes is null || !result.Attributes.TryGetValue(attribute, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    /// <summary>The rules the pipeline recorded against one hit, in the order they applied (QT-2).</summary>
    private static IReadOnlyList<HitRule> Rules(QueryTesterSideResult side, string id) =>
        side.AppliedRules.TryGetValue(id, out var applied)
            ? [.. applied.Select(rule => new HitRule(rule.RuleId, rule.Name, rule.Effect))]
            : [];

    private static List<QueryTesterHit> Hits(QueryTesterSideResult side)
    {
        var results = side.Response.Results ?? [];
        int queryLevel = side.QueryExplanations.Count;

        return
        [
            .. results.Select((result, index) => new QueryTesterHit(
                result.Id ?? string.Empty,
                Attribute(result, TitleAttribute),
                Attribute(result, UrlAttribute),
                result.Score ?? 0,
                (int)(result.Ranking?.Position ?? index + 1),
                // Since QT-2 this is the score before any boost, which is what the diff's "vs base"
                // column means; a response without a breakdown falls back to the final score.
                result.Ranking?.BaseScore ?? result.Score ?? 0,
                // The response prepends the query-level lines to every hit's boosts; the rest is
                // what applied to this hit alone (XpSearch.Core ProjectResponseStage).
                [.. (result.Ranking?.Boosts ?? []).Skip(queryLevel)],
                [.. (result.Ranking?.Steps ?? []).Select(step => new ScoreStep(step.Stage, step.Score))],
                Rules(side, result.Id ?? string.Empty),
                ResultChange.Unchanged))
        ];
    }
}
