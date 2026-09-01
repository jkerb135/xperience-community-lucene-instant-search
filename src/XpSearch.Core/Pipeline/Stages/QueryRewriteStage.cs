using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;

using XpSearch.Core.Indexing;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// The first tuning stage (ADR-0022). Loads the index's rules, decides which of them fire - the one
/// place a rule's schedule and conditions are evaluated - and applies the query rewrites of those
/// that did, so synonyms, the query parser and the highlighter all see the rewritten text.
/// </summary>
/// <remarks>
/// <para>
/// It runs before <see cref="SynonymExpansionStage"/> because a rewrite changes the words synonyms
/// are looked up for. It loads the synonyms itself all the same: a condition with
/// <c>matchAnalyzed</c> is compared against the analyzed query with the synonyms folded in, so they
/// are needed to decide which rules fire in the first place. Behind <c>XpSearch.Admin</c> that is one
/// more cache read, not one more database round trip (spec §8.5).
/// </para>
/// <para>
/// The search activity and the query log record the query the visitor typed, not the rewritten one
/// (ADR-0015, AN-4). What was searched for and what a marketer rewrote it into are two different
/// questions, and the reports answer the first.
/// </para>
/// </remarks>
public sealed class QueryRewriteStage : ISearchStage
{
    private readonly IRelevanceTuningSource source;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="QueryRewriteStage"/> class.</summary>
    /// <param name="source">Where relevance tuning is read from.</param>
    /// <param name="time">Clock used to evaluate rule schedules; substitutable in tests.</param>
    public QueryRewriteStage(IRelevanceTuningSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);

        this.source = source;
        this.time = time;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.QueryRewrite;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string index = context.Request.Index;

        var rules = await source.GetRulesAsync(index, cancellationToken).ConfigureAwait(false);
        var synonyms = await source.GetSynonymsAsync(index, cancellationToken).ConfigureAwait(false);

        var active = RuleSelection.Active(rules, MatchContext(context, synonyms), time.GetUtcNow().UtcDateTime);

        context.Tuning = context.Tuning with { Rules = active };

        Rewrite(context, active);
    }

    /// <summary>Applies the rewrite actions of the fired rules, in rule order then listed order.</summary>
    private static void Rewrite(SearchContext context, IReadOnlyList<TuningRule> rules)
    {
        bool explain = context.Request.Explain ?? false;
        string text = context.QueryText;

        foreach (var rule in rules)
        {
            bool applied = false;

            foreach (var action in rule.Actions)
            {
                string rewritten = action switch
                {
                    RuleAction.RemoveWord remove => ReplaceWord(text, remove.Word, string.Empty),
                    RuleAction.ReplaceWord replace => ReplaceWord(text, replace.Word, replace.Replacement),
                    RuleAction.ReplaceQuery replace => (replace.Query ?? string.Empty).Trim(),
                    _ => text
                };

                applied |= !string.Equals(rewritten, text, StringComparison.Ordinal);
                text = rewritten;
            }

            if (applied && explain)
            {
                context.QueryExplanations.Add(RuleSelection.Explain(rule));
            }
        }

        context.QueryText = text;
    }

    /// <summary>Replaces whole words, case-insensitively. An empty replacement drops the word.</summary>
    private static string ReplaceWord(string text, string? word, string? replacement)
    {
        string target = (word ?? string.Empty).Trim();

        if (target.Length == 0)
        {
            return text;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string with = (replacement ?? string.Empty).Trim();

        return string.Join(
            ' ',
            words
                .Select(current => current.Equals(target, StringComparison.OrdinalIgnoreCase) ? with : current)
                .Where(current => current.Length > 0));
    }

    /// <summary>Builds what the request looks like to a rule's conditions.</summary>
    private static RuleMatchContext MatchContext(SearchContext context, IReadOnlyList<TuningSynonym> synonyms)
    {
        string field = context.Schema.Fields
            .Where(candidate => candidate.Searchable)
            .Select(LuceneFieldNames.SearchFieldName)
            .FirstOrDefault() ?? string.Empty;

        IReadOnlyList<string> Analyze(string text) => Terms(context.Analyzer, field, text);

        var filters = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filter in context.Request.Filters?.Facets ?? [])
        {
            filters[filter.Attribute ?? string.Empty] =
                new HashSet<string>(filter.Values ?? [], StringComparer.OrdinalIgnoreCase);
        }

        return new RuleMatchContext(
            context.QueryText,
            AnalyzedPositions(context.QueryText, synonyms, Analyze),
            Analyze,
            filters,
            context.ContactGroups,
            context.Request.Language ?? string.Empty);
    }

    /// <summary>
    /// One set of analyzed terms per query position: the word that stands there and every synonym of
    /// it, each put through the index's analyzer. A multi-word synonym contributes all of its terms
    /// to the position it starts at, which is what makes <c>sofa bed</c> reachable from <c>futon</c>.
    /// </summary>
    private static IReadOnlyList<IReadOnlySet<string>> AnalyzedPositions(
        string query,
        IReadOnlyList<TuningSynonym> synonyms,
        Func<string, IReadOnlyList<string>> analyze)
    {
        var slots = SynonymExpansion.Expand(query, synonyms);

        if (slots.Count == 0)
        {
            slots =
            [
                .. query
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(word => (IReadOnlyList<string>)[word])
            ];
        }

        return
        [
            .. slots.Select(slot => (IReadOnlySet<string>)new HashSet<string>(
                slot.SelectMany(analyze),
                StringComparer.Ordinal))
        ];
    }

    /// <summary>Runs an analyzer over one piece of text and returns the terms it produced.</summary>
    private static IReadOnlyList<string> Terms(Analyzer analyzer, string field, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var terms = new List<string>();

        using var stream = analyzer.GetTokenStream(field, text);
        var term = stream.AddAttribute<ICharTermAttribute>();

        stream.Reset();

        while (stream.IncrementToken())
        {
            terms.Add(term.ToString());
        }

        stream.End();

        return terms;
    }
}
