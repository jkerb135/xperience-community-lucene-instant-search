using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Spec §7.5: an unconfigured widget instructs the editor, and renders nothing at all on a live page.
/// </summary>
[TestFixture]
internal sealed class UnconfiguredStateTests
{
    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog twoIndexes = new("site-content", "products");

    private SearchBoxWidgetViewComponent Component(XpSearchEditorMode mode) =>
        new(renderer, new FakeEditorContext(mode), twoIndexes);

    [TestCase(XpSearchEditorMode.Edit)]
    [TestCase(XpSearchEditorMode.ReadOnly)]
    [TestCase(XpSearchEditorMode.Preview)]
    public void An_unconfigured_widget_instructs_the_editor(XpSearchEditorMode mode)
    {
        var model = Component(mode).BuildModel(new SearchBoxWidgetProperties());

        Expect.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorTitle, Is.Not.Null.And.Not.Empty);
            Assert.That(model.EditorMessage, Does.Contain("Select a search index"));
        });
    }

    [Test]
    public void Each_editing_mode_gets_its_own_instruction()
    {
        string?[] messages =
        [
            Component(XpSearchEditorMode.Edit).BuildModel(new SearchBoxWidgetProperties()).EditorMessage,
            Component(XpSearchEditorMode.ReadOnly).BuildModel(new SearchBoxWidgetProperties()).EditorMessage,
            Component(XpSearchEditorMode.Preview).BuildModel(new SearchBoxWidgetProperties()).EditorMessage
        ];

        Assert.That(messages.Distinct(), Has.Exactly(3).Items);
    }

    [Test]
    public void An_unconfigured_widget_renders_nothing_on_a_live_page()
    {
        var model = Component(XpSearchEditorMode.Live).BuildModel(new SearchBoxWidgetProperties());

        Expect.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Is.Null);
            Assert.That(model.EditorTitle, Is.Null);
        });
    }

    [Test]
    public void A_facet_without_an_attribute_is_unconfigured_even_with_an_index()
    {
        var component = new FacetListWidgetViewComponent(renderer, new FakeEditorContext(XpSearchEditorMode.Edit), twoIndexes);

        var model = component.BuildModel(new FacetListWidgetProperties { Index = "site-content" });

        Expect.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Does.Contain("attribute"));
        });
    }

    [Test]
    public void A_sort_selector_whose_keys_the_API_would_reject_is_unconfigured()
    {
        var component = new SortSelectWidgetViewComponent(
            renderer,
            new FakeEditorContext(XpSearchEditorMode.Edit),
            twoIndexes,
            Microsoft.Extensions.Options.Options.Create(new Core.Options.XpSearchOptions()));

        var model = component.BuildModel(new SortSelectWidgetProperties
        {
            Index = "site-content",
            SortOptions = "nonsense;Nope"
        });

        Expect.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Does.Contain("key;Label"));
        });
    }
}
