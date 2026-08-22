using NUnit.Framework;

using XpSearch.Core.Indexing;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// <c>GET …/status</c> counts. Every figure counts live documents in the current index generation, so
/// the per-source counts add up to the total however often a document has been replaced.
/// </summary>
[TestFixture]
internal sealed class StatusCountTests
{
    [Test]
    public async Task ReplacedDocumentsAreCountedOnce()
    {
        using var harness = new TestHarness(
            xperienceContent: [TestLuceneIndex.XperienceDocument(Guid.NewGuid().ToString(), "en", "Coffee")]);

        // One batch, so the three documents share a segment; replacing one of them then leaves that
        // segment alive with a deleted document in it - the shape a reindexed Xperience index has.
        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")]),
                TestHarness.Document("pim-2", attributes: [("title", "Grinder")]),
                TestHarness.Document("pim-3", attributes: [("title", "Kettle")])
            ],
            waitForIndex: true);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine mk2")])],
            waitForIndex: true);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Documents.Total, Is.EqualTo(4));
            Assert.That(status.Documents.BySource["pim"], Is.EqualTo(3));
            Assert.That(status.Documents.BySource[LuceneFieldNames.XperienceSource], Is.EqualTo(1));
            Assert.That(status.Documents.BySource.Values.Sum(), Is.EqualTo(status.Documents.Total));
        });
    }
}
