using System.Reflection;

using CMS;

using NUnit.Framework;

using XpSearch.Admin.Forms;
using XpSearch.Widgets.Components.Widgets.XpSearch;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Xperience only scans an assembly's registration attributes when the assembly is marked
/// discoverable, so a shipped assembly that registers anything by attribute must carry
/// <see cref="AssemblyDiscoverableAttribute"/>
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// Without it the widgets are simply absent from the Page Builder, with nothing logged.
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    // One type per shipped assembly that registers something by attribute: RegisterWidget in
    // XpSearch.Widgets, RegisterFormComponentConfigurator in XpSearch.Admin.
    private static readonly Type[] Assemblies = [typeof(SearchBoxWidgetProperties), typeof(FacetAttributeConfigurator)];

    [Test]
    public void Shipped_assemblies_are_discoverable([ValueSource(nameof(Assemblies))] Type marker)
    {
        var assembly = marker.Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            $"{assembly.GetName().Name} must carry CMS.AssemblyDiscoverableAttribute or Xperience ignores its registration attributes.");
    }
}
