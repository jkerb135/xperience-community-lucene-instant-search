using NUnit.Framework;

namespace XpSearch.Client.Tests;

/// <summary>
/// The whole point of a separate client package: a PIM sync job or a console importer can talk to the
/// ingestion API without pulling Xperience or Lucene in. A ProjectReference added by accident would
/// make this fail.
/// </summary>
[TestFixture]
internal sealed class KenticoFreeTests
{
    [Test]
    public void TheClientAssemblyReferencesNothingButTheBcl()
    {
        var referenced = typeof(XpSearchIngestionClient).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal) && name != "netstandard")
            .ToArray();

        Assert.That(referenced, Is.Empty, $"XpSearch.Client took on a dependency: {string.Join(", ", referenced)}");
    }
}
