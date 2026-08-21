using CMS.DataEngine;
using CMS.FormEngine;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Detects searchable fields by reading a content type's class form definition
/// (<c>DataClassInfo.ClassFormDefinition</c>) through <see cref="FormInfo"/>, together with the fields
/// the content type inherits from the reusable field schemas it references.
/// </summary>
/// <remarks>
/// <para>
/// Field data types are the ones listed at
/// https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types;
/// the constants live in <see cref="FieldDataType"/>. A field whose data type has no useful search
/// meaning - assets, references, booleans, GUIDs - is skipped rather than indexed as text.
/// </para>
/// <para>
/// A content type's own definition holds only a <c>&lt;schema&gt;</c> reference per reusable field
/// schema, never the schema's fields. The fields themselves are defined on the
/// <c>CMS.ContentItemCommonData</c> class, each carrying a <c>kxp_schema_identifier</c> property whose
/// value is the <c>guid</c> of the schema it belongs to - see
/// https://docs.kentico.com/guides/development/advanced-content/convert-content-to-reusable-schemas
/// ("Consider references from other content types") and
/// https://docs.kentico.com/documentation/developers-and-admins/development/content-types/reusable-field-schemas.
/// </para>
/// </remarks>
public sealed class FormInfoContentTypeFieldSource : IContentTypeFieldSource
{
    /// <summary>Class name of the class that defines the fields of every reusable field schema.</summary>
    public const string ReusableFieldSchemaClassName = "CMS.ContentItemCommonData";

    /// <summary>Field property naming the reusable field schema a schema field belongs to.</summary>
    private const string SchemaIdentifierProperty = "kxp_schema_identifier";

    private readonly IDataClassDefinitionSource definitions;
    private readonly XpSearchIndexingOptions options;
    private readonly ILogger<FormInfoContentTypeFieldSource> logger;

    /// <summary>Initializes a new instance of the <see cref="FormInfoContentTypeFieldSource"/> class.</summary>
    /// <param name="definitions">Source of class form definitions.</param>
    /// <param name="options">Per-field overrides supplied by the developer.</param>
    /// <param name="logger">Logs field names a content type and a reusable field schema both define.</param>
    public FormInfoContentTypeFieldSource(
        IDataClassDefinitionSource definitions,
        XpSearchIndexingOptions options,
        ILogger<FormInfoContentTypeFieldSource> logger)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.definitions = definitions;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SchemaField> GetFields(string contentTypeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);

        string? definition = ClassFormDefinition(contentTypeName);

        if (string.IsNullOrWhiteSpace(definition))
        {
            return [];
        }

        return Detect(definition, contentTypeName, options, ClassFormDefinition(ReusableFieldSchemaClassName), logger);
    }

    /// <summary>Detects the schema fields of one class form definition.</summary>
    /// <param name="classFormDefinition">The XML of the content type's <c>DataClassInfo.ClassFormDefinition</c>.</param>
    /// <param name="contentTypeName">Class name the definition belongs to, passed to the overrides.</param>
    /// <param name="options">Per-field overrides supplied by the developer.</param>
    /// <param name="reusableFieldSchemaDefinition">
    /// The XML of the <c>CMS.ContentItemCommonData</c> class form definition, which holds the fields of
    /// every reusable field schema. When null, only the content type's own fields are detected.
    /// </param>
    /// <param name="logger">Logs a field name the content type and one of its schemas both define.</param>
    /// <returns>The detected fields: the content type's own first, then the ones its schemas contribute.</returns>
    public static IReadOnlyList<SchemaField> Detect(
        string classFormDefinition,
        string contentTypeName,
        XpSearchIndexingOptions options,
        string? reusableFieldSchemaDefinition = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(classFormDefinition);
        ArgumentNullException.ThrowIfNull(options);

        var form = new FormInfo(classFormDefinition);
        var fields = new List<SchemaField>();

        // Every name the content type defines itself, indexed or not: a schema field may not take one.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var formField in Indexable(form))
        {
            taken.Add(formField.Name);
            Add(fields, formField, contentTypeName, options);
        }

        foreach (var formField in SchemaFields(form, reusableFieldSchemaDefinition))
        {
            if (!taken.Add(formField.Name))
            {
                (logger ?? NullLogger.Instance).LogWarning(
                    "Field {FieldName} is defined both by content type {ContentType} and by a reusable field schema it uses. " +
                    "This is a configuration error; the content type's own field is indexed and the schema field is ignored.",
                    formField.Name,
                    contentTypeName);

                continue;
            }

            Add(fields, formField, contentTypeName, options);
        }

        return fields;
    }

    /// <summary>Gets the fields of every reusable field schema the content type references.</summary>
    private static IEnumerable<FormFieldInfo> SchemaFields(FormInfo form, string? reusableFieldSchemaDefinition)
    {
        if (string.IsNullOrWhiteSpace(reusableFieldSchemaDefinition))
        {
            yield break;
        }

        // A content type's definition carries one <schema> element per reusable field schema it uses.
        // Its name attribute is not the schema's code name, so the guid is what identifies it.
        var referenced = form.GetFields<FormSchemaInfo>().Select(schema => schema.Guid).ToHashSet();

        if (referenced.Count == 0)
        {
            yield break;
        }

        foreach (var formField in Indexable(new FormInfo(reusableFieldSchemaDefinition)))
        {
            if (formField.Properties?[SchemaIdentifierProperty] is string identifier
                && Guid.TryParse(identifier, out var schemaGuid)
                && referenced.Contains(schemaGuid))
            {
                yield return formField;
            }
        }
    }

    private static IEnumerable<FormFieldInfo> Indexable(FormInfo form) =>
        form.GetFields(visible: true, invisible: true)
            .Where(formField => !formField.System && !formField.PrimaryKey && !formField.IsDummyField);

    private static void Add(List<SchemaField> fields, FormFieldInfo formField, string contentTypeName, XpSearchIndexingOptions options)
    {
        if (FromDataType(formField.Name, formField.DataType) is not { } detected)
        {
            return;
        }

        if (options.Apply(contentTypeName, detected) is { } configured)
        {
            fields.Add(configured);
        }
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

    private string? ClassFormDefinition(string className) => definitions.GetFormDefinition(className);
}
