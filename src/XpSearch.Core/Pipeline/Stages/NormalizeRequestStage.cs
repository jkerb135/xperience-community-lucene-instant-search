using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Filters;
using XpSearch.Core.Options;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Validates and normalizes the request (spec §4.4, first stage): trims and length-caps the query
/// text, resolves paging, and parses every filter against the index schema so that a bad request
/// fails here with a field-keyed 400 rather than deep inside Lucene.
/// </summary>
public sealed class NormalizeRequestStage : ISearchStage
{
    private const long ContractMaxHitsPerPage = 1000;

    private readonly XpSearchOptions options;

    /// <summary>Initializes a new instance of the <see cref="NormalizeRequestStage"/> class.</summary>
    /// <param name="options">The configured search options.</param>
    public NormalizeRequestStage(IOptions<XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.Normalize;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;

        context.QueryText = Normalize(request.Query, options.MaxQueryLength);
        context.Page = ValidatePage(request.Page);
        context.HitsPerPage = ValidateHitsPerPage(request.HitsPerPage);
        ValidateResultWindow(context.Page, context.HitsPerPage);

        context.RequestedFacets = FacetFilterParser.ParseRequestedFacets(request.Facets, context.Schema);
        context.FacetFilters = FacetFilterParser.ParseAll(request.FacetFilters, context.Schema);
        context.NumericFilters = NumericFilterParser.ParseAll(request.NumericFilters, context.Schema);
        context.SortField = SortKeyParser.Parse(request.Sort, context.Schema, out bool descending);
        context.SortDescending = descending;
        context.AttributesToRetrieve = ValidateAttributes(request.AttributesToRetrieve, context.Schema);
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

    private int ValidatePage(long? page)
    {
        if (page is null)
        {
            return 0;
        }

        if (page < 0 || page > int.MaxValue)
        {
            throw new SearchValidationException("page", "page must be zero or greater.");
        }

        return (int)page.Value;
    }

    private int ValidateHitsPerPage(long? hitsPerPage)
    {
        if (hitsPerPage is null)
        {
            return options.DefaultHitsPerPage;
        }

        if (hitsPerPage < 1 || hitsPerPage > ContractMaxHitsPerPage)
        {
            throw new SearchValidationException(
                "hitsPerPage",
                $"hitsPerPage must be between 1 and {ContractMaxHitsPerPage}.");
        }

        // The contract ceiling is rejected above; the configured ceiling is clamped, and the clamped
        // value is what the response reports back.
        return (int)Math.Min(hitsPerPage.Value, options.MaxHitsPerPage);
    }

    private void ValidateResultWindow(int page, int hitsPerPage)
    {
        long window = (long)(page + 1) * hitsPerPage;

        if (window > options.MaxResultWindow)
        {
            throw new SearchValidationException(
                "page",
                $"page multiplied by hitsPerPage must not exceed {options.MaxResultWindow} results.");
        }
    }

    private static IReadOnlyList<string> ValidateAttributes(string[]? attributes, IndexSchema schema)
    {
        if (attributes is null)
        {
            return [];
        }

        var resolved = new List<string>(attributes.Length);

        foreach (string attribute in attributes)
        {
            var field = schema.Find(attribute);

            if (field is null || !field.Retrievable)
            {
                throw new SearchValidationException(
                    "attributesToRetrieve",
                    $"'{attribute}' is not a retrievable attribute of index '{schema.IndexName}'.");
            }

            resolved.Add(field.Name);
        }

        return resolved;
    }

    private static void ValidateHighlightFields(string[]? fields, IndexSchema schema)
    {
        foreach (string field in fields ?? [])
        {
            var schemaField = schema.Find(field);

            if (schemaField is null || !schemaField.Retrievable)
            {
                throw new SearchValidationException(
                    "highlight.fields",
                    $"'{field}' is not a retrievable attribute of index '{schema.IndexName}' and cannot be highlighted.");
            }
        }
    }
}
