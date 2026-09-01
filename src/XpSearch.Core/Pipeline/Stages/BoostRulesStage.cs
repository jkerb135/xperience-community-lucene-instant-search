using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Applies the actions that act before the search runs (ADR-0022):
/// <see cref="RuleAction.Boost"/> raises the score of its target,
/// <see cref="RuleAction.FilterResults"/> restricts the result set,
/// <see cref="RuleAction.Hide"/> takes a document out of it altogether and
/// <see cref="RuleAction.Redirect"/> records a destination on the response.
/// Pin and bury are a post-execution reordering and belong to <see cref="PinnedAndBuriedStage"/>.
/// </summary>
/// <remarks>
/// Rules arrive in precedence order (priority, then id), and each rule's actions in the order it
/// lists them, so a later clause wraps the earlier ones. Boost, filter and hide all apply; only the
/// first redirect does, and it does not stop the search - the response carries results next to the
/// destination and the client decides whether to navigate.
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
            bool applied = false;

            foreach (var action in rule.Actions)
            {
                applied |= action switch
                {
                    RuleAction.Boost boost => Boost(context, boost),
                    RuleAction.FilterResults filter => Filter(context, filter.FilterExpression),
                    RuleAction.Hide hide => Hide(context, hide),
                    RuleAction.Redirect redirect => Redirect(context, rule, redirect),
                    _ => false
                };
            }

            if (applied && explain)
            {
                context.QueryExplanations.Add(RuleSelection.Explain(rule));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Builds the query a boost targets: one document by id, or everything its filter expression selects.</summary>
    private static Query? Target(SearchContext context, RuleAction.Boost rule)
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

    private static bool Boost(SearchContext context, RuleAction.Boost rule)
    {
        if (Target(context, rule) is not { } target || rule.Multiplier <= 0)
        {
            return false;
        }

        // Lucene.NET 4.8 has no BoostQuery wrapper; the multiplier is a property of the query itself.
        target.Boost = (float)rule.Multiplier;

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
    private static bool Redirect(SearchContext context, TuningRule rule, RuleAction.Redirect redirect)
    {
        if (context.Redirect is not null || string.IsNullOrWhiteSpace(redirect.Url))
        {
            return false;
        }

        context.Redirect = new SearchRedirect { Url = redirect.Url.Trim(), Rule = rule.Name };

        return true;
    }

    /// <summary>
    /// Takes one document out of the search entirely: a <see cref="Occur.MUST_NOT"/> on its id, in the
    /// executed query and in the active filters, so it is missing from every page, excluded from the
    /// total, and cannot be injected back by a pin.
    /// </summary>
    /// <remarks>
    /// This is the difference between hide and bury. Bury removes a document from the page that came
    /// back; hide keeps it out of the result set, which is only expressible before the search runs.
    /// </remarks>
    private static bool Hide(SearchContext context, RuleAction.Hide rule)
    {
        if (string.IsNullOrWhiteSpace(rule.TargetId))
        {
            return false;
        }

        var target = new TermQuery(new Term(BaseDocumentProperties.ID, rule.TargetId.Trim()));

        context.BaseQuery = new BooleanQuery
        {
            { context.BaseQuery, Occur.MUST },
            { target, Occur.MUST_NOT }
        };

        context.ActiveFilters.Add(target, Occur.MUST_NOT);

        return true;
    }

    private static bool Filter(SearchContext context, string expression)
    {
        var pairs = RuleFilterExpression.Parse(expression);

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
