namespace XpSearch.Core.Abstractions;

/// <summary>
/// The kind of a schema field, which decides the Lucene field types
/// <see cref="XpSearch.Core.Indexing.XpSearchIndexingStrategy"/> emits for it and how the query
/// pipeline is allowed to use it.
/// </summary>
public enum SearchFieldKind
{
    /// <summary>Analyzed free text (text, long text, rich text).</summary>
    Text,

    /// <summary>An un-analyzed string, matched and sorted verbatim (URLs, code names, GUIDs).</summary>
    Keyword,

    /// <summary>A number, indexed as a Lucene <c>double</c> and filterable with <c>numericFilters</c>.</summary>
    Number,

    /// <summary>A date, indexed as Unix epoch seconds and filterable with <c>numericFilters</c>.</summary>
    Date,

    /// <summary>An Xperience taxonomy field, indexed as a multi-valued facet dimension.</summary>
    Taxonomy
}

/// <summary>
/// One field of an index, as auto-detected from the content types the index covers.
/// </summary>
/// <param name="Name">Lucene field name; also the facet dimension name for <see cref="SearchFieldKind.Taxonomy"/>.</param>
/// <param name="Kind">What the field holds.</param>
/// <param name="Searchable">Whether free-text queries match against the field.</param>
/// <param name="Facetable">Whether the field can appear in <c>facets</c> and <c>facetFilters</c>.</param>
/// <param name="Sortable">Whether the field can be used as a sort key (<c>name_asc</c> / <c>name_desc</c>).</param>
/// <param name="Retrievable">Whether the field is stored and can appear in <c>attributesToRetrieve</c>.</param>
/// <param name="Boost">Index-time boost applied to the searchable field.</param>
public sealed record SchemaField(
    string Name,
    SearchFieldKind Kind,
    bool Searchable,
    bool Facetable,
    bool Sortable,
    bool Retrievable,
    float Boost = 1f);

/// <summary>
/// The set of fields an index exposes. Consumed by the query pipeline (query building, filter and
/// sort validation, projection), by the admin attribute dropdown (spec §7.4) and by the ingestion
/// schema endpoint (spec §10.3).
/// </summary>
public sealed class IndexSchema
{
    private readonly Dictionary<string, SchemaField> byName;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexSchema"/> class.
    /// </summary>
    /// <param name="indexName">Code name of the index the schema describes.</param>
    /// <param name="fields">The fields, in declaration order. Later duplicates of a name are ignored.</param>
    public IndexSchema(string indexName, IEnumerable<SchemaField> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentNullException.ThrowIfNull(fields);

        IndexName = indexName;
        byName = new Dictionary<string, SchemaField>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SchemaField>();

        foreach (var field in fields)
        {
            if (byName.TryAdd(field.Name, field))
            {
                ordered.Add(field);
            }
        }

        Fields = ordered;
    }

    /// <summary>Gets the code name of the index this schema describes.</summary>
    public string IndexName { get; }

    /// <summary>Gets the fields of the index, in declaration order.</summary>
    public IReadOnlyList<SchemaField> Fields { get; }

    /// <summary>Looks a field up by name, case-insensitively.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The field, or <see langword="null"/> when the index has no such field.</returns>
    public SchemaField? Find(string name) =>
        name is not null && byName.TryGetValue(name, out var field) ? field : null;
}

/// <summary>
/// Supplies the <see cref="IndexSchema"/> of an index.
/// </summary>
public interface IIndexSchemaProvider
{
    /// <summary>Gets the schema of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="IndexNotFoundException">The index is not registered.</exception>
    Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken);
}
