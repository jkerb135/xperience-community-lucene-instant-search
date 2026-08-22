using System.Reflection;

using CMS;
using CMS.DataEngine;

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

    /// <summary>
    /// An <c>ObjectTypeInfo</c> alone is not a registration: without the assembly attribute the
    /// system never adds <c>IInfoProvider&lt;T&gt;</c> to the container, and the host fails DI
    /// validation on startup.
    /// </summary>
    [TestCase(typeof(XpSearchApiKeyInfo), XpSearchApiKeyInfo.OBJECT_TYPE)]
    [TestCase(typeof(XpSearchExternalDocumentInfo), XpSearchExternalDocumentInfo.OBJECT_TYPE)]
    [TestCase(typeof(XpSearchIngestionLogInfo), XpSearchIngestionLogInfo.OBJECT_TYPE)]
    public void Ingestion_object_types_are_registered(Type infoType, string objectType)
    {
        var registrations = typeof(XpSearchIngestionModule).Assembly
            .GetCustomAttributes<RegisterObjectTypeAttribute>()
            .Where(attribute => attribute.MarkedType == infoType)
            .ToList();

        Assert.That(registrations, Has.Count.EqualTo(1), $"{infoType.Name} must be registered exactly once.");
        Assert.That(registrations[0].ObjectType, Is.EqualTo(objectType));
    }
}
