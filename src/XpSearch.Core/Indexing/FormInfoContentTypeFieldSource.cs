using CMS.DataEngine;
using CMS.FormEngine;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Detects searchable fields by reading a content type's class form definition
/// (<c>DataClassInfo.ClassFormDefinition</c>) through <see cref="FormInfo"/>.
/// </summary>
/// <remarks>
/// Field data types are the ones listed at
/// https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types;
/// the constants live in <see cref="FieldDataType"/>. A field whose data type has no useful search
/// meaning - assets, references, booleans, GUIDs - is skipped rather than indexed as text.
/// </remarks>
public sealed class FormInfoContentTypeFieldSource : IContentTypeFieldSource
{
    private readonly IInfoProvider<DataClassInfo> dataClassProvider;
    private readonly XpSearchIndexingOptions options;

    /// <summary>Initializes a new instance of the <see cref="FormInfoContentTypeFieldSource"/> class.</summary>
    /// <param name="dataClassProvider">Provider of content type definitions.</param>
    /// <param name="options">Per-field overrides supplied by the developer.</param>
    public FormInfoContentTypeFieldSource(IInfoProvider<DataClassInfo> dataClassProvider, XpSearchIndexingOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataClassProvider);
        ArgumentNullException.ThrowIfNull(options);

        this.dataClassProvider = dataClassProvider;
        this.options = options;
    }

    /// <inheritdoc />
    public IReadOnlyList<SchemaField> GetFields(string contentTypeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);

        var dataClass = dataClassProvider.Get()
            .WhereEquals(nameof(DataClassInfo.ClassName), contentTypeName)
            .FirstOrDefault();

        if (dataClass is null || string.IsNullOrWhiteSpace(dataClass.ClassFormDefinition))
        {
            return [];
        }

        return Detect(dataClass.ClassFormDefinition, contentTypeName, options);
    }

    /// <summary>Detects the schema fields of one class form definition.</summary>
    /// <param name="classFormDefinition">The XML of <c>DataClassInfo.ClassFormDefinition</c>.</param>
    /// <param name="contentTypeName">Class name the definition belongs to, passed to the overrides.</param>
    /// <param name="options">Per-field overrides supplied by the developer.</param>
    /// <returns>The detected fields.</returns>
    public static IReadOnlyList<SchemaField> Detect(
        string classFormDefinition,
        string contentTypeName,
        XpSearchIndexingOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(classFormDefinition);
        ArgumentNullException.ThrowIfNull(options);

        var form = new FormInfo(classFormDefinition);
        var fields = new List<SchemaField>();

        foreach (var formField in form.GetFields(visible: true, invisible: true))
        {
            if (formField.System || formField.PrimaryKey || formField.IsDummyField)
            {
                continue;
            }

            var detected = FromDataType(formField.Name, formField.DataType);

            if (detected is null)
            {
                continue;
            }

            var configured = options.Apply(contentTypeName, detected);

            if (configured is not null)
            {
                fields.Add(configured);
            }
        }

        return fields;
    }

    private static SchemaField? FromDataType(string name, string? dataType) => dataType switch
    {
        FieldDataType.Taxonomy => new SchemaField(name, SearchFieldKind.Taxonomy, Searchable: true, Facetable: true, Sortable: false, Retrievable: true),
        FieldDataType.Text => new SchemaField(name, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: true, Retrievable: true),
        FieldDataType.LongText or FieldDataType.RichTextHTML => new SchemaField(name, SearchFieldKind.Text, Searchable: true, Facetable: false, Sortable: false, Retrievable: true),
        FieldDataType.Integer or FieldDataType.LongInteger or FieldDataType.Double or FieldDataType.Decimal =>
            new SchemaField(name, SearchFieldKind.Number, Searchable: false, Facetable: false, Sortable: true, Retrievable: true),
        FieldDataType.DateTime or FieldDataType.Date =>
            new SchemaField(name, SearchFieldKind.Date, Searchable: false, Facetable: false, Sortable: true, Retrievable: true),
        _ => null
    };
}
