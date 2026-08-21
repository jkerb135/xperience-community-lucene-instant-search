using System.Globalization;
using System.Text.Json;

using CMS.ContentEngine;
using CMS.DataEngine;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>
/// Registers the <c>taxonomy</c> field data type the way an Xperience application does at startup, so
/// the conversion <see cref="XpSearch.Core.Indexing.XpSearchIndexingStrategy"/> asks
/// <see cref="DataTypeManager"/> for is available in a test process.
/// </summary>
/// <remarks>
/// <para>
/// The platform registers it from <c>CMS.ContentEngine.TaxonomyDataType.RegisterDataTypes</c>, which is
/// internal and only runs when the CMS module initializes; nothing in a bare test host does that, and
/// <c>ConvertToSystemType</c> then fails with "SQL type 'taxonomy' is not registered". Registering the
/// same shape here - field data type <c>taxonomy</c>, C# type <c>IEnumerable&lt;TagReference&gt;</c>,
/// a JSON string in the column - is the documented way to register a data type
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/add-custom-data-types)
/// and exercises exactly the lookup the strategy performs.
/// </para>
/// <para>
/// The stored form is a JSON array of tag references, verified against the platform's own converter:
/// <c>[{"Identifier":"…"},{"Identifier":"…"}]</c>.
/// </para>
/// </remarks>
internal static class TaxonomyDataType
{
    private static readonly object Gate = new();

    /// <summary>Serializes tag identifiers the way the taxonomy column stores them.</summary>
    internal static string ColumnValue(params Guid[] identifiers) =>
        JsonSerializer.Serialize(identifiers.Select(identifier => new TagReference { Identifier = identifier }));

    /// <summary>Registers the data type once per process.</summary>
    internal static void Ensure()
    {
        lock (Gate)
        {
            if (DataTypeManager.GetDataType(TypeEnum.Field, FieldDataType.Taxonomy) is not null)
            {
                return;
            }

            DataTypeManager.RegisterDataTypes(new DataType<IEnumerable<TagReference>>(
                sqlType: "nvarchar(max)",
                fieldType: FieldDataType.Taxonomy,
                schemaType: "xs:string",
                conversionFunc: FromColumn,
                dbConversionFunc: ToColumn,
                textSerializer: new DefaultDataTypeTextSerializer(FieldDataType.Taxonomy)));
        }
    }

    private static IEnumerable<TagReference> FromColumn(object value, IEnumerable<TagReference> defaultValue, CultureInfo culture) =>
        value is string json && !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<List<TagReference>>(json) ?? defaultValue
            : defaultValue;

    private static object ToColumn(IEnumerable<TagReference> value, object defaultValue, CultureInfo culture) =>
        value is null ? defaultValue : JsonSerializer.Serialize(value);
}
