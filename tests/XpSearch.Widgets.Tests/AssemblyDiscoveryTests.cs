using System.Reflection;

using CMS;

using NUnit.Framework;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Xperience only scans an assembly's registration attributes when the assembly is marked
/// discoverable, so the assembly carrying the seven <c>RegisterWidget</c> attributes must carry
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
    }
}
