using System.Reflection;

using CMS;

using Kentico.PageBuilder.Web.Mvc;

using NUnit.Framework;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Xperience only scans an assembly's registration attributes when the assembly is marked
/// discoverable, so the assembly carrying the <c>RegisterWidget</c> attributes must carry
/// <see cref="AssemblyDiscoverableAttribute"/>
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// Without it the widgets are simply absent from the Page Builder, with nothing logged.
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    [Test]
    public void Widgets_assembly_is_discoverable()
    {
        var assembly = typeof(SearchBoxWidgetProperties).Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            "XpSearch.Widgets must carry CMS.AssemblyDiscoverableAttribute or none of its widgets appear in the Page Builder.");
    }

    [Test]
    public void The_range_filter_widget_is_registered()
    {
        var widget = typeof(RangeFilterWidgetProperties).Assembly
            .GetCustomAttributes<Kentico.PageBuilder.Web.Mvc.RegisterWidgetAttribute>()
            .SingleOrDefault(registration => registration.Identifier == XpSearchWidgetConstants.RangeFilterIdentifier);

        Assert.That(widget, Is.Not.Null);
        Expect.Multiple(() =>
        {
            Assert.That(widget!.Name, Is.EqualTo("Search - Range filter"));
            Assert.That(widget.IconClass, Is.Not.Empty);
        });
    /// <summary>
    /// Every identifier declared in <see cref="XpSearchWidgetConstants"/> is actually registered:
    /// a widget class that is added without its registration attribute (or with a mistyped
    /// identifier) is invisible in the Page Builder and nothing is logged.
    /// </summary>
    [Test]
    public void Every_declared_widget_identifier_is_registered()
    {
        var registered = typeof(SearchBoxWidgetProperties).Assembly
            .GetCustomAttributes<RegisterWidgetAttribute>()
            .Select(attribute => attribute.Identifier)
            .ToList();

        Assert.That(registered, Is.EquivalentTo(new[]
        {
            XpSearchWidgetConstants.SearchBoxIdentifier,
            XpSearchWidgetConstants.ResultsIdentifier,
            XpSearchWidgetConstants.FacetListIdentifier,
            XpSearchWidgetConstants.CategoryTreeIdentifier,
            XpSearchWidgetConstants.PaginationIdentifier,
            XpSearchWidgetConstants.ResultStatsIdentifier,
            XpSearchWidgetConstants.SortSelectIdentifier,
            XpSearchWidgetConstants.SuggestionsIdentifier,
            XpSearchWidgetConstants.RangeFilterIdentifier
        }));
    }
}
