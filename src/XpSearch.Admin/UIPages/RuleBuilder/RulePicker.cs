using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Search;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.UIPages.RuleBuilder;

/// <summary>One row of the rule builder's item picker (design canvas 5h, left).</summary>
public class PickedItemDto
{
    /// <summary>Gets or sets the result id, which is what the rule stores.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document's title, or <see langword="null"/> for a stored id the index no
    /// longer holds - which the builder shows as a warning instead of a title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the document's link, or <see langword="null"/> when it was not resolved.</summary>
    public string? Url { get; set; }
}

/// <summary>One value of the rule builder's attribute value picker (design canvas 5h, right).</summary>
public class AttributeValueDto
{
    /// <summary>Gets or sets the value as the index stores it, which is what the rule stores.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Gets or sets the text to show; for a taxonomy dimension the tag title.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets how many documents currently carry the value.</summary>
    public long Count { get; set; }
}

/// <summary>What the client sends to search the index for an item to pin, hide, boost or bury.</summary>
public class ItemSearchRequest
{
    /// <summary>Gets or sets what the marketer typed. An empty query lists the first items of the index.</summary>
    public string Query { get; set; } = string.Empty;
}

/// <summary>The answer to an item search: the matches, or why there are none.</summary>
public class ItemSearchResult
{
    /// <summary>Gets or sets the matches, capped at <see cref="RulePicker.MaxItems"/>.</summary>
    public IReadOnlyList<PickedItemDto> Items { get; set; } = [];

    /// <summary>Gets or sets why the search could not run, or an empty string.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Builds a failed search.</summary>
    /// <param name="message">What to tell the marketer.</param>
    /// <returns>The result.</returns>
    public static ItemSearchResult Failed(string message) => new() { Error = message };
}

/// <summary>What the client sends to fill an attribute's value drop-down.</summary>
public class AttributeValuesRequest
{
    /// <summary>Gets or sets the facetable attribute whose values are wanted.</summary>
    public string Attribute { get; set; } = string.Empty;
}

/// <summary>The answer to a value lookup: the values with their counts, or why there are none.</summary>
public class AttributeValuesResult
{
    /// <summary>Gets or sets the values, ordered by count descending as the facet query returns them.</summary>
    public IReadOnlyList<AttributeValueDto> Values { get; set; } = [];

    /// <summary>Gets or sets why the lookup could not run, or an empty string.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Builds a failed lookup.</summary>
    /// <param name="message">What to tell the marketer.</param>
    /// <returns>The result.</returns>
    public static AttributeValuesResult Failed(string message) => new() { Error = message };
}

/// <summary>
/// The three index reads behind the rule builder's pickers (design canvas 5h): search for an item,
/// resolve the ids a saved rule already holds, and list the real values of an attribute.
/// </summary>
public interface IRulePicker
{
    /// <summary>Searches the index for items to pin, hide, boost or bury.</summary>
    /// <param name="indexName">Code name of the index the rule belongs to.</param>
    /// <param name="query">What the marketer typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, capped at <see cref="RulePicker.MaxItems"/>.</returns>
    Task<IReadOnlyList<PickedItemDto>> SearchAsync(string indexName, string query, CancellationToken cancellationToken);

    /// <summary>Resolves the result ids a saved rule holds into what they point at.</summary>
    /// <param name="indexName">Code name of the index the rule belongs to.</param>
    /// <param name="ids">The stored ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per id, in the order asked for. An id the index no longer holds keeps a
    /// <see langword="null"/> <see cref="PickedItemDto.Title"/> rather than being dropped.
    /// </returns>
    Task<IReadOnlyList<PickedItemDto>> ResolveAsync(string indexName, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>Lists the values an attribute really holds, with their document counts.</summary>
    /// <param name="indexName">Code name of the index the rule belongs to.</param>
    /// <param name="attribute">The facetable attribute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The values.</returns>
    Task<IReadOnlyList<AttributeValueDto>> ValuesAsync(string indexName, string attribute, CancellationToken cancellationToken);

    /// <summary>Lists the attributes the attribute drop-down may offer: the index's facetable fields.</summary>
    /// <param name="indexName">Code name of the index the rule belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attribute names, empty when the index is unknown or has no facetable field.</returns>
    Task<IReadOnlyList<string>> AttributesAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IRulePicker"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both index reads go through <see cref="IQueryTesterSearch"/>, which assembles a pipeline from the
/// registered stages instead of calling the registered <c>ISearchPipeline</c>. That is deliberate:
/// the registered one is the caching decorator, and it is the decorator that writes the search
/// activity and the aggregate query log row through <c>ISearchRequestJournal</c> (spec §9.2). A
/// marketer typing into the item picker must not land in the analytics as if a visitor had searched,
/// and must not answer from - or fill - the visitor cache. Tuning is switched off for the same
/// reason: the picker shows the index as it is, not as the rule being written would rewrite it.
/// </para>
/// <para>
/// The value list is a facet-only query (page size one, one requested facet), so the cap on it is the
/// pipeline's own <c>MaxFacetValues</c>; nothing here re-imposes one.
/// </para>
/// </remarks>
public sealed class RulePicker : IRulePicker
{
    /// <summary>How many matches the item picker offers at once (design canvas 5h).</summary>
    public const int MaxItems = 20;

    private readonly IQueryTesterSearch search;
    private readonly IIndexDocumentLookup lookup;
    private readonly IIndexSchemaProvider schemaProvider;

    /// <summary>Initializes a new instance of the <see cref="RulePicker"/> class.</summary>
    /// <param name="search">Runs a pipeline search without journaling it.</param>
    /// <param name="lookup">Resolves stored result ids.</param>
    /// <param name="schemaProvider">Supplies the index schema the attribute list is built from.</param>
    public RulePicker(IQueryTesterSearch search, IIndexDocumentLookup lookup, IIndexSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        this.search = search;
        this.lookup = lookup;
        this.schemaProvider = schemaProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickedItemDto>> SearchAsync(string indexName, string query, CancellationToken cancellationToken)
    {
        var side = await search
            .ExecuteAsync(
                new SearchRequest
                {
                    Index = indexName,
                    Query = query ?? string.Empty,
                    Page = 1,
                    PageSize = MaxItems,
                },
                applyTuning: false,
                contactGroup: string.Empty,
                variant: TuningVariant.Live,
                cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. (side.Response.Results ?? []).Select(result => new PickedItemDto
            {
                Id = result.Id ?? string.Empty,
                Title = QueryTesterDiff.Attribute(result, QueryTesterDiff.TitleAttribute),
                Url = QueryTesterDiff.Attribute(result, QueryTesterDiff.UrlAttribute),
            })
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickedItemDto>> ResolveAsync(
        string indexName,
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var found = (await lookup.ResolveAsync(indexName, wanted, cancellationToken).ConfigureAwait(false))
            .ToDictionary(document => document.Id, StringComparer.Ordinal);

        // A stored id the index no longer holds keeps its place with no title, so the builder can
        // warn about it instead of quietly forgetting the action points at deleted content.
        return
        [
            .. wanted.Select(id => found.TryGetValue(id, out var document)
                ? new PickedItemDto { Id = id, Title = document.Title, Url = document.Url }
                : new PickedItemDto { Id = id })
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttributeValueDto>> ValuesAsync(string indexName, string attribute, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attribute))
        {
            return [];
        }

        string wanted = attribute.Trim();

        var side = await search
            .ExecuteAsync(
                new SearchRequest
                {
                    Index = indexName,
                    Query = string.Empty,
                    Page = 1,
                    PageSize = 1,
                    Facets = [wanted],
                },
                applyTuning: false,
                contactGroup: string.Empty,
                variant: TuningVariant.Live,
                cancellationToken)
            .ConfigureAwait(false);

        if (side.Response.Facets is not { } facets || !facets.TryGetValue(wanted, out var values))
        {
            return [];
        }

        return [.. values.Select(value => new AttributeValueDto { Value = value.Value, Label = value.Label, Count = value.Count })];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> AttributesAsync(string indexName, CancellationToken cancellationToken)
    {
        // Which fields an attribute drop-down may offer is decided once, in Core, for this and for
        // the widget property drop-downs alike (spec §7.4); the option lines it returns are
        // "value;label" pairs of the same name.
        string? options = await FacetAttributeOptions
            .BuildOptionsAsync(schemaProvider, indexName, cancellationToken)
            .ConfigureAwait(false);

        return options is null
            ? []
            : [.. options.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(';')[0])];
    }
}
