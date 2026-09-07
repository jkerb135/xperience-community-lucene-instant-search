using System.Text.RegularExpressions;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// SK-1 §2.2/§2.5: every mount carries a first paint, and the skeleton one is the block
/// <c>themes/fixtures/skeleton.html</c> draws for that widget - the same acceptance test
/// <c>card-parity.test.ts</c> applies to the result card. The fixture is copied next to the test
/// assembly by the project file.
/// </summary>
[TestFixture]
internal sealed class SkeletonParityTests
{
    private const string Index = "site-content";

    private static readonly XpSearchMountRenderer Renderer = new();
    private static readonly FakeIndexCatalog Catalog = new(Index);

    /// <summary>Each widget's tag element, with the options it needs to be renderable at all.</summary>
    private static IEnumerable<TestCaseData> Widgets()
    {
        yield return Case("xps-facet-list", new FacetListTagHelper(Renderer, Catalog) { Attribute = "contentType" });
        yield return Case("xps-category-tree", new CategoryTreeTagHelper(Renderer, Catalog) { Attribute = "categories" });
        yield return Case(
            "xps-range-filter",
            new RangeFilterTagHelper(Renderer, Catalog) { Attribute = "price", Minimum = 0m, Maximum = 500m });
        yield return Case(
            "xps-toggle-filter",
            new ToggleFilterTagHelper(Renderer, Catalog) { Attribute = "language", Value = "en", Label = "English only" });
        yield return Case("xps-result-stats", new ResultStatsTagHelper(Renderer, Catalog));
        yield return Case(
            "xps-sort-select",
            new SortSelectTagHelper(Renderer, Catalog, TagHelperTests.SearchOptions()) { SortOptions = "relevance;Most relevant" });
        yield return Case(
            "xps-filter-sort",
            new FilterSortTagHelper(Renderer, Catalog, TagHelperTests.SearchOptions()) { Facets = "contentType;Content type" });
        yield return Case("xps-pagination", new PaginationTagHelper(Renderer, Catalog));
        yield return Case("xps-active-filters", new ActiveFiltersTagHelper(Renderer, Catalog));
        yield return Case("xps-clear-filters", new ClearFiltersTagHelper(Renderer, Catalog));
        yield return Case("xps-load-more", new LoadMoreTagHelper(Renderer, Catalog));
        yield return Case("xps-suggestions", new SuggestionsTagHelper(Renderer, Catalog));
    }

    [TestCaseSource(nameof(Widgets))]
    public void The_default_first_paint_is_the_fixture_block(string tagName, string markup)
    {
        string block = Inside(markup);
        string expected = Fixture(RootClass(block));

        Expect.Multiple(() =>
        {
            Assert.That(block, Is.EqualTo(expected), tagName);
            // Decoration, and nothing else: no text a crawler could index, nothing focusable.
            Assert.That(block, Does.StartWith("<"), tagName);
            Assert.That(Regex.Replace(block, "<[^>]*>", string.Empty), Is.Empty, $"{tagName}: the skeleton carries text");
            Assert.That(block, Does.Not.Contain("<a ").And.Not.Contain("<button").And.Not.Contain("<input"), tagName);
        });
    }

    /// <summary>A widget nobody drew - a third party's - still gets the handover, as an empty root.</summary>
    [Test]
    public void A_third_party_widget_gets_the_empty_root()
    {
        string markup = TagHelperTests.Tag(
            new XpSearchWidgetTagHelper(Renderer, Catalog) { Type = "myCompany.dropdownFacet" },
            "xps-widget");

        Assert.That(
            Inside(markup),
            Is.EqualTo(
                "<div data-xps-server-rendered aria-hidden=\"true\""
                + " class=\"xps xps-my-company-dropdown-facet xps-my-company-dropdown-facet--skeleton\"></div>"));
    }

    private static TestCaseData Case<TOptions>(string tagName, XpSearchMountTagHelper<TOptions> helper)
        where TOptions : XpSearchMountOptions, new() =>
        new TestCaseData(tagName, TagHelperTests.Tag(helper, tagName)).SetArgDisplayNames(tagName);

    /// <summary>What the mount element contains.</summary>
    private static string Inside(string mount) =>
        mount[(mount.IndexOf('>', StringComparison.Ordinal) + 1)..mount.LastIndexOf("</div>", StringComparison.Ordinal)];

    private static string RootClass(string block) => Rendered.Attribute(block, "class");

    /// <summary>
    /// The fixture's block for one widget root, with the pretty-printing between tags removed: the
    /// file is authored for a human, and a skeleton has no text for that whitespace to belong to.
    /// </summary>
    private static string Fixture(string rootClass)
    {
        string fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "skeleton.html"));

        foreach (string mount in fixture.Split("<div class=\"xps-mount\">").Skip(1))
        {
            // Everything before the mount's own closing tag is the one block inside it.
            string block = Collapse(mount[..mount.LastIndexOf("</div>", StringComparison.Ordinal)]);

            if (block.Contains($"class=\"{rootClass}\"", StringComparison.Ordinal))
            {
                return block;
            }
        }

        Assert.Fail($"themes/fixtures/skeleton.html has no block for {rootClass}");

        return string.Empty;
    }

    private static string Collapse(string html) => Regex.Replace(html.Trim(), ">\\s+<", "><");
}
