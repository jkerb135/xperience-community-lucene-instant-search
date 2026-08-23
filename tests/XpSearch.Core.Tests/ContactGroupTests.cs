using NUnit.Framework;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Personalization;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>A resolver that answers whatever a test hands it (ADR-0021).</summary>
internal sealed class StubContactGroupResolver : IContactGroupResolver
{
    internal StubContactGroupResolver(params string[] groups) => Groups = ContactGroupSets.Of(groups);

    internal IReadOnlySet<string> Groups { get; set; }

    internal int Calls { get; private set; }

    public Task<IReadOnlySet<string>> GetContactGroupsAsync(CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(Groups);
    }
}

/// <summary>
/// Scoping a relevance rule to a contact group (ADR-0021): which rules survive selection, what the
/// explanation says, and that a personalised response is not served to another visitor.
/// </summary>
[TestFixture]
internal sealed class ContactGroupTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void AScopedRuleOnlyAppliesToVisitorsInItsGroup()
    {
        var scoped = RuleSelectionTests.Rule(id: 1, contactGroup: "grinder-shoppers");
        var unscoped = RuleSelectionTests.Rule(id: 2);

        Expect.Multiple(() =>
        {
            Assert.That(
                RuleSelection.Active([scoped, unscoped], "espresso", Now, ContactGroupSets.Of(["grinder-shoppers"])).Select(rule => rule.Id),
                Is.EqualTo(new[] { 1, 2 }).AsCollection,
                "a member gets both the scoped and the unscoped rule");

            Assert.That(
                RuleSelection.Active([scoped, unscoped], "espresso", Now, ContactGroupSets.Of(["kettle-shoppers"])).Select(rule => rule.Id),
                Is.EqualTo(new[] { 2 }).AsCollection,
                "a visitor in another group only gets the unscoped rule");

            Assert.That(
                RuleSelection.Active([scoped, unscoped], "espresso", Now).Select(rule => rule.Id),
                Is.EqualTo(new[] { 2 }).AsCollection,
                "no contact or no consent leaves only the unscoped rule");

            Assert.That(
                RuleSelection.Active([scoped], "espresso", Now, ContactGroupSets.Of(["GRINDER-Shoppers"])).Select(rule => rule.Id),
                Is.EqualTo(new[] { 1 }).AsCollection,
                "code names are compared the way Xperience treats them, case-insensitively");
        });
    }

    [Test]
    public void TheExplanationNamesTheGroupOnlyForAScopedRule() =>
        Expect.Multiple(() =>
        {
            Assert.That(
                RuleSelection.Explain(RuleSelectionTests.Rule(name: "Promote grinders", contactGroup: "grinder-shoppers")),
                Is.EqualTo("rule:Promote grinders (contact group grinder-shoppers)"));

            Assert.That(RuleSelection.Explain(RuleSelectionTests.Rule(name: "Promote grinders")), Is.EqualTo("rule:Promote grinders"));
        });

    [Test]
    public async Task TheStagePutsTheResolvedGroupsOnTheContext()
    {
        var resolver = new StubContactGroupResolver("grinder-shoppers");
        var context = Context();

        await new ResolveContactGroupsStage(resolver).ExecuteAsync(context, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(context.ContactGroups, Is.EquivalentTo(new[] { "grinder-shoppers" }));
            Assert.That(new ResolveContactGroupsStage(resolver).Order, Is.LessThan(SearchStageOrder.SynonymExpansion), "the groups must be known before rules are selected");
        });
    }

    [Test]
    public void AContextThatWasNeverResolvedIsInNoGroup() =>
        Assert.That(Context().ContactGroups, Is.Empty);

    /// <summary>
    /// A response shaped by a group-scoped rule is personal, so it must not be handed to a visitor
    /// who is in different groups.
    /// </summary>
    [Test]
    public void TheCacheKeyDependsOnTheVisitorsContactGroups()
    {
        var request = new SearchRequest { Index = "articles", Query = "espresso" };

        string anonymous = SearchCacheKey.Compute(request, "espresso");
        string member = SearchCacheKey.Compute(request, "espresso", ContactGroupSets.Of(["grinder-shoppers"]));
        string other = SearchCacheKey.Compute(request, "espresso", ContactGroupSets.Of(["kettle-shoppers"]));

        Expect.Multiple(() =>
        {
            Assert.That(member, Is.Not.EqualTo(anonymous));
            Assert.That(member, Is.Not.EqualTo(other));
            Assert.That(
                SearchCacheKey.Compute(request, "espresso", ContactGroupSets.Of(["b", "a"])),
                Is.EqualTo(SearchCacheKey.Compute(request, "espresso", ContactGroupSets.Of(["a", "b"]))),
                "the same membership in another order is the same visitor");
        });
    }

    private static SearchContext Context() =>
        new(
            new SearchRequest { Index = "articles", Query = "espresso" },
            new Abstractions.IndexSchema("articles", [new Abstractions.SchemaField("title", Abstractions.SearchFieldKind.Text, true, false, false, true)]),
            new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48),
            null,
            CancellationToken.None);
}
