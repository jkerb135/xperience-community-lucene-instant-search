using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Turns the normalized free text into a Lucene query over every searchable field of the schema,
/// and adds the language filter when the request asked for one.
/// </summary>
public sealed class BuildQueryStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.BuildQuery;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var textQuery = BuildTextQuery(context);
        string? language = context.Request.Language;

        if (string.IsNullOrWhiteSpace(language))
        {
            context.BaseQuery = textQuery;
            return Task.CompletedTask;
        }

        // One index holds every language and the integration writes the language into every document
        // (BaseDocumentProperties.LANGUAGE_NAME), so a language request is a term filter. Whether a
        // per-language index is the better model is still open - spec §13.2.
        var filtered = new BooleanQuery
        {
            { textQuery, Occur.MUST },
            { new TermQuery(new Term(BaseDocumentProperties.LANGUAGE_NAME, language)), Occur.MUST }
        };

        context.BaseQuery = filtered;
        return Task.CompletedTask;
    }

    private static Query BuildTextQuery(SearchContext context)
    {
        string[] fields = [.. context.Schema.Fields
            .Where(field => field.Searchable)
            .Select(LuceneFieldNames.SearchFieldName)];

        if (context.QueryText.Length == 0 || fields.Length == 0)
        {
            return new MatchAllDocsQuery();
        }

        var boosts = context.Schema.Fields
            .Where(field => field.Searchable)
            .ToDictionary(LuceneFieldNames.SearchFieldName, field => field.Boost, StringComparer.Ordinal);

        var parser = new MultiFieldQueryParser(LuceneVersion.LUCENE_48, fields, context.Analyzer, boosts)
        {
            DefaultOperator = Operator.AND
        };

        // The query is user input, so every operator character is escaped before parsing: the endpoint
        // exposes relevance, not the Lucene query syntax.
        return parser.Parse(QueryParserBase.Escape(context.QueryText));
    }
}
