using CMS.Activities;
using CMS.ContactManagement;
using CMS.DataEngine;
using CMS.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Experiments;
using XpSearch.Core.Personalization;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// What the two Page Builder personalization condition types decide (PS-1): the search-history
/// match, its consent gate, and the named sticky bucket.
/// </summary>
[TestFixture]
internal sealed class PersonalizationConditionTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Unspecified);

    [Test]
    public void AVisitorWhoSearchedForTheTermInsideTheWindowMatches() =>
        Expect.Multiple(() =>
        {
            Assert.That(
                SearchedFor.Matches([new RecentSearch("Espresso Machines", Now.AddDays(-3))], "espresso", 30, Now),
                Is.True,
                "the match is 'contains', ignoring case");
            Assert.That(
                SearchedFor.Matches([new RecentSearch("espresso", Now.AddDays(-31))], "espresso", 30, Now),
                Is.False,
                "the search fell out of the window");
            Assert.That(
                SearchedFor.Matches([new RecentSearch("grinders", Now.AddHours(-1))], "espresso", 30, Now),
                Is.False,
                "another search is not this term");
            Assert.That(
                SearchedFor.Matches([], "espresso", 30, Now),
                Is.False,
                "no contact, no consent or no activity - the original variant renders");
        });

    /// <summary>An editor who leaves the term empty would otherwise personalize for everyone.</summary>
    [Test]
    public void AnEmptyTermMatchesNobody() =>
        Expect.Multiple(() =>
        {
            Assert.That(SearchedFor.Matches([new RecentSearch("espresso", Now)], null, 30, Now), Is.False);
            Assert.That(SearchedFor.Matches([new RecentSearch("espresso", Now)], "   ", 30, Now), Is.False);
            Assert.That(SearchedFor.Matches([new RecentSearch("espresso", Now)], " espresso ", 30, Now), Is.True, "the term is trimmed");
        });

    [Test]
    public void AWindowBelowOneDayIsOneDay() =>
        Expect.Multiple(() =>
        {
            Assert.That(SearchedFor.Matches([new RecentSearch("espresso", Now.AddHours(-2))], "espresso", 0, Now), Is.True);
            Assert.That(SearchedFor.Matches([new RecentSearch("espresso", Now.AddDays(-2))], "espresso", -5, Now), Is.False);
        });

    [Test]
    public void SearchesAreNotReadFromAVisitorWhoHasNotConsentedToTracking()
    {
        var contacts = Substitute.For<ICurrentContactProvider>();
        var activities = Substitute.For<IInfoProvider<ActivityInfo>>();

        var searches = Provider(contacts, activities, Kentico.Web.Mvc.CookieLevel.System.Level).GetRecentSearches();

        Expect.Multiple(() =>
        {
            Assert.That(searches, Is.Empty);
            contacts.DidNotReceiveWithAnyArgs().GetExistingContact();
            activities.DidNotReceiveWithAnyArgs().Get();
        });
    }

    [Test]
    public void AVisitorWithNoContactYetHasNoSearchesAndIsNotQueriedFor()
    {
        var contacts = Substitute.For<ICurrentContactProvider>();
        var activities = Substitute.For<IInfoProvider<ActivityInfo>>();

        var searches = Provider(contacts, activities).GetRecentSearches();

        Expect.Multiple(() =>
        {
            Assert.That(searches, Is.Empty);
            activities.DidNotReceiveWithAnyArgs().Get();
        });
    }

    /// <summary>A page can carry many personalized widgets; they must not each read the activities.</summary>
    [Test]
    public void TheSearchesAreReadOncePerRequest()
    {
        var contacts = Substitute.For<ICurrentContactProvider>();
        var provider = Provider(contacts, Substitute.For<IInfoProvider<ActivityInfo>>());

        provider.GetRecentSearches();
        provider.GetRecentSearches();

        contacts.Received(1).GetExistingContact();
    }

    [Test]
    public void ANamedSplitBucketsAVisitorSameWayForever()
    {
        bool first = SearchBucket.IsInBucket("visitor-1", "B", "hero-test", 50);

        Expect.Multiple(() =>
        {
            Assert.That(
                Enumerable.Range(0, 20).All(_ => SearchBucket.IsInBucket("visitor-1", "B", "hero-test", 50) == first),
                Is.True,
                "same visitor, same split name, same answer");
            Assert.That(
                SearchBucket.IsInBucket("visitor-1", "A", "hero-test", 50),
                Is.EqualTo(!first),
                "a visitor is in exactly one of the two buckets");
            Assert.That(
                SearchBucket.IsInBucket("visitor-1", "b", " hero-test ", 50),
                Is.EqualTo(first),
                "the bucket letter and the split name are trimmed and case-insensitive");
        });
    }

    [Test]
    public void TwoSplitNamesBucketVisitorsIndependently()
    {
        int differing = Enumerable.Range(0, 500)
            .Count(index => SearchBucket.IsInBucket($"visitor-{index}", "B", "hero-test", 50)
                != SearchBucket.IsInBucket($"visitor-{index}", "B", "footer-test", 50));

        Assert.That(differing, Is.InRange(150, 350), "the two splits are unrelated, so about half the visitors differ");
    }

    [Test]
    public void TheSplitPercentageIsHonouredAtItsEdges() =>
        Expect.Multiple(() =>
        {
            Assert.That(
                Enumerable.Range(0, 500).Count(index => SearchBucket.IsInBucket($"v{index}", "B", "edge", 1)),
                Is.LessThan(30),
                "a 1% split sends almost nobody to B");
            Assert.That(
                Enumerable.Range(0, 500).Count(index => SearchBucket.IsInBucket($"v{index}", "B", "edge", 99)),
                Is.GreaterThan(470),
                "a 99% split sends nearly everyone to B");
            Assert.That(
                Enumerable.Range(0, 500).Count(index => SearchBucket.IsInBucket($"v{index}", "B", "edge", 0)),
                Is.LessThan(30),
                "an out-of-range percentage is clamped into 1-99, never 'everyone' or 'nobody'");
        });

    /// <summary>Without a cookie the visitor has no stable bucket, so the original variant renders.</summary>
    [Test]
    public void AVisitorWithNoBucketCookieIsInNeitherBucket() =>
        Expect.Multiple(() =>
        {
            Assert.That(SearchBucket.IsInBucket(null, "A", "hero-test", 50), Is.False);
            Assert.That(SearchBucket.IsInBucket(string.Empty, "B", "hero-test", 50), Is.False);
        });

    /// <summary>
    /// The string seed added for PS-1 must leave XP-1's experiment bucketing bit for bit identical.
    /// </summary>
    [Test]
    public void TheStringSeedIsTheSameHashAsTheExperimentGuid()
    {
        var experiment = new Guid("2f1c2d1e-0000-4000-8000-000000000001");

        Expect.Multiple(() =>
        {
            Assert.That(
                ExperimentBucketing.Bucket("visitor-1", experiment.ToString("N")),
                Is.EqualTo(ExperimentBucketing.Bucket("visitor-1", experiment)));
            Assert.That(
                ExperimentBucketing.Variant("visitor-1", experiment.ToString("N"), 30),
                Is.EqualTo(ExperimentBucketing.Variant("visitor-1", experiment, 30)));
        });
    }

    private static RecentSearchProvider Provider(
        ICurrentContactProvider contacts,
        IInfoProvider<ActivityInfo> activities,
        int? cookieLevel = null)
    {
        var levels = Substitute.For<ICurrentCookieLevelProvider>();
        levels.GetCurrentCookieLevel().Returns(cookieLevel ?? Kentico.Web.Mvc.CookieLevel.All.Level);

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext());

        return new RecentSearchProvider(
            contacts,
            levels,
            activities,
            accessor,
            NullLogger<RecentSearchProvider>.Instance);
    }
}
