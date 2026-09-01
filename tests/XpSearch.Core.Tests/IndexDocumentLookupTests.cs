using NUnit.Framework;

using XpSearch.Core.Search;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Resolving stored result ids back into documents, which is how the rule builder shows a pinned
/// item's title instead of its id (CR-5).
/// </summary>
[TestFixture]
internal sealed class IndexDocumentLookupTests
{
    private TestSearchIndex index = null!;
    private IndexDocumentLookup lookup = null!;

    [SetUp]
    public void SetUp()
    {
        index = new TestSearchIndex(TestCorpus.IndexName, TestCorpus.Documents, withTaxonomy: true);
        lookup = new IndexDocumentLookup(index, new StaticSchemaProvider(TestCorpus.Schema));
    }

    [TearDown]
    public void TearDown() => index.Dispose();

    [Test]
    public async Task Resolve_ReturnsTheTitleAndUrlOfEachId()
    {
        var resolved = await lookup.ResolveAsync(TestCorpus.IndexName, ["doc-4:en", "doc-1:en"], CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(resolved.Select(document => document.Id), Is.EqualTo(new[] { "doc-4:en", "doc-1:en" }).AsCollection, "asked-for order is kept");
            Assert.That(resolved[0].Title, Is.EqualTo("Coffee Grinder"));
            Assert.That(resolved[0].Url, Is.EqualTo("/products/coffee-grinder"));
        });
    }

    /// <summary>
    /// An id the index no longer holds comes back missing, never as a blank document - the builder
    /// tells the two apart to warn about a rule pointing at deleted content.
    /// </summary>
    [Test]
    public async Task Resolve_LeavesOutAnIdTheIndexNoLongerHolds()
    {
        var resolved = await lookup.ResolveAsync(TestCorpus.IndexName, ["doc-1:en", "doc-gone:en"], CancellationToken.None);

        Assert.That(resolved.Select(document => document.Id), Is.EqualTo(new[] { "doc-1:en" }).AsCollection);
    }

    [Test]
    public async Task Resolve_IgnoresBlanksAndDuplicatesAndDoesNotOpenTheIndexForNothing()
    {
        Assert.That(await lookup.ResolveAsync(TestCorpus.IndexName, ["", "  "], CancellationToken.None), Is.Empty);

        var resolved = await lookup.ResolveAsync(TestCorpus.IndexName, ["doc-1:en", "doc-1:en"], CancellationToken.None);

        Assert.That(resolved, Has.Count.EqualTo(1));
    }
}
