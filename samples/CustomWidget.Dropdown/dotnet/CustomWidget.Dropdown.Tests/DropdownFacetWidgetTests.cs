using System.Collections.Generic;

using MyCompany.Search.Widgets;

using NUnit.Framework;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace MyCompany.Search.Widgets.Tests;

[TestFixture]
public sealed class DropdownFacetWidgetTests
{
    private static DropdownFacetWidgetViewComponent CreateComponent(XpSearchEditorMode mode = XpSearchEditorMode.Live) =>
        new(new XpSearchMountRenderer(), new StubEditorContext(mode), new StubIndexCatalog());

    [Test]
    public void BuildModel_ConfiguredWidget_EmitsMountForTheJavaScriptWidget()
    {
        var model = CreateComponent().BuildModel(new DropdownFacetWidgetProperties
        {
            Index = "site-content",
            Attribute = "brand",
            Label = "Brand",
            AllLabel = "Any brand",
        });

        var mount = Render(model);

        Assert.Multiple(() =>
        {
            Assert.That(mount, Does.Contain("class=\"xps-mount\""));
            Assert.That(mount, Does.Contain("data-xps-widget=\"myCompany.dropdownFacet\""));
            Assert.That(mount, Does.Contain("data-xps-instance=\"default\""));
            Assert.That(Decode(mount), Does.Contain("\"attribute\":\"brand\""));
            Assert.That(Decode(mount), Does.Contain("\"label\":\"Brand\""));
            Assert.That(Decode(mount), Does.Contain("\"allLabel\":\"Any brand\""));
            Assert.That(Decode(mount), Does.Contain("\"index\":\"site-content\""));
            Assert.That(model.EditorMessage, Is.Null);
        });
    }

    [Test]
    public void BuildModel_NoAttribute_RendersNothingOnTheLiveSite()
    {
        var model = CreateComponent().BuildModel(new DropdownFacetWidgetProperties { Index = "site-content" });

        Assert.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Is.Null);
        });
    }

    [Test]
    public void BuildModel_NoAttribute_InstructsTheEditor()
    {
        var model = CreateComponent(XpSearchEditorMode.Edit)
            .BuildModel(new DropdownFacetWidgetProperties { Index = "site-content" });

        Assert.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Does.Contain("Select the attribute to filter on."));
        });
    }

    private static string Render(XpSearchMountViewModel model)
    {
        Assert.That(model.Mount, Is.Not.Null);

        using var writer = new System.IO.StringWriter();
        model.Mount!.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    /// <summary>The mount's JSON is HTML-attribute-encoded; decode before asserting on it.</summary>
    private static string Decode(string markup) => System.Net.WebUtility.HtmlDecode(markup);

    private sealed class StubEditorContext(XpSearchEditorMode mode) : IXpSearchEditorContext
    {
        public XpSearchEditorMode GetMode() => mode;
    }

    private sealed class StubIndexCatalog : IXpSearchIndexCatalog
    {
        public IReadOnlyList<string> GetIndexNames() => ["site-content"];
    }
}
