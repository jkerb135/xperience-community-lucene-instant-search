using System.Globalization;

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
        var languageFilter = new TermQuery(new Term(BaseDocumentProperties.LANGUAGE_NAME, language));

        var filtered = new BooleanQuery
        {
            { textQuery, Occur.MUST },
            { languageFilter, Occur.MUST }
        };

        context.ActiveFilters.Add(languageFilter, Occur.MUST);
        context.BaseQuery = filtered;
        return Task.CompletedTask;
    }

    private static Query BuildTextQuery(SearchContext context)
    {
        string[] fields = [.. context.Schema.Fields
            .Where(field => field.Searchable)
            .Select(LuceneFieldNames.SearchFieldName)];

        if ((context.QueryText.Length == 0 && context.QuerySlots.Count == 0) || fields.Length == 0)
        {
            return new MatchAllDocsQuery();
        }

        var parser = new MultiFieldQueryParser(LuceneVersion.LUCENE_48, fields, context.Analyzer, Boosts(context))
        {
            DefaultOperator = Operator.AND
        };

        if (context.QuerySlots.Count == 0)
        {
            // The query is user input, so every operator character is escaped before parsing: the
            // endpoint exposes relevance, not the Lucene query syntax.
            return parser.Parse(QueryParserBase.Escape(context.QueryText));
        }

        // Synonyms were applied (spec §8.3): each slot is one position of the query, its alternatives
        // ORed, and the slots ANDed - so "red sofa" with sofa=couch still requires both positions.
        var expanded = new BooleanQuery();

        foreach (var slot in context.QuerySlots)
        {
            var alternatives = new BooleanQuery();

            foreach (string term in slot)
            {
                alternatives.Add(parser.Parse(QueryParserBase.Escape(term)), Occur.SHOULD);
            }

            expanded.Add(alternatives, Occur.MUST);
        }

        return expanded;
    }

    /// <summary>
    /// Per-field boosts: the schema's own value, multiplied by the field weight an admin configured
    /// in the Search tuning application (spec §8.2, §8.3).
    /// </summary>
    private static IDictionary<string, float> Boosts(SearchContext context)
    {
        var boosts = new Dictionary<string, float>(StringComparer.Ordinal);
        bool explain = context.Request.Explain ?? false;

        foreach (var field in context.Schema.Fields.Where(field => field.Searchable))
        {
            float boost = field.Boost;

            if (context.Tuning.FieldWeights.TryGetValue(field.Name, out double weight))
            {
                boost *= (float)weight;

                if (explain)
                {
                    context.QueryExplanations.Add(
                        string.Create(CultureInfo.InvariantCulture, $"weight:{field.Name}×{weight}"));
                }
            }

            boosts[LuceneFieldNames.SearchFieldName(field)] = boost;
        }

        return boosts;
    }
}
