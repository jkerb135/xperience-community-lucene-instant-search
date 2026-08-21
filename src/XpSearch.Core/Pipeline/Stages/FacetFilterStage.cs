using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Filters;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Applies <c>facetFilters</c>: the outer array is ANDed, each inner array is ORed.
/// </summary>
/// <remarks>
/// A group that refines a single, not-yet-drilled dimension becomes a drill-down, so the execute
/// stage can run it through <c>DrillSideways</c> and keep that dimension's counts answering "what if
/// I picked another value" - the semantics a refinement list needs. Anything a drill-down cannot
/// express (a group spanning several dimensions, a second group on an already-drilled dimension, or
/// an index with no taxonomy sidecar) becomes an ordinary boolean MUST clause on the base query,
/// which is correct but counts that dimension as filtered.
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

        foreach (var group in context.FacetFilters)
        {
            string dimension = group[0].Attribute;
            bool singleDimension = group.All(refinement =>
                string.Equals(refinement.Attribute, dimension, StringComparison.Ordinal));

            if (context.FacetsConfig is not null && singleDimension && !context.DrillDown.ContainsKey(dimension))
            {
                context.DrillDown[dimension] = [.. group.Select(refinement => refinement.Value)];
            }
            else
            {
                mustClauses.Add(BuildOrClause(group));
            }
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

    private static Query BuildOrClause(IReadOnlyList<FacetRefinement> group)
    {
        var or = new BooleanQuery();

        foreach (var refinement in group)
        {
            // The strategy stores every facet value verbatim in a StringField named after the
            // dimension, so a plain term query is the non-taxonomy equivalent of a drill-down.
            or.Add(new TermQuery(new Term(refinement.Attribute, refinement.Value)), Occur.SHOULD);
        }

        return or;
    }
}
