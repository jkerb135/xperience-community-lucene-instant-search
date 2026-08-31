using Kentico.Xperience.Admin.Base.Forms;

using XpSearch.Admin.Forms;
using XpSearch.Admin.UIPages;
using XpSearch.Core;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Facets;

[assembly: RegisterFormComponentConfigurator(
    XpSearchConstants.WeightFieldConfiguratorIdentifier,
    typeof(WeightFieldConfigurator))]

namespace XpSearch.Admin.Forms;

/// <summary>
/// Fills the field weight form's Field drop-down with the searchable fields of the index the page is
/// scoped to, so an editor picks a field from the schema instead of typing an attribute name
/// (spec §8.2).
/// </summary>
/// <remarks>
/// Unlike <see cref="FacetAttributeConfigurator"/>, which depends on an index the editor chose in the
/// same dialog, this form's index comes from the URL. It still reaches the configurator through
/// <see cref="IFormFieldValueProvider"/>, because <c>IIndexScopedModel.IndexName</c> is a real - if
/// read-only - field of the form, and it is ordered before the field it feeds, which is what
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/editing-components/configure-editing-component-state
/// requires of a dependency.
/// </remarks>
public sealed class WeightFieldConfigurator : FormComponentConfigurator<DropDownComponent>
{
    private readonly IIndexSchemaProvider schemaProvider;

    /// <summary>Initializes a new instance of the <see cref="WeightFieldConfigurator"/> class.</summary>
    /// <param name="schemaProvider">Supplies the schema of the scoped index.</param>
    public WeightFieldConfigurator(IIndexSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        this.schemaProvider = schemaProvider;
    }

    /// <inheritdoc />
    public override async Task Configure(
        DropDownComponent formComponent,
        IFormFieldValueProvider formFieldValueProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formComponent);
        ArgumentNullException.ThrowIfNull(formFieldValueProvider);

        formFieldValueProvider.TryGet<string>(nameof(IIndexScopedModel.IndexName), out string? indexName);

        string? options = await FacetAttributeOptions.BuildOptionsAsync(
            schemaProvider, indexName, cancellationToken, FacetAttributeOptions.IsWeightable);

        formComponent.Properties.Options = options ?? string.Empty;
    }

    /// <summary>
    /// Prepends the stored value as its own option when the schema does not offer it. Without this a
    /// weight on a field the index no longer has renders as an empty drop-down, and the next save
    /// rewrites the row to whatever the editor picks, losing the record of what was tuned. Called
    /// from the edit page rather than from <see cref="Configure"/> because a form component
    /// configurator sees the fields that precede its own, not its own value.
    /// </summary>
    /// <param name="options">The option lines built from the schema, or <see langword="null"/>.</param>
    /// <param name="stored">The value the form is rendering.</param>
    /// <returns>The option lines to hand the drop-down.</returns>
    public static string WithStoredValue(string? options, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return options ?? string.Empty;
        }

        string[] lines = options?.Split("\r\n", StringSplitOptions.RemoveEmptyEntries) ?? [];

        return lines.Any(line => line.StartsWith($"{stored};", StringComparison.OrdinalIgnoreCase))
            ? options!
            : string.Join("\r\n", lines.Prepend($"{stored};{stored}"));
    }
}
