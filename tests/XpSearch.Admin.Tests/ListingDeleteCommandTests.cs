using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Core.Popularity;
using XpSearch.Ingestion.Persistence;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Covers the delete row action of the five listings (AD-8). Before the fix each listing registered
/// <c>AddDeleteAction(nameof(Delete), ...)</c> against the base class's <c>Delete</c>, which carries no
/// <see cref="PageCommandAttribute"/> - so a click had no command to invoke and failed as not found.
/// Populating an Info object needs Kentico's IoC container, so the refusal is exercised through the
/// row the provider cannot produce; a genuine foreign-index row is a host check - see KNOWN-LIMITATIONS.
/// </summary>
[TestFixture]
internal sealed class ListingDeleteCommandTests
{
    private const int IndexIdentifier = 7;
    private const string IndexName = "articles";

    private static readonly Type[] Listings =
    [
        typeof(FieldWeightListing),
        typeof(SynonymListing),
        typeof(StopwordListing),
        typeof(RuleListing),
        typeof(ApiKeyListing),
    ];

    /// <summary>The root cause: the inherited member the listings named is not an invokable command.</summary>
    [Test]
    public void BaseDelete_IsNotAPageCommand() =>
        Assert.That(
            typeof(ListingPage).GetMethod(nameof(ListingPage.Delete))!.GetCustomAttributes<PageCommandAttribute>(inherit: false),
            Is.Empty,
            "if the platform ever attributes its own Delete, the overrides below become optional rather than load-bearing");

    [TestCaseSource(nameof(Listings))]
    public void Delete_IsDeclaredOnTheListingItselfBehindTheDeletePermission(Type listing)
    {
        var method = listing.GetMethod(
            nameof(ListingPage.Delete),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.That(method, Is.Not.Null, $"{listing.Name} must declare its own Delete, not inherit one");

        var command = method!.GetCustomAttributes<PageCommandAttribute>(inherit: false).Single();

        Expect.Multiple(() =>
        {
            Assert.That(command.Permission, Is.EqualTo(SystemPermissions.DELETE));
            Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(new[] { typeof(int) }));
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<ICommandResponse<RowActionResult>>)));
        });
    }

    /// <summary>
    /// A delete carries only a row id, so a hand-edited request can name a row outside the listing's
    /// index filter. Each tuning listing must refuse rather than delete, the way an edit of a foreign
    /// row is refused (CR-4b). A row the scoped provider cannot vouch for is the same refusal.
    /// </summary>
    [Test]
    public void Delete_RefusesAWeightItCannotProveBelongsToTheIndexInTheUrl() =>
        AssertRefused(new FieldWeightListing(Storage(), Provider<XpSearchFieldWeightInfo>(), Provider<XpSearchPopularityIndexInfo>()) { IndexIdentifier = IndexIdentifier });

    [Test]
    public void Delete_RefusesASynonymItCannotProveBelongsToTheIndexInTheUrl() =>
        AssertRefused(new SynonymListing(Storage(), Provider<XpSearchSynonymInfo>(), Provider<XpSearchSynonymSuggestionInfo>(), Substitute.For<IPageLinkGenerator>()) { IndexIdentifier = IndexIdentifier });

    [Test]
    public void Delete_RefusesAStopwordListItCannotProveBelongsToTheIndexInTheUrl() =>
        AssertRefused(new StopwordListing(Storage(), Provider<XpSearchStopwordListInfo>()) { IndexIdentifier = IndexIdentifier });

    [Test]
    public void Delete_RefusesARuleItCannotProveBelongsToTheIndexInTheUrl() =>
        AssertRefused(new RuleListing(Storage(), Substitute.For<IContactGroupCatalog>(), Provider<XpSearchRuleInfo>(), Provider<XpSearchPopularitySuggestionInfo>())
        {
            IndexIdentifier = IndexIdentifier
        });

    /// <summary>An identifier the URL no longer resolves scopes to nothing, so nothing may be deleted through it.</summary>
    [Test]
    public void Delete_RefusesEveryRowWhenTheUrlsIndexIsNotRegistered() =>
        AssertRefused(new FieldWeightListing(Storage(), Provider<XpSearchFieldWeightInfo>(), Provider<XpSearchPopularityIndexInfo>()) { IndexIdentifier = 999 });

    /// <summary>The refusal must not reach the platform's delete: the provider is asked to read, never to write.</summary>
    [Test]
    public void Delete_RefusingLeavesTheRowAlone()
    {
        var provider = Provider<XpSearchFieldWeightInfo>();

        _ = new FieldWeightListing(Storage(), provider, Provider<XpSearchPopularityIndexInfo>()) { IndexIdentifier = IndexIdentifier }.Delete(1);

        provider.DidNotReceiveWithAnyArgs().Delete(default(XpSearchFieldWeightInfo)!);
    }

    private static void AssertRefused(ListingPage listing)
    {
        var response = listing.Delete(1).GetAwaiter().GetResult();

        Expect.Multiple(() =>
        {
            Assert.That(response.Messages.Single().Message, Is.EqualTo(IndexScope.CrossIndexDeleteRefusal));
            Assert.That(response.Result.Reload, Is.False, "nothing changed, so the table has nothing to re-read");
        });
    }

    private static Kentico.Xperience.Lucene.Core.Indexing.ILuceneConfigurationStorageService Storage() =>
        Tests.Storage.Holding(IndexIdentifier, IndexName);

    /// <summary>
    /// A provider that holds no such row, so the listing cannot prove the row is in scope.
    /// <c>IInfoProvider&lt;T&gt;.Get(int)</c> is an extension that throws unless the provider also
    /// implements <see cref="IInfoByIdProvider{TInfo}"/>, which the container's real providers do.
    /// </summary>
    private static IInfoProvider<TInfo> Provider<TInfo>()
        where TInfo : AbstractInfoBase<TInfo>, IInfoWithId, new() =>
        Substitute.For<IInfoProvider<TInfo>, IInfoByIdProvider<TInfo>>();
}

