using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Contract;

namespace XpSearch.Admin.Tests;

/// <summary>Covers the query tester page command (spec §8.4).</summary>
[TestFixture]
internal sealed class QueryTesterPageTests
{
    private IQueryTesterSearch search = null!;
    private QueryTesterPage page = null!;

    [SetUp]
    public void SetUp()
    {
        search = Substitute.For<IQueryTesterSearch>();
        search
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new QueryTesterSideResult(Empty(), [])));

        page = new QueryTesterPage(Substitute.For<ILuceneIndexManager>(), search);
    }

    [Test]
    public async Task Run_ExecutesBothSidesWithExplain()
    {
        await page.Run(new QueryTesterRequest { IndexName = "articles", Query = "espresso" }, CancellationToken.None);

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
            new QueryTesterRequest { IndexName = "articles", PageSize = 5000, Language = "  " },
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
        var response = await page.Run(new QueryTesterRequest { IndexName = "  " }, CancellationToken.None);

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
            .ExecuteAsync(Arg.Any<SearchRequest>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<QueryTesterSideResult>>(_ => throw new XpSearch.Core.Abstractions.SearchValidationException("query", "Too long."));

        var response = await page.Run(new QueryTesterRequest { IndexName = "articles" }, CancellationToken.None);

        Assert.That(response.Result.Error, Does.Contain("Too long."));
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
