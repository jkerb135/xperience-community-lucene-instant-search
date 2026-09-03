using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Options;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Validates and normalizes the request (spec §4.4, first stage): trims and length-caps the query
/// text, resolves paging, and validates every filter against the index schema so that a bad request
/// fails here with a 400 keyed by the offending JSON path rather than deep inside Lucene.
/// </summary>
public sealed class NormalizeRequestStage : ISearchStage
{
    private const long ContractMaxPageSize = 1000;

    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly IOptionsMonitor<XpSearchIndexSettings> settings;

    /// <summary>Initializes a new instance of the <see cref="NormalizeRequestStage"/> class.</summary>
    /// <param name="options">The current search options, for the index's code-only members.</param>
    /// <param name="settings">The current per-index settings (AR-2).</param>
    public NormalizeRequestStage(IOptionsMonitor<XpSearchOptions> options, IOptionsMonitor<XpSearchIndexSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        this.options = options;
        this.settings = settings;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.Normalize;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var indexSettings = settings.Get(context.IndexName);

        context.QueryText = Normalize(request.Query, indexSettings.MaxQueryLength);
        context.Page = ValidatePage(request.Page);
        context.PageSize = ValidatePageSize(request.PageSize, indexSettings);
        ValidateResultWindow(context.Page, context.PageSize, indexSettings);

        context.RequestedFacets = ValidateFacets(request.Facets, context.Schema);
        context.FacetFilters = ValidateFacetFilters(request.Filters?.Facets, context.Schema);
        context.NumericFilters = ValidateNumericFilters(request.Filters?.Numeric, context.Schema);
        context.SortField = SortKeyParser.Parse(
            request.Sort,
            context.Schema,
            (IReadOnlyDictionary<string, SortKey>)options.CurrentValue.Indexes[request.Index].SortKeys,
            out bool descending);
        context.SortDescending = descending;
        context.Fields = ValidateFields(request.Fields, context.Schema);
        ValidateHighlightFields(request.Highlight?.Fields, context.Schema);

        return Task.CompletedTask;
    }

    /// <summary>Trims, lowercases and length-caps free-text query input.</summary>
    /// <param name="query">The raw query text.</param>
    /// <param name="maxLength">Maximum number of characters to keep.</param>
    /// <returns>The normalized text; an empty string means "match all".</returns>
    public static string Normalize(string? query, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        string normalized = query.Trim().ToLowerInvariant();

        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static int ValidatePage(long? page)
    {
        if (page is null)
        {
            return 1;
        }

        if (page < 1 || page > int.MaxValue)
        {
            throw new SearchValidationException("page", "page must be one or greater.");
        }

        return (int)page.Value;
    }

    private static int ValidatePageSize(long? pageSize, XpSearchIndexSettings settings)
    {
        if (pageSize is null)
        {
            return settings.DefaultPageSize;
        }

        if (pageSize < 1 || pageSize > ContractMaxPageSize)
        {
            throw new SearchValidationException(
                "pageSize",
                $"pageSize must be between 1 and {ContractMaxPageSize}.");
        }

        // The contract ceiling is rejected above; the configured ceiling is clamped, and the clamped
        // value is what the response reports back.
        return (int)Math.Min(pageSize.Value, settings.MaxPageSize);
    }

    private static void ValidateResultWindow(int page, int pageSize, XpSearchIndexSettings settings)
    {
        long window = (long)page * pageSize;

        if (window > settings.MaxResultWindow)
        {
            throw new SearchValidationException(
                "page",
                $"page multiplied by pageSize must not exceed {settings.MaxResultWindow} results.");
        }
    }

    private static IReadOnlyList<string> ValidateFacets(string[]? facets, IndexSchema schema)
    {
        var names = new List<string>();

        for (int i = 0; i < (facets?.Length ?? 0); i++)
        {
            names.Add(FacetableField(facets![i], schema, $"facets[{i}]").Name);
        }

        return names;
    }

    private static IReadOnlyList<FacetFilter> ValidateFacetFilters(FacetFilter[]? filters, IndexSchema schema)
    {
        var validated = new List<FacetFilter>();

        for (int i = 0; i < (filters?.Length ?? 0); i++)
        {
            var entry = filters![i];
            var field = FacetableField(entry?.Attribute, schema, $"filters.facets[{i}].attribute");

            if (entry!.Values is null || entry.Values.Length == 0)
            {
                // Nothing selected on that attribute refines nothing; dropping it here keeps the
                // query stages from having to special-case an empty OR clause.
                continue;
            }

            validated.Add(new FacetFilter
            {
                // The dimension, not the attribute: a facet filter is only ever used to build a
                // drill-down or a term query, and the response never echoes it back.
                Attribute = field.LuceneName,
                Values = entry.Values,
                Operator = entry.Operator
            });
        }

        return validated;
    }

    private static IReadOnlyList<NumericFilter> ValidateNumericFilters(NumericFilter[]? filters, IndexSchema schema)
    {
        var validated = new List<NumericFilter>();

        for (int i = 0; i < (filters?.Length ?? 0); i++)
        {
            var entry = filters![i];
            string path = $"filters.numeric[{i}].attribute";
            var field = Field(entry?.Attribute, schema, path);

            if (field.Kind is not (SearchFieldKind.Number or SearchFieldKind.Date))
            {
                throw new SearchValidationException(
                    path,
                    $"'{entry!.Attribute}' is not a numeric attribute of index '{schema.IndexName}'.");
            }

            validated.Add(new NumericFilter
            {
                Attribute = field.Name,
                Operator = entry!.Operator,
                Value = entry.Value
            });
        }

        return validated;
    }

    private static IReadOnlyList<string> ValidateFields(string[]? fields, IndexSchema schema)
    {
        var resolved = new List<string>();

        for (int i = 0; i < (fields?.Length ?? 0); i++)
        {
            string path = $"fields[{i}]";
            var field = Field(fields![i], schema, path);

            if (!field.Retrievable)
            {
                throw new SearchValidationException(
                    path,
                    $"'{fields[i]}' is not a retrievable field of index '{schema.IndexName}'.");
            }

            resolved.Add(field.Name);
        }

        return resolved;
    }

    private static void ValidateHighlightFields(string[]? fields, IndexSchema schema)
    {
        for (int i = 0; i < (fields?.Length ?? 0); i++)
        {
            string path = $"highlight.fields[{i}]";
            var field = Field(fields![i], schema, path);

            if (!field.Retrievable)
            {
                throw new SearchValidationException(
                    path,
                    $"'{fields[i]}' is not a retrievable field of index '{schema.IndexName}' and cannot be highlighted.");
            }
        }
    }

    private static SchemaField Field(string? name, IndexSchema schema, string path) =>
        (name is null ? null : schema.Find(name))
            ?? throw new SearchValidationException(path, $"'{name}' is not an attribute of index '{schema.IndexName}'.");

    private static SchemaField FacetableField(string? name, IndexSchema schema, string path)
    {
        var field = Field(name, schema, path);

        return field.Facetable
            ? field
            : throw new SearchValidationException(path, $"'{name}' is not a facetable attribute of index '{schema.IndexName}'.");
    }
}
