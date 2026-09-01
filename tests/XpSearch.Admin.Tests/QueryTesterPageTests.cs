using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Contract;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>Covers the query tester page command (spec §8.4).</summary>
[TestFixture]
internal sealed class QueryTesterPageTests
{
    private const int IndexIdentifier = 7;

    private IQueryTesterSearch search = null!;
    private IContactGroupCatalog contactGroups = null!;
    private IExperimentCatalog experiments = null!;
    private IPageLinkGenerator links = null!;
    private QueryTesterPage page = null!;

    [SetUp]
    public void SetUp()
    {
        search = Substitute.For<IQueryTesterSearch>();
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<TuningVariant>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new QueryTesterSideResult(Empty(), [])));

        contactGroups = Substitute.For<IContactGroupCatalog>();
        contactGroups
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ContactGroupOption>>([new ContactGroupOption("grinder-shoppers", "Grinder shoppers")]));

        experiments = Substitute.For<IExperimentCatalog>();
        experiments
            .GetUnfinishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExperimentSummary?>(new ExperimentSummary(11, "articles", "Boost recent", 50, ExperimentState.Draft, ExperimentOutcome.None, null, null)));

        links = Substitute.For<IPageLinkGenerator>();
        links.GetPath<IndexStatusPage>(Arg.Any<PageParameterValues>()).Returns("/admin/lucene/indexes/edit/7/status");

        page = new QueryTesterPage(Storage.Holding(IndexIdentifier, "articles", "en", "es"), search, links, contactGroups, experiments)
        {
            IndexIdentifier = IndexIdentifier
        };
    }

    /// <summary>
    /// The variant a run answers from is resolved on the server from the index's own experiment: the
    /// client says "variant B", never which experiment (XP-1).
    /// </summary>
    [Test]
    public async Task Run_AnswersFromTheIndexsOwnExperimentWhenVariantBIsAskedFor()
    {
        await page.Run(new QueryTesterRequest { Query = "espresso", VariantB = true }, CancellationToken.None);

        var variants = search
            .ReceivedCalls()
            .Select(call => (TuningVariant)call.GetArguments()[3]!)
            .ToList();

        Expect.Multiple(() =>
        {
            Assert.That(variants, Has.Count.EqualTo(2), "both sides of the comparison run against the same variant");
            Assert.That(variants, Is.All.EqualTo(new TuningVariant(11)));
        });
    }

    [Test]
    public async Task Run_AnswersFromTheLiveTuningByDefault()
    {
        await page.Run(new QueryTesterRequest { Query = "espresso" }, CancellationToken.None);

        Assert.That(
            search.ReceivedCalls().Select(call => (TuningVariant)call.GetArguments()[3]!),
            Is.All.EqualTo(TuningVariant.Live));
    }

    /// <summary>An index with no unfinished experiment has no variant B to offer, so the select is absent.</summary>
    [Test]
    public async Task ConfigureTemplateProperties_OffersTheVariantOnlyWhileAnExperimentExists()
    {
        var withExperiment = await page.ConfigureTemplateProperties(new QueryTesterClientProperties());

        experiments.GetUnfinishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExperimentSummary?>(null));

        var without = await page.ConfigureTemplateProperties(new QueryTesterClientProperties());

        Expect.Multiple(() =>
        {
            Assert.That(withExperiment.ExperimentName, Is.EqualTo("Boost recent"));
            Assert.That(without.ExperimentName, Is.Empty);
        });
    }

    [Test]
    public async Task Run_ExecutesBothSidesWithExplain()
    {
        await page.Run(new QueryTesterRequest { Query = "espresso" }, CancellationToken.None);

        var requests = search
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .ToList();

        Expect.Multiple(() =>
        {
            Assert.That(requests, Has.Count.EqualTo(2), "one run per side");
            Assert.That(requests.Select(arguments => ((SearchRequest)arguments[0]!).Explain), Is.All.True);
            Assert.That(requests.Select(arguments => (bool)arguments[1]!), Is.EqualTo(new[] { true, false }));
            Assert.That(requests.Select(arguments => ((SearchRequest)arguments[0]!).Index), Is.All.EqualTo("articles"));
            Assert.That(requests.Select(arguments => ((SearchRequest)arguments[0]!).Query), Is.All.EqualTo("espresso"));
        });
    }

    [Test]
    public async Task Run_ClampsThePageSizeAndOmitsAnEmptyLanguage()
    {
        await page.Run(
            new QueryTesterRequest { PageSize = 5000, Language = "  " },
            CancellationToken.None);

        var request = (SearchRequest)search.ReceivedCalls().First().GetArguments()[0]!;

        Expect.Multiple(() =>
        {
            Assert.That(request.PageSize, Is.EqualTo(QueryTesterPage.MaxPageSize));
            Assert.That(request.Language, Is.Null);
        });
    }

    [Test]
    public async Task Run_ReportsAMissingIndexWithoutSearching()
    {
        var unregistered = new QueryTesterPage(Storage.Holding(IndexIdentifier, "articles"), search, links, contactGroups, experiments) { IndexIdentifier = 999 };

        var response = await unregistered.Run(new QueryTesterRequest(), CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(response.Result.Error, Is.Not.Empty);
            Assert.That(search.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task Run_TurnsAValidationFailureIntoAMessage()
    {
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<TuningVariant>(), Arg.Any<CancellationToken>())
            .Returns<Task<QueryTesterSideResult>>(_ => throw new XpSearch.Core.Abstractions.SearchValidationException("query", "Too long."));

        var response = await page.Run(new QueryTesterRequest(), CancellationToken.None);

        Assert.That(response.Result.Error, Does.Contain("Too long."));
    }

    /// <summary>
    /// The tester runs against the index in the URL, never against one the client asked for, and the
    /// client template is told the index is not a choice.
    /// </summary>
    [Test]
    public async Task ConfigureTemplateProperties_LocksTheIndexToTheOneInTheUrl()
    {
        var properties = await page.ConfigureTemplateProperties(new QueryTesterClientProperties());

        Expect.Multiple(() =>
        {
            Assert.That(properties.SelectedIndexName, Is.EqualTo("articles"));
            Assert.That(properties.Languages, Is.EqualTo(new[] { "en", "es" }), "the language selector only offers what the index holds");
        });
    }

    /// <summary>The "could not be run" callout offers the status page of the same index.</summary>
    [Test]
    public async Task OpenStatus_NavigatesToTheStatusPageOfTheIndexInTheUrl()
    {
        var response = await page.OpenStatus();

        var parameters = (PageParameterValues)links.ReceivedCalls().Single().GetArguments()[0]!;
        parameters.TryGetValue(typeof(IndexTuningSection), out object? index);

        Expect.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(IndexIdentifier));
            Assert.That(response, Is.Not.Null);
        });
    }

    [Test]
    public void Page_AndItsCommandAreBehindTheApplicationsReadPermission()
    {
        var pagePermission = typeof(QueryTesterPage)
            .GetCustomAttributes(typeof(UIEvaluatePermissionAttribute), inherit: false)
            .Cast<UIEvaluatePermissionAttribute>()
            .SingleOrDefault();

        var command = typeof(QueryTesterPage)
            .GetMethod(nameof(QueryTesterPage.Run))!
            .GetCustomAttributes(typeof(PageCommandAttribute), inherit: false)
            .Cast<PageCommandAttribute>()
            .Single();

        Expect.Multiple(() =>
        {
            Assert.That(pagePermission?.Permission, Is.EqualTo(SystemPermissions.VIEW));
            Assert.That(command.Permission, Is.EqualTo(SystemPermissions.VIEW));
        });
    }

    private static SearchResponse Empty() =>
        new() { Results = [], Total = 0, TookMs = 1, Page = 1, PageSize = 10, TotalPages = 0 };
}
