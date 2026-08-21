using XpSearch.Core.Abstractions;

namespace XpSearch.Ingestion.Schema;

/// <summary>
/// Declares an index's ingestion schema on its indexing strategy class (spec §10.3). Fields are
/// declared with <see cref="XpSearchFieldAttribute"/>; this attribute only carries the settings that
/// belong to the index as a whole.
/// </summary>
/// <example>
/// <code>
/// [XpSearchSchema(AllowDynamicFields = false)]
/// [XpSearchField("title", SearchFieldKind.Text, Searchable = true, Sortable = true)]
/// [XpSearchField("price", SearchFieldKind.Number, Sortable = true)]
/// public class ProductStrategy : XpSearchIndexingStrategy { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class XpSearchSchemaAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether a pushed document may carry attributes the schema does
    /// not declare. Defaults to <see langword="false"/>: an unknown field is a 400, because a typo in
    /// a field name is far cheaper to find at push time than as a facet that mysteriously never works.
    /// </summary>
    public bool AllowDynamicFields { get; set; }
}

/// <summary>
/// Declares one field of an index's ingestion schema (spec §10.3). Repeat it per field on the
/// strategy class.
/// </summary>
/// <remarks>
/// A declared field wins over the same field detected from an Xperience content type, so a schema can
/// re-flag a detected field. An external-only index has no content types, and its schema is whatever
/// these attributes declare plus the base fields every document carries.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class XpSearchFieldAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchFieldAttribute"/> class.</summary>
    /// <param name="name">Attribute name, as pushed documents and queries spell it.</param>
    /// <param name="kind">What the field holds.</param>
    public XpSearchFieldAttribute(string name, SearchFieldKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        Kind = kind;
    }

    /// <summary>Gets the attribute name.</summary>
    public string Name { get; }

    /// <summary>Gets what the field holds.</summary>
    public SearchFieldKind Kind { get; }

    /// <summary>Gets or sets a value indicating whether free-text queries match against the field.</summary>
    public bool Searchable { get; set; }

    /// <summary>Gets or sets a value indicating whether the field can be filtered on with <c>filters.facets</c>.</summary>
    public bool Facetable { get; set; }

    /// <summary>Gets or sets a value indicating whether the field can be used as a sort key.</summary>
    public bool Sortable { get; set; }

    /// <summary>Gets or sets a value indicating whether the field is stored and can be returned. Defaults to <see langword="true"/>.</summary>
    public bool Retrievable { get; set; } = true;

    /// <summary>Gets or sets the index-time boost applied to the searchable field. Defaults to 1.</summary>
    public float Boost { get; set; } = 1f;

    /// <summary>Converts the declaration into a schema field.</summary>
    /// <returns>The schema field.</returns>
    public SchemaField ToSchemaField() => new(Name, Kind, Searchable, Facetable, Sortable, Retrievable, Boost);
}
