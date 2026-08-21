using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// The per-field escape hatch for auto-detection (spec §4.5f): exclude a field, rename it, or change
/// its flags and boost.
/// </summary>
/// <example>
/// <code>
/// services.AddXpSearch(options =&gt; { }, indexing =&gt; indexing
///     .Exclude("DancingGoat.ArticlePage", "ArticlePageSummary")
///     .Configure("DancingGoat.ArticlePage", "ArticleTitle", field =&gt; field with { Boost = 3f }));
/// </code>
/// </example>
public sealed class XpSearchIndexingOptions
{
    private readonly List<Func<string, SchemaField, SchemaField?>> overrides = [];
    private readonly List<FlattenedLink> flattened = [];

    /// <summary>Gets the depth <c>WithLinkedItems</c> is configured with when an item is loaded for indexing.</summary>
    /// <remarks>One by default; raised by <see cref="FlattenLinkedItems"/>.</remarks>
    public int LinkedItemsDepth { get; private set; } = 1;

    /// <summary>
    /// Flattens the content items linked from one field of a content type into that type's document
    /// (spec §10.7): every field the linked item's own content type defines is detected and indexed on
    /// the parent document under its own name.
    /// </summary>
    /// <param name="contentTypeName">Class name of the content type that holds the link, for example <c>DancingGoat.ProductPage</c>.</param>
    /// <param name="linkedFieldName">Name of its field of data type <em>Pages and reusable content</em>, for example <c>ProductPageProduct</c>.</param>
    /// <param name="linkedContentTypeNames">
    /// Class names the field can hold. Only the reported schema uses them - the document itself is
    /// mapped from whatever content type each linked item turns out to be - but the query pipeline,
    /// the ingestion schema endpoint and the admin attribute dropdown need to know the flattened
    /// fields without loading an item, so the types the field accepts have to be named here.
    /// </param>
    /// <param name="depth">
    /// Depth passed to <c>WithLinkedItems</c> when the parent item is loaded. One is enough to flatten
    /// the linked item itself; raise it when an override of the contribution hook needs the linked
    /// item's own linked items. The highest value any registration asks for wins.
    /// </param>
    /// <returns>The same instance, for chaining.</returns>
    /// <remarks>
    /// A flattened field whose name the parent content type already defines is dropped, with one
    /// warning per name: the parent's own value wins.
    /// </remarks>
    public XpSearchIndexingOptions FlattenLinkedItems(
        string contentTypeName,
        string linkedFieldName,
        IEnumerable<string> linkedContentTypeNames,
        int depth = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);
        ArgumentException.ThrowIfNullOrEmpty(linkedFieldName);
        ArgumentNullException.ThrowIfNull(linkedContentTypeNames);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        flattened.Add(new FlattenedLink(contentTypeName, linkedFieldName, [.. linkedContentTypeNames]));
        LinkedItemsDepth = Math.Max(LinkedItemsDepth, depth);

        return this;
    }

    /// <summary>Gets the flatten registrations of one content type, in registration order.</summary>
    /// <param name="contentTypeName">Class name of the content type being indexed.</param>
    /// <returns>The registrations, empty when the type flattens nothing.</returns>
    public IReadOnlyList<FlattenedLink> FlattenedLinksOf(string contentTypeName) =>
        [.. flattened.Where(link => string.Equals(link.ContentTypeName, contentTypeName, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Drops a field from the schema, so it is neither indexed nor returned.</summary>
    /// <param name="contentTypeName">Class name of the content type the field belongs to.</param>
    /// <param name="fieldName">Name of the field to drop.</param>
    /// <returns>The same instance, for chaining.</returns>
    public XpSearchIndexingOptions Exclude(string contentTypeName, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);

        return Configure(contentTypeName, fieldName, _ => null);
    }

    /// <summary>Rewrites one detected field.</summary>
    /// <param name="contentTypeName">Class name of the content type the field belongs to.</param>
    /// <param name="fieldName">Name of the field to rewrite.</param>
    /// <param name="configure">Receives the detected field and returns the replacement, or <see langword="null"/> to drop it.</param>
    /// <returns>The same instance, for chaining.</returns>
    public XpSearchIndexingOptions Configure(string contentTypeName, string fieldName, Func<SchemaField, SchemaField?> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(configure);

        overrides.Add((type, field) =>
            string.Equals(type, contentTypeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)
                ? configure(field)
                : field);

        return this;
    }

    /// <summary>Applies every registered override to a detected field.</summary>
    /// <param name="contentTypeName">Class name the field was detected on.</param>
    /// <param name="field">The detected field.</param>
    /// <returns>The field after the overrides, or <see langword="null"/> when one dropped it.</returns>
    public SchemaField? Apply(string contentTypeName, SchemaField field)
    {
        SchemaField? current = field;

        foreach (var @override in overrides)
        {
            if (current is null)
            {
                return null;
            }

            current = @override(contentTypeName, current);
        }

        return current;
    }
}

/// <summary>One <see cref="XpSearchIndexingOptions.FlattenLinkedItems"/> registration.</summary>
/// <param name="ContentTypeName">Class name of the content type that holds the link.</param>
/// <param name="LinkedFieldName">Name of the field the linked items are read from.</param>
/// <param name="LinkedContentTypeNames">Class names the field can hold, used to report the flattened fields in the parent type's schema.</param>
public sealed record FlattenedLink(
    string ContentTypeName,
    string LinkedFieldName,
    IReadOnlyList<string> LinkedContentTypeNames);
