using NUnit.Framework;

using XpSearch.Core.Indexing;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// Spec §10.2: "A rebuild of Xperience content must never delete externally pushed documents, and
/// clear must be scopeable to one source." This is the test the whole design exists for.
/// </summary>
[TestFixture]
internal sealed class SourceIsolationTests
{
    private static IEnumerable<Lucene.Net.Documents.Document> XperienceContent() =>
    [
        TestLuceneIndex.XperienceDocument("11111111-1111-1111-1111-111111111111", "en", "Espresso Basics"),
        TestLuceneIndex.XperienceDocument("22222222-2222-2222-2222-222222222222", "en", "Our Cafés"),
    ];

    [Test]
    public async Task Rebuild_KeepsExternalDocumentsAndRebuildsXperienceContent()
    {
        using var harness = new TestHarness(xperienceContent: XperienceContent());

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("pim-sku-88213", attributes: ("title", "Ethiopian Yirgacheffe")),
                TestHarness.Document("pim-sku-99001", attributes: ("title", "Colombian Supremo")),
            ],
            waitForIndex: true);

        Assert.That(harness.Index.Count(), Is.EqualTo(4), "two Xperience documents plus two pushed ones");

        // The integration's rebuild, through the client a host resolves: reset with OpenMode.CREATE
        // and re-index Xperience content only.
        await harness.Client.Rebuild(TestHarness.IndexName, CancellationToken.None);

        Assert.That(harness.Index.Count(), Is.EqualTo(2), "the rebuild itself wipes everything but Xperience content");

        // The replay the decorator queued behind the rebuild.
        await harness.Queue.DrainAsync();

        var documents = harness.Index.Documents();

        Expect.Multiple(() =>
        {
            Assert.That(
                documents.Where(document => document.Source == LuceneFieldNames.XperienceSource).Select(document => document.Id),
                Is.EquivalentTo(["11111111-1111-1111-1111-111111111111:en", "22222222-2222-2222-2222-222222222222:en"]),
                "Xperience content was rebuilt");

            Assert.That(
                documents.Where(document => document.Source == "pim").Select(document => document.Id),
                Is.EquivalentTo(["pim-sku-88213", "pim-sku-99001"]),
                "externally pushed documents survived the rebuild");

            Assert.That(harness.Index.Matching("title", "yirgacheffe"), Is.EqualTo(1), "and they are searchable again");
        });
    }

    [Test]
    public async Task Clear_WithASource_DeletesOnlyThatSource()
    {
        using var harness = new TestHarness(xperienceContent: XperienceContent());

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("pim-1", attributes: ("title", "From the PIM")),
                TestHarness.Document("kb-1", "support", ("title", "From the knowledge base")),
            ],
            waitForIndex: true);

        var response = await harness.Indexer.DeleteBySourceAsync(TestHarness.IndexName, "pim", waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Deleted, Is.EqualTo(1));
            Assert.That(
                harness.Index.Documents().Select(document => document.Id),
                Is.EquivalentTo(["11111111-1111-1111-1111-111111111111:en", "22222222-2222-2222-2222-222222222222:en", "kb-1"]));
            Assert.That(harness.Store.Rows.Select(row => row.Id), Is.EquivalentTo(["kb-1"]));
        });
    }

    [Test]
    public async Task Clear_WithoutASource_DeletesEveryExternalDocumentAndNoXperienceContent()
    {
        using var harness = new TestHarness(xperienceContent: XperienceContent());

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("pim-1", attributes: ("title", "From the PIM")),
                TestHarness.Document("kb-1", "support", ("title", "From the knowledge base")),
            ],
            waitForIndex: true);

        var response = await harness.Indexer.DeleteBySourceAsync(TestHarness.IndexName, source: null, waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Deleted, Is.EqualTo(2));
            Assert.That(
                harness.Index.Documents().Select(document => document.Source),
                Is.EquivalentTo([LuceneFieldNames.XperienceSource, LuceneFieldNames.XperienceSource]));
        });
    }

    [Test]
    public void Clear_RefusesToTouchXperienceContent()
    {
        using var harness = new TestHarness(xperienceContent: XperienceContent());

        Expect.ThrowsAsync<XpSearch.Ingestion.Abstractions.IngestionValidationException>(
            () => harness.Indexer.DeleteBySourceAsync(TestHarness.IndexName, LuceneFieldNames.XperienceSource));
    }
}
