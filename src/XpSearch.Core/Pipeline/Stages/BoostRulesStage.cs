using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Applies the rules that act before the search runs (spec §8.3): <see cref="RuleConsequence.Boost"/>
/// raises the score of its target, <see cref="RuleConsequence.Filter"/> restricts the result set and
/// <see cref="RuleConsequence.Redirect"/> records a destination on the response.
/// Pin and bury are a post-execution reordering and belong to <see cref="PinnedAndBuriedStage"/>.
/// </summary>
/// <remarks>
/// Rules arrive in precedence order (priority, then id) and are applied in that order, so a later
/// rule's clauses wrap the earlier ones. Boost and filter rules all apply; only the first redirect
/// does, and it does not stop the search - the response carries results next to the destination and
/// the client decides whether to navigate.
/// </remarks>
public sealed class BoostRulesStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.BoostRules;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool explain = context.Request.Explain ?? false;

        foreach (var rule in context.Tuning.Rules)
        {
            var applied = rule.Consequence switch
            {
                RuleConsequence.Boost => Boost(context, rule),
                RuleConsequence.Filter => Filter(context, rule),
                RuleConsequence.Redirect => Redirect(context, rule),
                _ => false
            };

            if (applied && explain)
            {
                context.QueryExplanations.Add(RuleSelection.Explain(rule));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Builds the query a rule targets: one document by id, or everything its filter expression selects.</summary>
    private static Query? Target(SearchContext context, TuningRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.TargetId))
        {
            return new TermQuery(new Term(BaseDocumentProperties.ID, rule.TargetId));
        }

        var pairs = RuleFilterExpression.Parse(rule.FilterExpression);

        if (pairs.Count == 0)
        {
            return null;
        }

        var query = new BooleanQuery();

        foreach ((string field, string value) in pairs)
        {
            query.Add(new TermQuery(TermFor(context, field, value)), Occur.MUST);
        }

        return query;
    }

    /// <summary>
    /// Resolves one <c>field:value</c> pair of a filter expression to the term the documents carry.
    /// </summary>
    /// <remarks>
    /// A marketer writes the attribute name they see in a request or a facet, which for the base
    /// fields is not the Lucene field name (<c>contentType</c> against <c>ContentTypeName</c>). A
    /// name the schema does not know is used verbatim, so a rule can still reach a field detection
    /// missed.
    /// </remarks>
    private static Term TermFor(SearchContext context, string field, string value) =>
        new(context.Schema.Find(field)?.LuceneName ?? field, value);

    private static bool Boost(SearchContext context, TuningRule rule)
    {
        if (Target(context, rule) is not { } target || rule.BoostValue <= 0)
        {
            return false;
        }

        // Lucene.NET 4.8 has no BoostQuery wrapper; the multiplier is a property of the query itself.
        target.Boost = (float)rule.BoostValue;

        context.BaseQuery = new BooleanQuery
        {
            { context.BaseQuery, Occur.MUST },
            { target, Occur.SHOULD }
        };

        return true;
    }

    /// <summary>
    /// Records where a redirect rule sends the visitor. The first one wins: rules arrive in
    /// precedence order, and a later one naming another destination is ignored rather than
    /// overwriting it. A rule with no URL configured is not a redirect at all.
    /// </summary>
    private static bool Redirect(SearchContext context, TuningRule rule)
    {
        if (context.Redirect is not null || string.IsNullOrWhiteSpace(rule.RedirectUrl))
        {
            return false;
        }

        context.Redirect = new SearchRedirect { Url = rule.RedirectUrl.Trim(), Rule = rule.Name };

        return true;
    }

    private static bool Filter(SearchContext context, TuningRule rule)
    {
        var pairs = RuleFilterExpression.Parse(rule.FilterExpression);

        if (pairs.Count == 0)
        {
            return false;
        }

        var filter = new BooleanQuery();

        foreach ((string field, string value) in pairs)
        {
            filter.Add(new TermQuery(TermFor(context, field, value)), Occur.MUST);
        }

        context.BaseQuery = new BooleanQuery
        {
            { context.BaseQuery, Occur.MUST },
            { filter, Occur.MUST }
        };

        context.ActiveFilters.Add(filter, Occur.MUST);

        return true;
    }
}
