using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Facets;

/// <summary>
/// Builds the option list of the facet attribute drop-down from an index's real schema, so an editor
/// picks a facetable field instead of typing a field name (spec §7.4).
/// </summary>
/// <remarks>
/// The logic lives here rather than in the form component configurator for two reasons: it can be
/// exercised without the administration packages, and <c>XpSearch.Admin</c> - which hosts the
/// configurator - must not depend on <c>XpSearch.Widgets</c> (spec §2.2). The configurator is a shell
/// around this method.
/// </remarks>
public static class FacetAttributeOptions
{
    /// <summary>
    /// Matches the fields a range filter can work on: a Lucene <c>double</c> or an epoch-seconds date,
    /// both filterable with <c>filters.numeric</c>.
    /// </summary>
    /// <param name="field">The schema field.</param>
    /// <returns><see langword="true"/> when the field holds a number or a date.</returns>
    public static bool IsRangeFilterable(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind is SearchFieldKind.Number or SearchFieldKind.Date;
    }

    /// <summary>
    /// Matches the fields a field weight can affect. <c>BuildQueryStage.Boosts</c> only visits
    /// searchable fields, so a weight on any other field is silently ignored at query time.
    /// </summary>
    /// <param name="field">The schema field.</param>
    /// <returns><see langword="true"/> when free-text queries match against the field.</returns>
    public static bool IsWeightable(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Searchable;
    }

    /// <summary>
    /// Builds the <c>value;label</c> option lines of the fields of an index an attribute drop-down
    /// may offer.
    /// </summary>
    /// <param name="schemaProvider">Supplies the index schema.</param>
    /// <param name="indexName">Code name of the selected index, or empty when nothing is selected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="include">
    /// Which fields to offer. Defaults to the facetable ones, which is what the facet list widget's
    /// drop-down needs; <see cref="IsRangeFilterable"/> is what the range filter's needs.
    /// </param>
    /// <returns>
    /// The option lines in the format <c>DropDownComponent.Options</c> expects, or
    /// <see langword="null"/> when no index is selected, the index is unknown, or no field matches -
    /// in each of those cases the field should be hidden.
    /// </returns>
    public static async Task<string?> BuildOptionsAsync(
        IIndexSchemaProvider schemaProvider,
        string? indexName,
        CancellationToken cancellationToken,
        Func<SchemaField, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (string.IsNullOrWhiteSpace(indexName))
        {
            return null;
        }

        IndexSchema schema;
        try
        {
            schema = await schemaProvider.GetSchemaAsync(indexName.Trim(), cancellationToken);
        }
        catch (IndexNotFoundException)
        {
            return null;
        }

        var lines = schema.Fields
            .Where(include ?? (field => field.Facetable))
            .Select(field => $"{field.Name};{field.Name}")
            .ToList();

        return lines.Count == 0 ? null : string.Join("\r\n", lines);
    }
}
