using Lucene.Net.Facet;
using Lucene.Net.Index;
using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Runs the assembled query against the index and materializes the requested page.
/// </summary>
/// <remarks>
/// When the index has a taxonomy sidecar the search goes through <see cref="DrillSideways"/>, which
/// yields facet counts in the same pass; otherwise it is a plain searcher call and no counts are
/// available. Documents are read inside the searcher callback because the searcher comes from a
/// cached lease and is invalid the moment the callback returns.
/// </remarks>
public sealed class ExecuteSearchStage : ISearchStage
{
    private readonly ILuceneIndexAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="ExecuteSearchStage"/> class.</summary>
    /// <param name="accessor">The Lucene seam.</param>
    public ExecuteSearchStage(ILuceneIndexAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        this.accessor = accessor;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.Execute;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string indexName = context.Request.Index;
        int topN = Math.Max(1, context.Page * context.PageSize);
        var sort = BuildSort(context);

        if (context.FacetsConfig is null)
        {
            accessor.UseSearcher(indexName, searcher =>
            {
                var hits = sort is null
                    ? searcher.Search(context.BaseQuery, topN)
                    : searcher.Search(context.BaseQuery, null, topN, sort, doDocScores: true, doMaxScore: false);

                Materialize(context, searcher, hits);
                return true;
            });

            return Task.CompletedTask;
        }

        var drillDown = BuildDrillDownQuery(context);

        accessor.UseSearcherWithDrillSideways(indexName, (searcher, drillSideways) =>
        {
            var result = sort is null
                ? drillSideways.Search(drillDown, topN)
                : drillSideways.Search(drillDown, null, null, topN, sort, doDocScores: true, doMaxScore: false);

            context.Facets = result.Facets;
            Materialize(context, searcher, result.Hits);
            return true;
        });

        return Task.CompletedTask;
    }

    private static DrillDownQuery BuildDrillDownQuery(SearchContext context)
    {
        var config = context.FacetsConfig!;
        var drillDown = new DrillDownQuery(config, context.BaseQuery);

        foreach ((string dimension, var values) in context.DrillDown)
        {
            if (values.Count == 1)
            {
                drillDown.Add(dimension, values[0]);
                continue;
            }

            // One Add per dimension: several values of the same dimension are ORed by handing
            // DrillDownQuery a boolean of the drill-down terms it would have built itself.
            string indexedField = config.GetDimConfig(dimension).IndexFieldName;
            var or = new BooleanQuery();

            foreach (string value in values)
            {
                or.Add(new TermQuery(DrillDownQuery.Term(indexedField, dimension, value)), Occur.SHOULD);
            }

            drillDown.Add(dimension, or);
        }

        return drillDown;
    }

    private static Sort? BuildSort(SearchContext context)
    {
        if (context.SortField is null)
        {
            return null;
        }

        var type = context.SortField.Kind switch
        {
            SearchFieldKind.Number => SortFieldType.DOUBLE,
            SearchFieldKind.Date => SortFieldType.INT64,
            _ => SortFieldType.STRING
        };

        return new Sort(new SortField(LuceneFieldNames.SortFieldName(context.SortField), type, context.SortDescending));
    }

    private static void Materialize(SearchContext context, IndexSearcher searcher, TopDocs hits)
    {
        context.Total = hits.TotalHits;

        int skip = (context.Page - 1) * context.PageSize;
        var page = new List<ScoredDocument>();

        for (int i = skip; i < hits.ScoreDocs.Length && page.Count < context.PageSize; i++)
        {
            var scoreDoc = hits.ScoreDocs[i];
            page.Add(new ScoredDocument(searcher.Doc(scoreDoc.Doc), scoreDoc.Score));
        }

        context.Documents = page;
    }
}
