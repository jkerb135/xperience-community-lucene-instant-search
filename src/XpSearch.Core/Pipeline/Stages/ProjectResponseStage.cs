using System.Globalization;
using System.Text.Json;

using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Documents;
using Lucene.Net.Index;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Projects the materialized page onto the response DTO: hits with their retrieved attributes,
/// facet counts, paging figures and, when asked for, the ranking explanation.
/// </summary>
public sealed class ProjectResponseStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.Project;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool explain = context.Request.Explain ?? false;
        var hits = new Hit[context.Documents.Count];

        for (int i = 0; i < hits.Length; i++)
        {
            var scored = context.Documents[i];

            hits[i] = new Hit
            {
                ObjectId = ResolveObjectId(scored.Document),
                Score = scored.Score,
                Attributes = ProjectAttributes(context, scored.Document),
                Highlights = i < context.Highlights.Count ? context.Highlights[i] : null,
                RankingInfo = explain
                    ? new RankingInfo
                    {
                        BaseScore = scored.Score,

                        // Boost rules are Phase 5; until then nothing has changed the score.
                        AppliedBoosts = [],
                        Position = ((long)context.Page * context.HitsPerPage) + i + 1
                    }
                    : null
            };
        }

        context.Response = new SearchResponse
        {
            Hits = hits,
            Facets = context.FacetCounts,
            Page = context.Page,
            HitsPerPage = context.HitsPerPage,
            NbHits = context.TotalHits,
            NbPages = context.HitsPerPage <= 0
                ? 0
                : (long)Math.Ceiling(context.TotalHits / (double)context.HitsPerPage),
            QueryId = string.IsNullOrWhiteSpace(context.Request.QueryId)
                ? Guid.NewGuid().ToString()
                : context.Request.QueryId
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the stable identifier a click or conversion event addresses the document by.
    /// </summary>
    /// <remarks>
    /// <c>XpSearchIndexingStrategy</c> writes <c>BaseDocumentProperties.ID</c>; the Lucene integration
    /// itself does not, so a hand-written strategy may leave it out. The fallback composes the two
    /// fields the integration always adds, which is exactly the pair it deletes documents by.
    /// </remarks>
    private static string ResolveObjectId(Document document)
    {
        string? id = document.Get(BaseDocumentProperties.ID);

        if (!string.IsNullOrEmpty(id))
        {
            return id;
        }

        return XpSearchIndexingStrategy.ComposeObjectId(
            document.Get(BaseDocumentProperties.ITEM_GUID) ?? string.Empty,
            document.Get(BaseDocumentProperties.LANGUAGE_NAME) ?? string.Empty);
    }

    private static Dictionary<string, JsonElement> ProjectAttributes(SearchContext context, Document document)
    {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var wanted = context.AttributesToRetrieve.Count > 0
            ? context.AttributesToRetrieve.Select(context.Schema.Find).OfType<SchemaField>()
            : context.Schema.Fields.Where(field => field.Retrievable);

        foreach (var field in wanted)
        {
            if (string.Equals(field.Name, BaseDocumentProperties.ID, StringComparison.Ordinal))
            {
                // objectID is a reserved member of the hit, never an extension-data attribute.
                continue;
            }

            var values = document.GetFields(field.Name);

            if (values.Length == 0)
            {
                continue;
            }

            attributes[field.Name] = ToJson(field, values);
        }

        return attributes;
    }

    private static JsonElement ToJson(SchemaField field, IIndexableField[] values)
    {
        if (values.Length > 1)
        {
            return JsonSerializer.SerializeToElement(values.Select(value => ToScalar(field, value)).ToArray());
        }

        return JsonSerializer.SerializeToElement(ToScalar(field, values[0]));
    }

    private static object? ToScalar(SchemaField field, IIndexableField value)
    {
        if (field.Kind is SearchFieldKind.Number or SearchFieldKind.Date)
        {
            object? number = value.NumericType switch
            {
                NumericFieldType.DOUBLE => value.GetDoubleValue(),
                NumericFieldType.SINGLE => value.GetSingleValue(),
                NumericFieldType.INT64 => value.GetInt64Value(),
                NumericFieldType.INT32 => value.GetInt32Value(),
                _ => null
            };

            if (number is not null)
            {
                return number;
            }
        }

        string? text = value.GetStringValue(CultureInfo.InvariantCulture);

        return string.Equals(field.Name, BaseDocumentProperties.URL, StringComparison.OrdinalIgnoreCase)
            ? WebUrl.ToRootRelative(text)
            : text;
    }
}
