using Kentico.Xperience.Admin.Base.Forms;

using XpSearch.Admin.Forms;
using XpSearch.Core.Abstractions;
using XpSearch.Widgets;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterFormComponentConfigurator(
    XpSearchWidgetConstants.FacetAttributeConfiguratorIdentifier,
    typeof(FacetAttributeConfigurator))]

namespace XpSearch.Admin.Forms;

/// <summary>
/// Fills a facet attribute drop-down with the facetable fields of the index selected in the same
/// dialog, and hides the field while no index is selected (spec §7.4).
/// </summary>
/// <remarks>
/// Dependent-field pattern from
/// https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/editing-components/configure-editing-component-state.
/// Registered under a string identifier so live-site widget properties can reference it without a
/// dependency on <c>Kentico.Xperience.Admin</c>.
/// </remarks>
public sealed class FacetAttributeConfigurator : FormComponentConfigurator<DropDownComponent>
{
    private readonly IIndexSchemaProvider schemaProvider;

    /// <summary>Initializes a new instance of the <see cref="FacetAttributeConfigurator"/> class.</summary>
    /// <param name="schemaProvider">Supplies the schema of the selected index.</param>
    public FacetAttributeConfigurator(IIndexSchemaProvider schemaProvider)
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

        formFieldValueProvider.TryGet<string>(nameof(XpSearchMountWidgetProperties.Index), out string? indexName);

        string? options = await FacetAttributeOptions.BuildOptionsAsync(schemaProvider, indexName, cancellationToken);

        if (options is null)
        {
            formComponent.VisibilityConditions.Add(new AlwaysInvisible());
            return;
        }

        formComponent.Properties.Options = options;
    }
}

/// <summary>A visibility condition that never shows the field, used to hide a dependent drop-down.</summary>
public sealed class AlwaysInvisible : VisibilityCondition
{
    /// <inheritdoc />
    public override bool Evaluate(IFormFieldValueProvider formFieldValueProvider) => false;
}
