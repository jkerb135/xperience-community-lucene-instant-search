using Microsoft.Extensions.Options;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// A pushed document must be findable through the query pipeline in the same process, without a
/// restart: <c>DefaultLuceneClient</c> writes in place and does not invalidate the integration's
/// cached searcher, so the write path has to.
/// </summary>
[TestFixture]
internal sealed class FreshnessTests
{
    private sealed class FixedSchemaProvider(IndexSchema schema) : IIndexSchemaProvider
    {
        public Task<IndexSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken) =>
            Task.FromResult(schema);
    }

    private static ISearchPipeline PipelineOver(TestHarness harness)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new XpSearchOptions());

        return new SearchPipeline(
            harness.Index,
            new FixedSchemaProvider(harness.Schema.Fields),
            [
                new NormalizeRequestStage(options),
                new BuildQueryStage(),
                new FacetFilterStage(),
                new NumericFilterStage(),
                new ExecuteSearchStage(harness.Index),
                new CollectFacetsStage(new TaxonomyFacetProvider(harness.Index), options),
                new HighlightStage(new LuceneHighlighter()),
                new ProjectResponseStage()
            ]);
    }

    [Test]
    public async Task PushedDocumentIsSearchableWithoutARestart()
    {
        using var harness = new TestHarness();
        var pipeline = PipelineOver(harness);

        var before = await pipeline.ExecuteAsync(
            new SearchRequest { Index = TestHarness.IndexName, Query = "espresso" },
            CancellationToken.None);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")])],
            waitForIndex: true);

        var after = await pipeline.ExecuteAsync(
            new SearchRequest { Index = TestHarness.IndexName, Query = "espresso" },
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(before.Total, Is.Zero);
            Assert.That(after.Total, Is.EqualTo(1));
            Assert.That(after.Results[0].Id, Is.EqualTo("pim-1"));
        });
    }

    [Test]
    public async Task StatusReflectsAPushWithoutARestart()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")])],
            waitForIndex: true);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Documents.Total, Is.EqualTo(1));
            Assert.That(status.Documents.BySource["pim"], Is.EqualTo(1));
        });
    }
}
