using System.Reflection;

using CMS;

using NUnit.Framework;

using XpSearch.Admin.Forms;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The admin assembly registers its form component configurator, its tuning object types and the
/// Search tuning application's UI pages by attribute, and Xperience only scans an assembly marked
/// discoverable
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    [Test]
    public void Admin_assembly_is_discoverable()
    {
        var assembly = typeof(FacetAttributeConfigurator).Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            "XpSearch.Admin must carry CMS.AssemblyDiscoverableAttribute or Xperience ignores its registration attributes.");
    }
}
