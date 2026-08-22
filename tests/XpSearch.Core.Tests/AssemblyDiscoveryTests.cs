using System.Reflection;

using CMS;

using NUnit.Framework;

using XpSearch.Core.Analytics;

namespace XpSearch.Core.Tests;

/// <summary>
/// The core assembly registers its analytics module class, its query log object type and the
/// retention scheduled task by attribute, and Xperience only scans an assembly marked discoverable
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    [Test]
    public void Core_assembly_is_discoverable()
    {
        var assembly = typeof(XpSearchAnalyticsModule).Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            "XpSearch.Core must carry CMS.AssemblyDiscoverableAttribute or Xperience ignores its registration attributes.");
    }
}
