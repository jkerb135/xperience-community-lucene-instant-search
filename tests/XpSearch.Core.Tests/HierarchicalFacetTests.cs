using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Options;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Hierarchical taxonomy facets (ADR-0018): the dimension stays flat, every ancestor of a tag is
/// written onto the document as a value of its own, and <c>FacetValue.path</c> carries the ancestry
/// back to the client. The corpus's <c>Topic</c> dimension is three levels deep
/// (<c>drinks &gt; espresso-drinks &gt; latte</c>); <c>Category</c> and <c>Tags</c> stay flat.
/// </summary>
[TestFixture]
internal sealed class HierarchicalFacetTests
{
    private readonly TestHarness harness = new();

    [OneTimeTearDown]
    public void Dispose() => harness.Dispose();

    [Test]
    public async Task ADocumentTaggedWithAGrandchild_CountsTowardsItsParentAndItsGrandparent()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.TopicField];

        var response = await harness.Search(request);
        var counts = response.Facets![TestCorpus.TopicField].ToDictionary(value => value.Value, value => value.Count);

        Expect.Multiple(() =>
        {
            // doc-2 and doc-6 are tagged "latte" only; doc-1 is tagged with its parent.
            Assert.That(counts["latte"], Is.EqualTo(2));
            Assert.That(counts["espresso-drinks"], Is.EqualTo(3), "the parent counts its own document plus both grandchildren");
            Assert.That(counts["drinks"], Is.EqualTo(3), "the grandparent counts them too");
            Assert.That(counts["grinders"], Is.EqualTo(1));
            Assert.That(counts["gear"], Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EveryValue_CarriesItsAncestorsAsPathAndARootCarriesNone()
    {
        var request = TestHarness.Request();
        request.Facets = [TestCorpus.TopicField, TestCorpus.CategoryField, TestCorpus.ContentTypeField];

        var response = await harness.Search(request);
        var topics = response.Facets![TestCorpus.TopicField].ToDictionary(value => value.Value);

        Expect.Multiple(() =>
        {
            Assert.That(topics["drinks"].Path, Is.Null, "a root-level value has no path");
            Assert.That(topics["espresso-drinks"].Path, Is.EqualTo(new[] { "drinks" }));
            Assert.That(topics["latte"].Path, Is.EqualTo(new[] { "drinks", "espresso-drinks" }), "root first, excluding the value itself");
            Assert.That(topics["latte"].Label, Is.EqualTo("Latte"), "the label is still the tag title");

            // A flat taxonomy and a non-taxonomy dimension both report no path at all.
            Assert.That(response.Facets[TestCorpus.CategoryField].All(value => value.Path is null), Is.True);
            Assert.That(response.Facets[TestCorpus.ContentTypeField].All(value => value.Path is null), Is.True);
        });
    }

    [Test]
    public async Task AFilterOnAParent_MatchesTheDocumentsTaggedWithItsDescendants()
    {
        var request = TestHarness.Request();
        request.Filters = new Filters
        {
            Facets = [new FacetFilter { Attribute = TestCorpus.TopicField, Values = ["drinks"] }]
        };

        var response = await harness.Search(request);

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-1:en", "doc-2:en", "doc-6:de" }),
            "no document carries the root tag directly; they are tagged with its child and grandchild");
    }

    /// <summary>
    /// The contract promises that every ancestor a <c>path</c> names is itself in the same facet's
    /// values. Writing ancestors first makes the top-N cut keep them for an index this library
    /// wrote, so the top-up is asserted directly rather than through a corpus that cannot provoke it.
    /// </summary>
    [Test]
    public void AnAncestorDroppedByTheTopNCut_IsPulledBackIn()
    {
        var emitted = new List<FacetValue>
        {
            new() { Value = "latte", Label = "Latte", Count = 2, Path = ["drinks", "espresso-drinks"] }
        };

        var all = new FacetValue[]
        {
            new() { Value = "drinks", Label = "Drinks", Count = 3 },
            new() { Value = "espresso-drinks", Label = "Espresso drinks", Count = 3, Path = ["drinks"] },
            new() { Value = "latte", Label = "Latte", Count = 2, Path = ["drinks", "espresso-drinks"] }
        };

        int reads = 0;
        TaxonomyFacetProvider.EnsureAncestors(emitted, () => { reads++; return all; });

        var complete = new List<FacetValue>(emitted);
        int completeReads = 0;
        TaxonomyFacetProvider.EnsureAncestors(complete, () => { completeReads++; return all; });

        Expect.Multiple(() =>
        {
            Assert.That(emitted.Select(value => value.Value), Is.EquivalentTo(new[] { "latte", "espresso-drinks", "drinks" }));
            Assert.That(emitted.Single(value => value.Value == "drinks").Count, Is.EqualTo(3), "the pulled-in ancestor keeps its own count");
            Assert.That(reads, Is.EqualTo(1), "the full count list is read once, and only when something is missing");
            Assert.That(completeReads, Is.Zero, "a complete list costs no second read");
        });
    }

    [Test]
    public async Task TheTopNCut_KeepsTheAncestorsOfEveryValueItEmits()
    {
        var options = new XpSearchOptions { MaxFacetValues = 1 };
        using var capped = new TestHarness(options);

        var request = TestHarness.Request();
        request.Facets = [TestCorpus.TopicField];

        var values = (await capped.Search(request)).Facets![TestCorpus.TopicField];
        var present = values.Select(value => value.Value).ToHashSet(StringComparer.Ordinal);

        Assert.That(
            values.SelectMany(value => value.Path ?? []),
            Is.All.Matches<string>(present.Contains),
            "every ancestor a path names is itself one of the emitted values");
    }
}
