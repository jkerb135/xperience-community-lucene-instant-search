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
    /// Builds the <c>value;label</c> option lines of the facetable fields of an index.
    /// </summary>
    /// <param name="schemaProvider">Supplies the index schema.</param>
    /// <param name="indexName">Code name of the selected index, or empty when nothing is selected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The option lines in the format <c>DropDownComponent.Options</c> expects, or
    /// <see langword="null"/> when no index is selected, the index is unknown, or it has no facetable
    /// field - in each of those cases the field should be hidden.
    /// </returns>
    public static async Task<string?> BuildOptionsAsync(
        IIndexSchemaProvider schemaProvider,
        string? indexName,
        CancellationToken cancellationToken)
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
            .Where(field => field.Facetable)
            .Select(field => $"{field.Name};{field.Name}")
            .ToList();

        return lines.Count == 0 ? null : string.Join("\r\n", lines);
    }
}
