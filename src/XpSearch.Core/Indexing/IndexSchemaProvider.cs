using Kentico.Xperience.Lucene.Core;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Builds an index's schema from the base fields every document carries plus the auto-detected
/// fields of every content type the index covers.
/// </summary>
public sealed class IndexSchemaProvider : IIndexSchemaProvider
{
    /// <summary>
    /// Name of the field <see cref="XpSearchIndexingStrategy"/> writes the item's display name to.
    /// Present on every document regardless of content type, which is what makes a generic
    /// "search the title" query and the document suggester possible.
    /// </summary>
    public const string TitleField = "Title";

    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexContentTypeSource contentTypeSource;
    private readonly IContentTypeFieldSource fieldSource;
    private readonly XpSearchIndexingOptions options;

    /// <summary>Initializes a new instance of the <see cref="IndexSchemaProvider"/> class.</summary>
    /// <param name="accessor">The Lucene seam, used to check that the index exists.</param>
    /// <param name="contentTypeSource">Lists the content types an index covers.</param>
    /// <param name="fieldSource">Detects the fields of a content type.</param>
    /// <param name="options">Supplies the linked content types whose fields are flattened into a covered type's documents.</param>
    public IndexSchemaProvider(
        ILuceneIndexAccessor accessor,
        IIndexContentTypeSource contentTypeSource,
        IContentTypeFieldSource fieldSource,
        XpSearchIndexingOptions options)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(contentTypeSource);
        ArgumentNullException.ThrowIfNull(fieldSource);
        ArgumentNullException.ThrowIfNull(options);

        this.accessor = accessor;
        this.contentTypeSource = contentTypeSource;
        this.fieldSource = fieldSource;
        this.options = options;
    }

    /// <summary>
    /// Gets the fields present on every document, whatever its content type: the identifier and the
    /// properties the Lucene integration adds itself, plus the title the strategy adds.
    /// </summary>
    /// <returns>The base fields, in projection order.</returns>
    public static IReadOnlyList<SchemaField> BaseFields() =>
    [
        // The result id is a member of its own, so ID is never projected as an attribute.
        new SchemaField(BaseDocumentProperties.ID, SearchFieldKind.Keyword, Searchable: false, Facetable: false, Sortable: false, Retrievable: false),
        new SchemaField(TitleField, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: true, Retrievable: true, Boost: 2f),
        new SchemaField(BaseDocumentProperties.CONTENT_TYPE_NAME, SearchFieldKind.Keyword, Searchable: false, Facetable: true, Sortable: false, Retrievable: true),
        new SchemaField(BaseDocumentProperties.LANGUAGE_NAME, SearchFieldKind.Keyword, Searchable: false, Facetable: true, Sortable: false, Retrievable: true),
        new SchemaField(BaseDocumentProperties.URL, SearchFieldKind.Keyword, Searchable: false, Facetable: false, Sortable: false, Retrievable: true)
    ];

    /// <inheritdoc />
    public async Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);

        if (!accessor.Exists(indexName))
        {
            throw new IndexNotFoundException(indexName);
        }

        var fields = new List<SchemaField>(BaseFields());
        var contentTypes = await contentTypeSource.GetContentTypesAsync(indexName, cancellationToken).ConfigureAwait(false);

        foreach (string contentType in contentTypes)
        {
            // A field of the same name on two content types is one schema field; IndexSchema keeps
            // the first definition, which matches how the documents share one Lucene field. The
            // content type's own fields come first, so a flattened field never shadows one of its own.
            fields.AddRange(fieldSource.GetFields(contentType));

            foreach (var link in options.FlattenedLinksOf(contentType))
            {
                foreach (string linkedContentType in link.LinkedContentTypeNames)
                {
                    fields.AddRange(fieldSource.GetFields(linkedContentType));
                }
            }
        }

        return new IndexSchema(indexName, fields);
    }
}
