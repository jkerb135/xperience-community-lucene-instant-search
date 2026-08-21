using System.Globalization;
using System.Text.Json;

using Kentico.Xperience.Lucene.Core;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Indexing;
using XpSearch.Ingestion.Contract;

using CoreSchemaField = XpSearch.Core.Abstractions.SchemaField;

namespace XpSearch.Ingestion.Schema;

/// <summary>
/// A pushed document after validation: the normalized body, or the reasons it was rejected.
/// </summary>
/// <param name="Id">The document's identifier.</param>
/// <param name="Source">The provenance the document is stored under.</param>
/// <param name="Attributes">The body with every value coerced to the type its schema field declares.</param>
/// <param name="Errors">Why the document was rejected. Empty when it was accepted.</param>
public sealed record ValidatedDocument(
    string Id,
    string Source,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<IngestionError> Errors);

/// <summary>
/// Validates a pushed document against an index schema (spec §10.3). Coercion is narrow and explicit:
/// a string becomes a number, a date or a boolean only when it parses unambiguously, and everything
/// else is a rejection rather than a guess - a silently coerced value produces facets that
/// mysteriously do not work, which costs far more to debug than a clear error.
/// </summary>
public static class DocumentValidator
{
    /// <summary>The attribute names a pushed document may not carry, because the writer owns them.</summary>
    public static readonly IReadOnlySet<string> ReservedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BaseDocumentProperties.ID,
        BaseDocumentProperties.ITEM_GUID,
        LuceneFieldNames.SourceField
    };

    /// <summary>Validates and normalizes one document.</summary>
    /// <param name="schema">The index's ingestion schema.</param>
    /// <param name="id">The document's identifier, as pushed.</param>
    /// <param name="source">The document's <c>_source</c>, or <see langword="null"/> to use the default.</param>
    /// <param name="attributes">The document body.</param>
    /// <param name="defaultSource">The source to store the document under when it declares none.</param>
    /// <returns>The validated document.</returns>
    public static ValidatedDocument Validate(
        IngestionSchema schema,
        string? id,
        string? source,
        IReadOnlyDictionary<string, JsonElement> attributes,
        string defaultSource)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(attributes);

        var errors = new List<IngestionError>();
        var normalized = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add(Error(id, "id", "id is required and must not be empty."));
        }

        if (string.Equals(source, LuceneFieldNames.XperienceSource, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(Error(id, LuceneFieldNames.SourceField, $"'{LuceneFieldNames.XperienceSource}' is reserved for content indexed from Xperience."));
        }

        foreach (var (name, value) in attributes)
        {
            if (ReservedAttributes.Contains(name))
            {
                errors.Add(Error(id, name, $"'{name}' is reserved and is written by the ingestion API itself."));
                continue;
            }

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                // An explicit null is how a caller drops an attribute, not an error.
                continue;
            }

            var field = schema.Fields.Find(name);

            if (field is null)
            {
                if (!schema.AllowDynamicFields)
                {
                    errors.Add(Error(id, name, $"Unknown field '{name}'. Declare it in the index schema, or enable allowDynamicFields for this index."));
                    continue;
                }

                normalized[name] = value;
                continue;
            }

            if (Coerce(field, value) is { } coerced)
            {
                normalized[name] = coerced;
            }
            else
            {
                errors.Add(Error(id, name, $"Field '{name}' is declared as {Describe(field.Kind)} but the value is {Describe(value)}. Send the declared type; only an unambiguous string is converted."));
            }
        }

        return new ValidatedDocument(id ?? string.Empty, source ?? defaultSource, normalized, errors);
    }

    /// <summary>Describes a schema field's kind the way the wire contract spells it.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The contract's type name.</returns>
    public static string Describe(SearchFieldKind kind) => kind switch
    {
        SearchFieldKind.Text => "text",
        SearchFieldKind.Keyword => "string",
        SearchFieldKind.Number => "number",
        SearchFieldKind.Date => "date",
        SearchFieldKind.Boolean => "boolean",
        _ => "string[]"
    };

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"the string \"{Truncate(value.GetString())}\"",
        JsonValueKind.Number => $"the number {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => $"the boolean {value.GetRawText()}",
        JsonValueKind.Array => "an array",
        _ => "an object"
    };

    private static string Truncate(string? value) =>
        value is { Length: > 40 } ? value[..40] + "…" : value ?? string.Empty;

    private static JsonElement? Coerce(CoreSchemaField field, JsonElement value) => field.Kind switch
    {
        SearchFieldKind.Text or SearchFieldKind.Keyword => value.ValueKind == JsonValueKind.String ? value : null,
        SearchFieldKind.Number => CoerceNumber(value),
        SearchFieldKind.Date => CoerceDate(value),
        SearchFieldKind.Boolean => CoerceBoolean(value),
        _ => CoerceStringArray(value)
    };

    private static JsonElement? CoerceNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? JsonSerializer.SerializeToElement(number)
                : null;
    }

    private static JsonElement? CoerceDate(JsonElement value)
    {
        // Dates are indexed as Unix epoch seconds, the same encoding the query pipeline filters on.
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt64(out _) ? value : null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var date)
                ? JsonSerializer.SerializeToElement(date.ToUnixTimeSeconds())
                : null;
    }

    private static JsonElement? CoerceBoolean(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value;
        }

        // 0 and 1 are deliberately not booleans: a numeric flag is exactly the ambiguity spec §10.3
        // asks to reject.
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)
            ? JsonSerializer.SerializeToElement(parsed)
            : null;
    }

    private static JsonElement? CoerceStringArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return JsonSerializer.SerializeToElement(new[] { value.GetString()! });
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) ? value : null;
    }

    private static IngestionError Error(string? id, string field, string message) =>
        new() { Id = string.IsNullOrEmpty(id) ? null : id, Field = field, Message = message };
}
