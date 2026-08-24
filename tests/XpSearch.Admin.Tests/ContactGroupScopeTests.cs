using CMS.ContactManagement;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base.FormAnnotations;

using Lucene.Net.Analysis.Standard;
using Lucene.Net.Facet;
using Lucene.Net.Util;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Scoping a rule to a contact group from the admin side (ADR-0021): the field a marketer fills in,
/// what the listing shows, and the query tester's simulation.
/// </summary>
[TestFixture]
internal sealed class ContactGroupScopeTests
{
    private const string Group = "grinder-shoppers";

    private static readonly TuningRule Scoped = RuleStorageMigration.FromFlat(
        1,
        "Promote grinders",
        enabled: true,
        LegacyCondition.Always,
        string.Empty,
        LegacyConsequence.Boost,
        "doc-1",
        0,
        2,
        string.Empty,
        string.Empty,
        null,
        null,
        100,
        Group);

    /// <summary>
    /// The builder's Context toggle stores the contact group's code name, because that is what the
    /// stored conditions JSON carries and what the pipeline compares (ADR-0021).
    /// </summary>
    [Test]
    public void TheBuilderRoundTripsTheContactGroupAsACodeName()
    {
        var edited = RuleConditionsDto.From(new RuleConditions(null, [], Group, string.Empty));

        Expect.Multiple(() =>
        {
            Assert.That(edited.ContactGroup, Is.EqualTo(Group));
            Assert.That(edited.ToModel().ContactGroup, Is.EqualTo(Group));
            Assert.That(new RuleConditionsDto().ToModel().ContactGroup, Is.Empty, "no group means everyone");
        });
    }

    [Test]
    public void TheListingShowsEveryoneForAnUnscopedRuleAndTheCodeNameForAGroupThatIsGone()
    {
        // The by-code-name lookup goes through IInfoByNameProvider, which the real provider also implements.
        var catalog = new ContactGroupCatalog(Substitute.For<IInfoProvider<ContactGroupInfo>, IInfoByNameProvider<ContactGroupInfo>>());

        Expect.Multiple(() =>
        {
            Assert.That(catalog.Label(null), Is.EqualTo("Everyone"));
            Assert.That(catalog.Label(string.Empty), Is.EqualTo("Everyone"));
            Assert.That(catalog.Label("  "), Is.EqualTo("Everyone"));
            Assert.That(catalog.Label("deleted-group"), Is.EqualTo("deleted-group"), "a deleted group still tells the marketer what the rule says");
        });
    }

    [Test]
    public async Task TheTesterSimulatesTheChosenGroupInsteadOfResolvingTheAdminsOwnContact()
    {
        var resolver = new FakeResolver();
        var recorder = new RecordingStage();

        await Build(resolver, recorder).ExecuteAsync(Request(), applyTuning: true, Group, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(recorder.ContactGroups, Is.EquivalentTo(new[] { Group }));
            Assert.That(recorder.Rules!.Select(rule => rule.Id), Is.EqualTo(new[] { 1 }).AsCollection, "the group-scoped rule fires");
            Assert.That(resolver.Calls, Is.Zero, "the admin's own contact is not consulted while simulating");
        });
    }

    [Test]
    public async Task TheTesterFallsBackToTheRealVisitorWhenNoGroupIsChosen()
    {
        var resolver = new FakeResolver();
        var recorder = new RecordingStage();

        await Build(resolver, recorder).ExecuteAsync(Request(), applyTuning: true, string.Empty, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(resolver.Calls, Is.EqualTo(1));
            Assert.That(recorder.ContactGroups, Is.Empty);
            Assert.That(recorder.Rules, Is.Empty, "the admin is in no group, so the scoped rule does not fire");
        });
    }

    private static SearchRequest Request() =>
        new() { Index = "articles", Query = "espresso", Explain = true, Page = 1, PageSize = 10 };

    private static IndexSchema Schema() =>
        new("articles", [new SchemaField("title", SearchFieldKind.Text, true, false, false, true)]);

    private static QueryTesterSearch Build(IContactGroupResolver resolver, ISearchStage recorder)
    {
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        accessor.Exists("articles").Returns(true);
        accessor.GetAnalyzer("articles").Returns(new StandardAnalyzer(LuceneVersion.LUCENE_48));
        accessor.GetFacetsConfig("articles").Returns((FacetsConfig?)null);

        var schemaProvider = Substitute.For<IIndexSchemaProvider>();
        schemaProvider.GetSchemaAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult(Schema()));

        var source = Substitute.For<IRelevanceTuningSource>();
        source.GetRulesAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<TuningRule>>([Scoped]));
        source.GetSynonymsAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<TuningSynonym>>([]));
        source.GetStopwordsAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));
        source.GetFieldWeightsAsync("articles", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FieldWeight>>([]));

        ISearchStage[] stages =
        [
            new ResolveContactGroupsStage(resolver),
            new QueryRewriteStage(source, TimeProvider.System),
            new SynonymExpansionStage(source),
            recorder
        ];

        return new QueryTesterSearch(accessor, schemaProvider, stages, TimeProvider.System);
    }

    private sealed class FakeResolver : IContactGroupResolver
    {
        public int Calls { get; private set; }

        public Task<IReadOnlySet<string>> GetContactGroupsAsync(CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(ContactGroupSets.None);
        }
    }

    private sealed class RecordingStage : ISearchStage
    {
        public IReadOnlySet<string>? ContactGroups { get; private set; }

        public IReadOnlyList<TuningRule>? Rules { get; private set; }

        public int Order => SearchStageOrder.Project;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            ContactGroups = context.ContactGroups;
            Rules = context.Tuning.Rules;
            context.Response = new SearchResponse { Results = [], Total = 0, Page = 1, PageSize = 10, TotalPages = 0 };

            return Task.CompletedTask;
        }
    }
}
