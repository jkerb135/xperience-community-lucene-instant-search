using CMS.ContentEngine;

using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// What <see cref="XpSearchIndexingStrategy.ContributeAsync"/> gets to work with: the item being
/// indexed, the data that was loaded for it, its detected schema, and the two helpers that write a
/// value into the document exactly the way the base mapping does - same field types, same sort and
/// label fields, same facet registration.
/// </summary>
public sealed class IndexingContext
{
    private readonly XpSearchIndexingStrategy strategy;
    private readonly Document document;

    internal IndexingContext(
        XpSearchIndexingStrategy strategy,
        Document document,
        IIndexEventItemModel item,
        IContentQueryDataContainer data,
        IReadOnlyList<SchemaField> fields)
    {
        this.strategy = strategy;
        this.document = document;

        Item = item;
        Data = data;
        Fields = fields;
    }

    /// <summary>Gets the item the Lucene integration asked for a document.</summary>
    public IIndexEventItemModel Item { get; }

    /// <summary>
    /// Gets the loaded content of the item. Linked items are reachable through
    /// <c>TryGetLinkedItems(fieldName, out var linked)</c> up to the configured depth.
    /// </summary>
    public IContentQueryDataContainer Data { get; }

    /// <summary>Gets the fields auto-detected for the item's content type.</summary>
    public IReadOnlyList<SchemaField> Fields { get; }

    /// <summary>Gets the code name of the language variant being indexed.</summary>
    public string LanguageName => Item.LanguageName;

    /// <summary>Adds a value to the document with the encoding the field's kind implies.</summary>
    /// <param name="field">The field to write. Its <see cref="SchemaField.Name"/> is the Lucene field name.</param>
    /// <param name="value">
    /// The raw value, as a content query data container returns it. Empty and null values add nothing.
    /// A <see cref="SearchFieldKind.Taxonomy"/> field accepts either the raw column value or an
    /// <c>IEnumerable&lt;TagReference&gt;</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the value has been written.</returns>
    /// <remarks>Asynchronous because a taxonomy value has its tag titles resolved before it is written.</remarks>
    public Task AddFieldAsync(SchemaField field, object? value, CancellationToken cancellationToken) =>
        strategy.AddValue(document, field, value, LanguageName, cancellationToken);

    /// <summary>Adds resolved tag references to the document as a facet dimension.</summary>
    /// <param name="field">The taxonomy field to write.</param>
    /// <param name="tags">The tag references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the tags have been written.</returns>
    public Task AddTaxonomyAsync(SchemaField field, IEnumerable<TagReference> tags, CancellationToken cancellationToken) =>
        strategy.AddTags(document, field, tags, LanguageName, cancellationToken);
}
