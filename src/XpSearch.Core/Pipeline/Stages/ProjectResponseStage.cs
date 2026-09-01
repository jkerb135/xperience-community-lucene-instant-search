using System.Globalization;
using System.Text.Json;

using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Documents;
using Lucene.Net.Index;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Projects the materialized page onto the response DTO: results with their retrieved fields,
/// facet values, paging figures, the data attached by matching rules and, when asked for, the
/// ranking explanation.
/// </summary>
public sealed class ProjectResponseStage : ISearchStage
{
    private readonly ILogger<ProjectResponseStage> logger;

    /// <summary>Initializes a new instance of the <see cref="ProjectResponseStage"/> class.</summary>
    /// <param name="logger">Reports a rule whose custom data is not a JSON object.</param>
    public ProjectResponseStage(ILogger<ProjectResponseStage>? logger = null) =>
        this.logger = logger ?? NullLogger<ProjectResponseStage>.Instance;

    /// <inheritdoc />
    public int Order => SearchStageOrder.Project;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool explain = context.Request.Explain ?? false;
        var results = new Result[context.Documents.Count];

        for (int i = 0; i < results.Length; i++)
        {
            var scored = context.Documents[i];
            string id = ResolveResultId(scored.Document);

            results[i] = new Result
            {
                Id = id,
                Score = scored.Score,
                Attributes = ProjectAttributes(context, scored.Document),
                Highlights = i < context.Highlights.Count ? context.Highlights[i] : null,
                Ranking = explain
                    ? new RankingInfo
                    {
                        BaseScore = scored.Score,
                        Boosts = Explain(context, id),
                        Position = ((long)(context.Page - 1) * context.PageSize) + i + 1
                    }
                    : null
            };
        }

        context.Response = new SearchResponse
        {
            Results = results,
            Facets = context.FacetValues,
            Page = context.Page,
            PageSize = context.PageSize,
            Total = context.Total,
            TotalPages = context.PageSize <= 0
                ? 0
                : (long)Math.Ceiling(context.Total / (double)context.PageSize),
            Redirect = context.Redirect,
            RuleData = RuleData(context),
            QueryId = string.IsNullOrWhiteSpace(context.Request.QueryId)
                ? Guid.NewGuid().ToString()
                : context.Request.QueryId
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// The <c>ruleData</c> of the response: the JSON objects of every fired rule's custom-data
    /// action, shallow-merged in application order so a later rule wins a key outright
    /// (ADR-0022). <see langword="null"/> - the member is then absent - when no rule returned data.
    /// </summary>
    /// <remarks>
    /// The storage validates the JSON on save, so a rule that carries something else has been edited
    /// around the form or predates the validation. It is skipped rather than failing the search: a
    /// marketer's typo must not take search down.
    /// </remarks>
    private Dictionary<string, object>? RuleData(SearchContext context)
    {
        Dictionary<string, object>? merged = null;

        foreach (var (rule, data) in context.Tuning.Rules
            .SelectMany(rule => rule.Actions.OfType<RuleAction.CustomData>().Select(data => (rule, data))))
        {
            JsonElement parsed;

            try
            {
                parsed = JsonDocument.Parse(data.Json ?? string.Empty).RootElement.Clone();
            }
            catch (JsonException exception)
            {
                logger.LogDebug(exception, "Rule {Rule} carries custom data that is not valid JSON; skipped.", rule.Name);
                continue;
            }

            if (parsed.ValueKind != JsonValueKind.Object)
            {
                logger.LogDebug("Rule {Rule} carries custom data that is not a JSON object; skipped.", rule.Name);
                continue;
            }

            merged ??= new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var property in parsed.EnumerateObject())
            {
                merged[property.Name] = property.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Resolves the stable identifier a click or conversion event addresses the document by.
    /// </summary>
    /// <remarks>
    /// <c>XpSearchIndexingStrategy</c> writes <c>BaseDocumentProperties.ID</c>; the Lucene integration
    /// itself does not, so a hand-written strategy may leave it out. The fallback composes the two
    /// fields the integration always adds, which is exactly the pair it deletes documents by.
    /// </remarks>
    internal static string ResolveResultId(Document document)
    {
        string? id = document.Get(BaseDocumentProperties.ID);

        if (!string.IsNullOrEmpty(id))
        {
            return id;
        }

        return XpSearchIndexingStrategy.ComposeResultId(
            document.Get(BaseDocumentProperties.ITEM_GUID) ?? string.Empty,
            document.Get(BaseDocumentProperties.LANGUAGE_NAME) ?? string.Empty);
    }

    /// <summary>
    /// The <c>ranking.boosts</c> entries of one hit (spec §8.3): everything that applied to the whole
    /// query - field weights, synonym expansions, boost and filter rules - then the rules that named
    /// this document.
    /// </summary>
    private static string[] Explain(SearchContext context, string id)
    {
        if (!context.DocumentExplanations.TryGetValue(id, out var entries))
        {
            return [.. context.QueryExplanations];
        }

        return [.. context.QueryExplanations, .. entries];
    }

    private static Dictionary<string, JsonElement> ProjectAttributes(SearchContext context, Document document)
    {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var wanted = context.Fields.Count > 0
            ? context.Fields.Select(context.Schema.Find).OfType<SchemaField>()
            : context.Schema.Fields.Where(field => field.Retrievable);

        foreach (var field in wanted)
        {
            if (string.Equals(field.LuceneName, BaseDocumentProperties.ID, StringComparison.Ordinal))
            {
                // The result id is a member of its own, never one of the projected attributes.
                continue;
            }

            var values = document.GetFields(field.LuceneName);

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

        return string.Equals(field.LuceneName, BaseDocumentProperties.URL, StringComparison.OrdinalIgnoreCase)
            ? WebUrl.ToRootRelative(text)
            : text;
    }
}
