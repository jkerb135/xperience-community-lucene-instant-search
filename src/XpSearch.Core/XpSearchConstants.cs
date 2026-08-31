namespace XpSearch.Core;

/// <summary>
/// Identifiers shared by more than one package. They live in <c>XpSearch.Core</c> because
/// <c>XpSearch.Widgets</c> declares them and <c>XpSearch.Admin</c> implements them, and neither
/// package may depend on the other (spec §2.2).
/// </summary>
public static class XpSearchConstants
{
    /// <summary>
    /// Identifier of the form component configurator that fills a facet attribute drop-down from the
    /// selected index's schema (spec §7.4). A widget properties class pairs it with a drop-down:
    /// <c>[FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]</c>.
    /// The configurator is registered under this identifier by <c>XpSearch.Admin</c>; referring to it
    /// by string rather than by type is what keeps live-site code free of a dependency on
    /// <c>Kentico.Xperience.Admin</c>.
    /// </summary>
    public const string FacetAttributeConfiguratorIdentifier = "xpsearch.facetAttribute";

    /// <summary>
    /// Identifier of the form component configurator that fills an attribute drop-down with the
    /// numeric and date fields of the selected index - the fields a range filter can work on. Used
    /// exactly like <see cref="FacetAttributeConfiguratorIdentifier"/>.
    /// </summary>
    public const string NumericAttributeConfiguratorIdentifier = "xpsearch.numericAttribute";

    /// <summary>
    /// Identifier of the form component configurator that fills the field weight form's Field
    /// drop-down with the searchable fields of the index the admin page is scoped to (spec §8.2).
    /// </summary>
    public const string WeightFieldConfiguratorIdentifier = "xpsearch.weightField";

    /// <summary>
    /// Name of the property the facet attribute configurator reads the selected index from. Every
    /// widget inherits it from <c>XpSearchMountWidgetProperties</c>, so a widget author never has to
    /// think about it - but a properties class that does not derive from that base must name its
    /// index property exactly this.
    /// </summary>
    public const string IndexPropertyName = "Index";
}
