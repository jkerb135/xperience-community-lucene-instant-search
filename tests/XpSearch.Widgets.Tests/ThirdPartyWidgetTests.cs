using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

using NUnit.Framework;

using XpSearch.Core;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

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

internal sealed record DropdownFacetOptions : XpSearchMountOptions
{
    public string? Attribute { get; init; }

    public string Label { get; init; } = "Filter";

    public string AllLabel { get; init; } = "All";
}

[HtmlTargetElement("my-dropdown-facet")]
internal sealed class DropdownFacetTagHelper : XpSearchMountTagHelper<DropdownFacetOptions>
{
    public DropdownFacetTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    [HtmlAttributeName("all-label")]
    public string? AllLabel { get; set; }

    protected override string WidgetType => "myCompany.dropdownFacet";

    public override string? Validate(DropdownFacetOptions options) =>
        base.Validate(options)
        ?? (string.IsNullOrWhiteSpace(options?.Attribute) ? "Select the attribute to filter on." : null);

    protected override DropdownFacetOptions Merge(DropdownFacetOptions options) => options with
    {
        Attribute = Attribute ?? options.Attribute,
        Label = Label ?? options.Label,
        AllLabel = AllLabel ?? options.AllLabel
    };
}

internal sealed class DropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties, DropdownFacetOptions>
{
    public DropdownFacetWidgetViewComponent(
        XpSearchMountTagHelper<DropdownFacetOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    public override DropdownFacetOptions ToOptions(DropdownFacetWidgetProperties properties) => new()
    {
        Index = properties.Index,
        InstanceId = properties.InstanceId,
        Attribute = properties.Attribute,
        Label = properties.Label,
        AllLabel = properties.AllLabel
    };
}

/// <summary>
/// The Page Builder preview override as the Custom widgets guide prints it - a third party's own
/// markup, built with TagBuilder so every editor-typed value is encoded.
/// </summary>
internal sealed class PreviewingDropdownFacetWidgetViewComponent
    : XpSearchMountWidgetViewComponent<DropdownFacetWidgetProperties, DropdownFacetOptions>
{
    public PreviewingDropdownFacetWidgetViewComponent(
        XpSearchMountTagHelper<DropdownFacetOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    public override DropdownFacetOptions ToOptions(DropdownFacetWidgetProperties properties) => new()
    {
        Index = properties.Index,
        InstanceId = properties.InstanceId,
        Attribute = properties.Attribute,
        Label = properties.Label,
        AllLabel = properties.AllLabel
    };

    protected override IHtmlContent BuildEditorPreview(DropdownFacetWidgetProperties properties)
    {
        var select = new TagBuilder("select");
        select.AddCssClass("xps-select__control");
        select.Attributes["disabled"] = "disabled";
        select.InnerHtml.AppendHtml(Element("option", null, properties.AllLabel));

        var box = new TagBuilder("div");
        box.AddCssClass("xps-select");
        box.InnerHtml.AppendHtml(Element("label", "xps-select__label", properties.Label));
        box.InnerHtml.AppendHtml(select);

        return new HtmlContentBuilder()
            .AppendHtml(box)
            .AppendHtml(Element("p", "xps-editor-preview__note", $"Attribute: {properties.Attribute}"));
    }

    private static TagBuilder Element(string tagName, string? cssClass, string text)
    {
        var tag = new TagBuilder(tagName);

        if (cssClass is not null)
        {
            tag.AddCssClass(cssClass);
        }

        // TagBuilder encodes: an editor's text can never become markup.
        tag.InnerHtml.Append(text);

        return tag;
    }
}

[TestFixture]
internal sealed class ThirdPartyWidgetTests
{
    private static DropdownFacetWidgetViewComponent Component(XpSearchEditorMode mode) =>
        new(
            new DropdownFacetTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")),
            new FakeEditorContext(mode));

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
    public void A_third_party_widget_can_own_what_the_Page_Builder_shows()
    {
        var component = new PreviewingDropdownFacetWidgetViewComponent(
            new DropdownFacetTagHelper(new XpSearchMountRenderer(), new FakeIndexCatalog("site-content")),
            new FakeEditorContext(XpSearchEditorMode.Edit));

        string markup = Rendered.Html(component
            .BuildModel(new DropdownFacetWidgetProperties { Attribute = "brand", Label = "<b>Brand</b>", AllLabel = "Any brand" })
            .Preview!);

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("xps-editor-preview--my-company-dropdown-facet"));
            Assert.That(markup, Does.Contain("<option>Any brand</option>"));
            Assert.That(markup, Does.Contain("Attribute: brand"));
            Assert.That(markup, Does.Not.Contain("<b>Brand</b>"));
            Assert.That(markup, Does.Contain("&lt;b&gt;Brand&lt;/b&gt;"));
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
