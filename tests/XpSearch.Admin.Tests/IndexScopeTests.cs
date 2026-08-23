using Kentico.Xperience.Lucene.Core.Indexing;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.UIPages;

namespace XpSearch.Admin.Tests;

/// <summary>Builds a stand-in for the Lucene integration's configuration storage.</summary>
internal static class Storage
{
    /// <summary>A storage service that knows exactly one index.</summary>
    /// <param name="indexIdentifier">The identifier the index is registered under.</param>
    /// <param name="indexName">The index code name.</param>
    /// <param name="languages">The content languages the index is configured for.</param>
    /// <returns>The substitute.</returns>
    public static ILuceneConfigurationStorageService Holding(int indexIdentifier, string indexName, params string[] languages)
    {
        var storage = Substitute.For<ILuceneConfigurationStorageService>();

        storage.GetIndexDataOrNullAsync(Arg.Any<int>()).Returns((LuceneIndexModel?)null);
        storage.GetIndexDataOrNullAsync(indexIdentifier)
            .Returns(new LuceneIndexModel { Id = indexIdentifier, IndexName = indexName, LanguageNames = [.. languages] });

        return storage;
    }
}

/// <summary>
/// The two decisions every index-scoped admin page rests on: which index the URL means, and whether a
/// row reached through that URL belongs to it (ADR-0017).
/// </summary>
[TestFixture]
internal sealed class IndexScopeTests
{
    [Test]
    public void Resolve_TurnsTheUrlsIdentifierIntoTheIndexCodeName() =>
        Assert.That(IndexScope.Resolve(Storage.Holding(7, "articles"), 7), Is.EqualTo("articles"));

    /// <summary>An identifier no longer registered must not resolve to some other index's name.</summary>
    [Test]
    public void Resolve_IsEmptyForAnIndexThatIsNotRegistered() =>
        Assert.That(IndexScope.Resolve(Storage.Holding(7, "articles"), 999), Is.Empty);

    [Test]
    public void Route_KeysTheIndexOnThePageThatOwnsTheParameterizedSlug()
    {
        var route = IndexScope.Route(7);

        route.TryGetValue(typeof(IndexTuningSection), out object? value);

        Assert.That(value, Is.EqualTo(7));
    }

    /// <summary>
    /// A rule of index A opened through index B's URL must be refused, or a save would silently move
    /// it to B.
    /// </summary>
    [TestCase("articles", "articles", true)]
    [TestCase("ARTICLES", "articles", true)]
    [TestCase("products", "articles", false)]
    [TestCase(null, "articles", false)]
    [TestCase("articles", "", false)]
    [TestCase("articles", null, false)]
    public void Matches_AcceptsOnlyARowOfTheIndexInTheUrl(string? stored, string? route, bool expected) =>
        Assert.That(IndexScope.Matches(stored, route), Is.EqualTo(expected));
}
