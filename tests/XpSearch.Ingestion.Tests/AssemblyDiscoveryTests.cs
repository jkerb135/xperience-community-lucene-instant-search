using System.Reflection;

using CMS;

using NUnit.Framework;

using XpSearch.Ingestion.Persistence;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// The ingestion assembly registers its module class and its object types by attribute, which
/// Xperience only scans in an assembly marked discoverable
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    [Test]
    public void Ingestion_assembly_is_discoverable()
    {
        var assembly = typeof(XpSearchIngestionModule).Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            "XpSearch.Ingestion must carry CMS.AssemblyDiscoverableAttribute or Xperience ignores its registration attributes.");
    }
}
