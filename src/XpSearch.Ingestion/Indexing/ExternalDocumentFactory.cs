using System.Globalization;
using System.Text.Json;

using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Documents;
using Lucene.Net.Util;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Ingestion.Abstractions;

namespace XpSearch.Ingestion.Indexing;

/// <summary>
/// Turns a stored external document into the Lucene document the query pipeline reads.
/// </summary>
/// <remarks>
/// The field names and encodings are the ones <c>XpSearch.Core</c>'s indexing strategy writes for
/// Xperience content, so one query serves both: analyzed text under the attribute name, numbers and
/// dates as numeric fields with doc values, sortable attributes under the <c>_sort</c> suffix.
/// <para>
/// Two identifier fields are written. <c>ID</c> is what a result is addressed by on the wire;
/// <c>ItemGuid</c> is the term <c>DefaultLuceneClient</c> deletes and replaces documents by
/// (<c>DeleteRecordsInternal</c> builds a <c>Term("ItemGuid", …)</c>), so external documents are
/// addressable by the integration's own client without reaching for an index writer.
/// </para>
/// <para>
/// Taxonomy-typed (<c>string[]</c>) attributes are written as plain terms rather than facet fields:
/// facet fields would have to be registered in the strategy's <c>FacetsConfig</c> before the client
/// builds them, and an external document has no strategy. Filtering on such an attribute works;
/// facet counts do not include externally pushed documents (see KNOWN-LIMITATIONS).
/// </para>
/// </remarks>
public static class ExternalDocumentFactory
{
    /// <summary>Builds the Lucene document for a stored external document.</summary>
    /// <param name="record">The stored document.</param>
    /// <param name="schema">The index schema, which decides how each attribute is encoded.</param>
    /// <returns>The Lucene document.</returns>
    public static Document Create(ExternalDocumentRecord record, IndexSchema schema)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(schema);

        var document = new Document
        {
            new StringField(BaseDocumentProperties.ID, record.Id, Field.Store.YES),
            new StringField(BaseDocumentProperties.ITEM_GUID, record.Id, Field.Store.YES),
            new StringField(LuceneFieldNames.SourceField, record.Source, Field.Store.YES)
        };

        using var body = JsonDocument.Parse(record.Json);

        foreach (var property in body.RootElement.EnumerateObject())
        {
            var field = schema.Find(property.Name);

            if (field is null)
            {
                AddDynamic(document, property.Name, property.Value);
                continue;
            }

            Add(document, field, property.Value);
        }

        return document;
    }

    private static void Add(Document document, SchemaField field, JsonElement value)
    {
        switch (field.Kind)
        {
            case SearchFieldKind.Text when value.ValueKind == JsonValueKind.String:
                AddText(document, field, value.GetString()!);
                break;

            case SearchFieldKind.Keyword when value.ValueKind == JsonValueKind.String:
                AddKeyword(document, field, value.GetString()!);
                break;

            case SearchFieldKind.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                document.Add(new StringField(field.Name, value.GetBoolean() ? "true" : "false", Field.Store.YES));
                break;

            case SearchFieldKind.Number when value.TryGetDouble(out double number):
                document.Add(new DoubleField(field.Name, number, Field.Store.YES));
                document.Add(new DoubleDocValuesField(field.Name, number));
                break;

            case SearchFieldKind.Date when value.TryGetInt64(out long epochSeconds):
                document.Add(new Int64Field(field.Name, epochSeconds, Field.Store.YES));
                document.Add(new NumericDocValuesField(field.Name, epochSeconds));
                break;

            case SearchFieldKind.Taxonomy when value.ValueKind == JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AddTag(document, field, item.GetString()!);
                }

                break;

            default:
                // Validation coerced every value to its declared type, so anything left here is a
                // value the schema has no encoding for. Dropping it beats writing a wrong encoding.
                break;
        }
    }

    private static void AddText(Document document, SchemaField field, string value)
    {
        document.Add(new TextField(field.Name, value, Field.Store.YES));

        if (field.Sortable)
        {
            document.Add(new SortedDocValuesField(LuceneFieldNames.SortFieldName(field), new BytesRef(value)));
        }
    }

    private static void AddKeyword(Document document, SchemaField field, string value)
    {
        document.Add(new StringField(field.Name, value, Field.Store.YES));

        if (field.Sortable)
        {
            document.Add(new SortedDocValuesField(LuceneFieldNames.SortFieldName(field), new BytesRef(value)));
        }
    }

    private static void AddTag(Document document, SchemaField field, string value)
    {
        document.Add(new StringField(field.Name, value, Field.Store.YES));
        document.Add(new TextField(LuceneFieldNames.SearchFieldName(field), value, Field.Store.NO));

        // External sources have no tag titles, so the code name doubles as the label; the query side
        // reads both halves of this term to build a facet value.
        document.Add(new StringField(
            LuceneFieldNames.LabelFieldName(field),
            LuceneFieldNames.ComposeLabel(value, value),
            Field.Store.NO));
    }

    private static void AddDynamic(Document document, string name, JsonElement value)
    {
        // Only reached with allowDynamicFields: the JSON type is all there is to go on.
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                document.Add(new TextField(name, value.GetString()!, Field.Store.YES));
                break;

            case JsonValueKind.Number when value.TryGetDouble(out double number):
                document.Add(new DoubleField(name, number, Field.Store.YES));
                document.Add(new DoubleDocValuesField(name, number));
                break;

            case JsonValueKind.True or JsonValueKind.False:
                document.Add(new StringField(name, value.GetBoolean() ? "true" : "false", Field.Store.YES));
                break;

            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String))
                {
                    document.Add(new StringField(name, item.GetString()!, Field.Store.YES));
                }

                break;

            default:
                document.Add(new StoredField(name, value.GetRawText()));
                break;
        }
    }

    /// <summary>Serializes a document body the way it is persisted: a JSON object of attributes.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The JSON text.</returns>
    public static string ToJson(IReadOnlyDictionary<string, JsonElement> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        // Ordered, so the content hash of the same body is stable whatever order the caller sent it in.
        return JsonSerializer.Serialize(
            attributes.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>Hashes a document body, so an unchanged re-push is recognisable.</summary>
    /// <param name="json">The serialized body.</param>
    /// <returns>The hash, lower-case hexadecimal.</returns>
    public static string Hash(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
