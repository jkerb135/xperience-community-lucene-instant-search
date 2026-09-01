using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;
using XpSearch.Core.Fuzzy;
using XpSearch.Core.Popularity;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The typo tolerance opt-in on the synonym listing (FZ-1): the command the header button names, and
/// the two states the callout and the button describe. Populating an Info object needs Kentico's IoC
/// container, so the flip itself is a host check - see the HW-11 checklist.
/// </summary>
[TestFixture]
internal sealed class TypoToleranceToggleTests
{
    private const int IndexIdentifier = 7;

    /// <summary>
    /// A page command has to be a plain method on the final page class: an inherited or re-annotated
    /// override is not found at runtime (XP-1b, agent-primer).
    /// </summary>
    [Test]
    public void TheToggle_IsDeclaredOnTheListingItselfBehindTheUpdatePermission()
    {
        var method = typeof(SynonymListing).GetMethod(
            nameof(SynonymListing.ToggleTypoTolerance),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.That(method, Is.Not.Null, "the header command names this method by string");

        var command = method!.GetCustomAttributes<PageCommandAttribute>(inherit: false).Single();

        Expect.Multiple(() =>
        {
            Assert.That(command.Permission, Is.EqualTo(SystemPermissions.UPDATE));
            Assert.That(method.GetParameters(), Is.Empty);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<ICommandResponse<RowActionResult>>)));
        });
    }

    [Test]
    public void TheCalloutAndTheButton_SayWhichStateTheIndexIsIn()
    {
        var on = TypoToleranceToggle.Callout(true);
        var off = TypoToleranceToggle.Callout(false);

        Expect.Multiple(() =>
        {
            Assert.That(on.Headline, Does.Contain("on"));
            Assert.That(off.Headline, Does.Contain("off"));
            Assert.That(on.Content, Is.Not.EqualTo(off.Content));
            Assert.That(on.ContentAsHtml, Is.False, "the texts are plain, so nothing can inject markup");
            Assert.That(TypoToleranceToggle.ActionLabel(true), Is.EqualTo("Turn typo tolerance off"), "the button says what clicking does");
            Assert.That(TypoToleranceToggle.ActionLabel(false), Is.EqualTo("Turn typo tolerance on"));
            Assert.That(TypoToleranceToggle.SuccessMessage(true), Is.Not.EqualTo(TypoToleranceToggle.SuccessMessage(false)));
        });
    }

    /// <summary>
    /// The command carries no index, so an identifier the URL no longer resolves must write nothing
    /// rather than create a settings row for the empty index name (ADR-0017).
    /// </summary>
    [Test]
    public void TheToggle_RefusesAndWritesNothingWhenTheUrlsIndexIsNotRegistered()
    {
        var fuzzy = Provider<XpSearchFuzzyIndexInfo>();

        var listing = new SynonymListing(
            Storage.Holding(IndexIdentifier, "articles"),
            Provider<XpSearchSynonymInfo>(),
            Provider<XpSearchSynonymSuggestionInfo>(),
            fuzzy,
            Substitute.For<IPageLinkGenerator>())
        {
            IndexIdentifier = 999
        };

        var response = listing.ToggleTypoTolerance().GetAwaiter().GetResult();

        Expect.Multiple(() =>
        {
            Assert.That(response.Messages.Single().Message, Is.EqualTo(IndexScope.CrossIndexDeleteRefusal));
            Assert.That(response.Result.Reload, Is.False, "nothing changed, so the listing has nothing to re-read");
            fuzzy.DidNotReceiveWithAnyArgs().Set(default(XpSearchFuzzyIndexInfo)!);
        });
    }

    private static IInfoProvider<TInfo> Provider<TInfo>()
        where TInfo : AbstractInfoBase<TInfo>, IInfoWithId, new() =>
        Substitute.For<IInfoProvider<TInfo>, IInfoByIdProvider<TInfo>>();
}
