using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Contract;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Applies <c>filters.facets</c>: entries are ANDed, and the values inside one entry combine
/// according to its <c>operator</c> (<c>or</c> by default, <c>and</c> on request).
/// </summary>
/// <remarks>
/// An <c>or</c> entry on a not-yet-drilled dimension becomes a drill-down, so the execute stage can
/// run it through <c>DrillSideways</c> and keep that dimension's counts answering "what if I picked
/// another value" - the semantics a facet list needs. Anything a drill-down cannot express (an
/// <c>and</c> entry, a second entry on an already-drilled dimension, or an index with no taxonomy
/// sidecar) becomes an ordinary boolean clause on the base query, which is correct but counts that
/// dimension as filtered.
/// </remarks>
public sealed class FacetFilterStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.FacetFilters;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.FacetFilters.Count == 0)
        {
            return Task.CompletedTask;
        }

        var mustClauses = new List<Query>();

        foreach (var filter in context.FacetFilters)
        {
            bool disjunctive = (filter.Operator ?? FacetOperator.Or) == FacetOperator.Or;

            if (disjunctive && context.FacetsConfig is not null && !context.DrillDown.ContainsKey(filter.Attribute))
            {
                context.DrillDown[filter.Attribute] = filter.Values;

                // A drill-down never reaches the base query, so the equivalent boolean clause is
                // recorded separately: the pin stage has to know every refinement in play (spec §8.3).
                context.ActiveFilters.Add(BuildClause(filter, disjunctive: true), Occur.MUST);
                continue;
            }

            var clause = BuildClause(filter, disjunctive);

            mustClauses.Add(clause);
            context.ActiveFilters.Add(clause, Occur.MUST);
        }

        if (mustClauses.Count == 0)
        {
            return Task.CompletedTask;
        }

        var combined = new BooleanQuery { { context.BaseQuery, Occur.MUST } };

        foreach (var clause in mustClauses)
        {
            combined.Add(clause, Occur.MUST);
        }

        context.BaseQuery = combined;
        return Task.CompletedTask;
    }

    private static Query BuildClause(FacetFilter filter, bool disjunctive)
    {
        var query = new BooleanQuery();

        foreach (string value in filter.Values)
        {
            // The strategy stores every facet value verbatim in a StringField named after the
            // dimension, so a plain term query is the non-taxonomy equivalent of a drill-down.
            query.Add(new TermQuery(new Term(filter.Attribute, value)), disjunctive ? Occur.SHOULD : Occur.MUST);
        }

        return query;
    }
}
