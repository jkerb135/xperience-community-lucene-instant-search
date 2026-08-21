using Kentico.Xperience.Admin.Base.FormAnnotations;

using NUnit.Framework;

using XpSearch.Core;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The worked example of the Custom widgets guide, built with nothing but the documented base class -
/// spec §12's extensibility check, as a test rather than a manual walkthrough.
/// </summary>
internal sealed class DropdownFacetWidgetProperties : XpSearchMountWidgetProperties
{
    [DropDownComponent(Label = "Attribute", Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = "Filter";

    [TextInputComponent(Label = "\"All\" option text", Order = OrderFirstWidgetProperty + 20)]
    public string AllLabel { get; set; } = "All";
}

internal sealed class DropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties>
{
    public DropdownFacetWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    protected override string WidgetType => "myCompany.dropdownFacet";

    protected override string? ConfigurationHint(DropdownFacetWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties.Attribute) ? "Select the attribute to filter on." : null;
}

[TestFixture]
internal sealed class ThirdPartyWidgetTests
{
    private static DropdownFacetWidgetViewComponent Component(XpSearchEditorMode mode) =>
        new(new XpSearchMountRenderer(), new FakeEditorContext(mode), new FakeIndexCatalog("site-content"));

    [Test]
    public void A_third_party_widget_gets_the_mount_contract_from_the_base_class_alone()
    {
        var model = Component(XpSearchEditorMode.Live)
            .BuildModel(new DropdownFacetWidgetProperties { Attribute = "brand", Label = "Brand" });

        Assert.That(model.Mount, Is.Not.Null);
        string markup = Rendered.Html(model.Mount!);
        var config = Rendered.Json(markup, "data-xps-config");

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("myCompany.dropdownFacet"));
            Assert.That(Rendered.Attribute(markup, "data-xps-instance"), Is.EqualTo("default"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("brand"));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("Brand"));
            Assert.That(config.GetProperty("allLabel").GetString(), Is.EqualTo("All"));
            Assert.That(
                Rendered.Json(markup, "data-xps-instance-config").GetProperty("index").GetString(),
                Is.EqualTo("site-content"));
        });
    }

    [Test]
    public void A_third_party_widget_gets_the_unconfigured_state_from_the_base_class_alone()
    {
        var editing = Component(XpSearchEditorMode.Edit).BuildModel(new DropdownFacetWidgetProperties());
        var live = Component(XpSearchEditorMode.Live).BuildModel(new DropdownFacetWidgetProperties());

        Expect.Multiple(() =>
        {
            Assert.That(editing.EditorMessage, Does.Contain("Select the attribute to filter on."));
            Assert.That(editing.Mount, Is.Null);
            Assert.That(live.EditorMessage, Is.Null);
            Assert.That(live.Mount, Is.Null);
        });
    }
}
