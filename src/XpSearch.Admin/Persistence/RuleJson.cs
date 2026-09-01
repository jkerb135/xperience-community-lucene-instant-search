using System.Text.Json;
using System.Text.Json.Serialization;

using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Persistence;

/// <summary>
/// How a rule's <c>if</c> and <c>then</c> are stored: two JSON columns on
/// <see cref="XpSearchRuleInfo"/> (ADR-0022 addendum, unit CR-4b).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XpSearchRuleInfo.RuleConditions"/> holds one object. <c>query</c> is left out when the
/// rule matches any query; the other three members are always written, so "anyone, any language"
/// reads as <c>{"filters":[],"contactGroup":"","language":""}</c>:
/// </para>
/// <code>
/// {"query":{"operator":"contains","pattern":"grinder","matchAnalyzed":true},
///  "filters":[{"attribute":"ProductFieldCategory","value":"Grinders"}],
///  "contactGroup":"CoffeeGrinders","language":"en"}
/// </code>
/// <para>
/// <see cref="XpSearchRuleInfo.RuleActions"/> holds an array, in the order the rule applies
/// them, each tagged with the <c>type</c> discriminator declared on <see cref="RuleAction"/>:
/// </para>
/// <code>
/// [{"type":"pin","targetId":"doc-1:en","position":1},
///  {"type":"customData","json":"{\"banner\":\"Grinder week\"}"}]
/// </code>
/// <para>
/// Reading never throws. A column that a hand edit or a failed write left unparseable comes back as
/// "no conditions" or "no actions", which makes the rule inert rather than taking the whole
/// index's tuning down with it - the same tolerance the ingestion log has for a bad row.
/// </para>
/// </remarks>
public static class RuleJson
{
    /// <summary>The serializer settings both columns are written with and read with.</summary>
    /// <remarks>
    /// Only nulls are omitted - "the rule matches any query" is the absence of <c>query</c>, not a
    /// <c>null</c> spelled out. Everything else is written even when empty, so what a support
    /// engineer reads in a database client is the whole shape rather than whatever happened to
    /// differ from a default.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The stored form of a rule that says nothing about when it applies.</summary>
    public static RuleConditions NoConditions { get; } = new(null, [], string.Empty, string.Empty);

    /// <summary>Writes the <c>if</c> of a rule.</summary>
    /// <param name="conditions">The conditions.</param>
    /// <returns>The JSON to store.</returns>
    public static string Write(RuleConditions conditions) =>
        JsonSerializer.Serialize(conditions ?? NoConditions, Options);

    /// <summary>Writes the <c>then</c> of a rule.</summary>
    /// <param name="actions">The actions, in the order they are applied.</param>
    /// <returns>The JSON to store.</returns>
    public static string Write(IReadOnlyList<RuleAction> actions) =>
        JsonSerializer.Serialize(actions ?? [], Options);

    /// <summary>Reads the <c>if</c> of a rule back.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The conditions, or <see cref="NoConditions"/> when the column is empty or unreadable.</returns>
    public static RuleConditions ReadConditions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NoConditions;
        }

        try
        {
            var read = JsonSerializer.Deserialize<RuleConditions>(json, Options);

            return read is null ? NoConditions : read with { Filters = read.Filters ?? [] };
        }
        catch (JsonException)
        {
            return NoConditions;
        }
    }

    /// <summary>Reads the <c>then</c> of a rule back.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The actions, or an empty list when the column is empty or unreadable.</returns>
    public static IReadOnlyList<RuleAction> ReadActions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RuleAction>>(json, Options) is { } read
                ? [.. read.Where(action => action is not null)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Tells whether a text is a JSON object, which is all <see cref="RuleAction.CustomData"/> accepts.</summary>
    /// <param name="json">The text to check.</param>
    /// <returns><see langword="true"/> when the text parses to a JSON object.</returns>
    public static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);

            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
