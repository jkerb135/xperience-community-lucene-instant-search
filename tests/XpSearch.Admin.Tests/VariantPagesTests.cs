using System.Reflection;

using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Covers the variant-B tuning pages (XP-1): they are the live pages parameterized by an experiment,
/// not copies of them, and everything they write stays inside the variant they were reached through.
/// </summary>
[TestFixture]
internal sealed class VariantPagesTests
{
    private const int IndexIdentifier = 7;
    private const int ExperimentIdentifier = 11;
    private const string IndexName = "articles";

    /// <summary>Live page, variant page and the base that holds the behaviour both run.</summary>
    private static readonly (Type Live, Type Variant, Type Shared)[] Pairs =
    [
        (typeof(RuleListing), typeof(VariantRuleListing), typeof(RuleListingBase)),
        (typeof(SynonymListing), typeof(VariantSynonymListing), typeof(SynonymListingBase)),
        (typeof(StopwordListing), typeof(VariantStopwordListing), typeof(StopwordListingBase)),
        (typeof(FieldWeightListing), typeof(VariantFieldWeightListing), typeof(FieldWeightListingBase)),
        (typeof(SynonymEdit), typeof(VariantSynonymEdit), typeof(SynonymEditPageBase)),
        (typeof(SynonymCreate), typeof(VariantSynonymCreate), typeof(SynonymEditPageBase)),
        (typeof(StopwordEdit), typeof(VariantStopwordEdit), typeof(StopwordEditPageBase)),
        (typeof(StopwordCreate), typeof(VariantStopwordCreate), typeof(StopwordEditPageBase)),
        (typeof(FieldWeightEdit), typeof(VariantFieldWeightEdit), typeof(FieldWeightEditPageBase)),
        (typeof(FieldWeightCreate), typeof(VariantFieldWeightCreate), typeof(FieldWeightEditPageBase)),
        (typeof(RuleEdit), typeof(VariantRuleEdit), typeof(RuleBuilderPage)),
        (typeof(RuleCreate), typeof(VariantRuleCreate), typeof(RuleBuilderPage)),
    ];

    private static readonly Type[] VariantListings =
    [
        typeof(VariantRuleListing),
        typeof(VariantSynonymListing),
        typeof(VariantStopwordListing),
        typeof(VariantFieldWeightListing),
    ];

    private static IEnumerable<UIPageAttribute> Registrations =>
        typeof(ExperimentScope).Assembly.GetCustomAttributes<UIPageAttribute>();

    /// <summary>
    /// The amendment asks for the draft to be edited "with the same pages the live tuning uses". Both
    /// sides therefore run one class's body: a variant page that stopped sharing it would be a fork.
    /// </summary>
    [TestCaseSource(nameof(Pairs))]
    public void AVariantPageSharesTheLivePagesBehaviour((Type Live, Type Variant, Type Shared) pair)
    {
        Expect.Multiple(() =>
        {
            Assert.That(pair.Shared.IsAssignableFrom(pair.Live), $"{pair.Live.Name} must run {pair.Shared.Name}'s body");
            Assert.That(pair.Shared.IsAssignableFrom(pair.Variant), $"{pair.Variant.Name} must run {pair.Shared.Name}'s body");
            Assert.That(pair.Variant.IsSubclassOf(pair.Live), Is.False, "neither page is the other's special case");
        });
    }

    /// <summary>The variant pages render with the same client templates as the live ones.</summary>
    [TestCaseSource(nameof(Pairs))]
    public void AVariantPageUsesTheSameTemplateAsTheLiveOne((Type Live, Type Variant, Type Shared) pair)
    {
        var templates = Registrations.ToDictionary(page => page.Type, page => page.TemplateName);

        Assert.That(templates.GetValueOrDefault(pair.Variant), Is.EqualTo(templates.GetValueOrDefault(pair.Live)));
    }

    /// <summary>Every variant editor hangs inside the experiment whose draft it edits.</summary>
    [Test]
    public void TheVariantEditorsHangUnderTheExperimentInTheUrl()
    {
        var parents = Registrations.ToDictionary(page => page.Type, page => page.ParentType);

        Assert.That(
            VariantListings.Select(listing => parents.GetValueOrDefault(listing)),
            Is.All.EqualTo(typeof(ExperimentSection)));
    }

    /// <summary>
    /// A page command has to be a plain method on the final page class - inherited and re-annotated
    /// ones have failed discovery on the host (docs/internal/agent-primer.md).
    /// </summary>
    [TestCaseSource(nameof(VariantListings))]
    public void AVariantListingDeclaresItsOwnDeleteCommand(Type listing)
    {
        var method = listing.GetMethod("DeleteRow", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.That(method, Is.Not.Null, $"{listing.Name} must declare its own delete command");

        Expect.Multiple(() =>
        {
            Assert.That(method!.GetCustomAttributes<PageCommandAttribute>(inherit: false).Single().Permission, Is.EqualTo(SystemPermissions.DELETE));
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<ICommandResponse<RowActionResult>>)));
        });
    }

    /// <summary>
    /// The delete carries only a row id, so the listing's variant filter does not reach it. A row the
    /// scoped provider cannot vouch for is refused, exactly as a foreign index's row is (ADR-0017).
    /// </summary>
    [Test]
    public void AVariantDeleteRefusesARowItCannotProveIsInThisVariant()
    {
        var listing = new VariantSynonymListing(
            Storage.Holding(IndexIdentifier, IndexName),
            Substitute.For<IInfoProvider<XpSearchSynonymInfo>, IInfoByIdProvider<XpSearchSynonymInfo>>(),
            Substitute.For<IExperimentCatalog>())
        {
            IndexIdentifier = IndexIdentifier,
            ExperimentIdentifier = ExperimentIdentifier
        };

        var response = listing.DeleteRow(1).GetAwaiter().GetResult();

        Expect.Multiple(() =>
        {
            Assert.That(response.Messages.Single().Message, Is.EqualTo(IndexScope.CrossIndexDeleteRefusal));
            Assert.That(response.Result.Reload, Is.False);
        });
    }

    /// <summary>The scope is what keeps a page pointed at one experiment of one index.</summary>
    [Test]
    public void TheScopeResolvesOnlyTheExperimentOfTheIndexInTheUrl()
    {
        var catalog = Substitute.For<IExperimentCatalog>();
        catalog.Get(ExperimentIdentifier).Returns(Summary(ExperimentState.Draft));

        Expect.Multiple(() =>
        {
            Assert.That(ExperimentScope.Resolve(catalog, ExperimentIdentifier, IndexName), Is.Not.Null);
            Assert.That(ExperimentScope.Resolve(catalog, ExperimentIdentifier, "products"), Is.Null, "another index's URL resolves to nothing");
            Assert.That(ExperimentScope.Resolve(catalog, 0, IndexName), Is.Null);
            Assert.That(ExperimentScope.Variant(ExperimentIdentifier), Is.EqualTo(new TuningVariant(ExperimentIdentifier)));
            Assert.That(ExperimentScope.Variant(0), Is.EqualTo(TuningVariant.Live));
        });
    }

    /// <summary>Both parameterized ancestors are needed, in the order the URL spells them.</summary>
    [Test]
    public void TheRouteCarriesTheIndexAndTheExperiment() =>
        Assert.That(
            ExperimentScope.Route(IndexIdentifier, ExperimentIdentifier).Select(entry => entry.Key),
            Is.EqualTo(new[] { typeof(IndexTuningSection), typeof(ExperimentSection) }));

    /// <summary>
    /// The banner is what stops an editor changing the wrong set of rows, so it has to name the
    /// experiment and say plainly when the draft is frozen.
    /// </summary>
    [Test]
    public void TheBannerNamesTheExperimentAndSaysWhenItIsReadOnly()
    {
        var draft = ExperimentScope.Banner(Summary(ExperimentState.Draft));
        var running = ExperimentScope.Banner(Summary(ExperimentState.Running));

        Expect.Multiple(() =>
        {
            Assert.That(draft.Headline, Is.EqualTo("Variant B draft — Boost recent"));
            Assert.That(draft.Content, Does.Contain("not the live tuning"));
            Assert.That(running.Content, Does.Contain("read-only"));
            Assert.That(ExperimentScope.IsDraft(Summary(ExperimentState.Draft)), Is.True);
            Assert.That(ExperimentScope.IsDraft(Summary(ExperimentState.Running)), Is.False, "a started experiment's B is frozen");
            Assert.That(ExperimentScope.IsDraft(null), Is.False);
            Assert.That(ExperimentScope.Banner(null).Headline, Does.Contain("not found"));
        });
    }

    private static ExperimentSummary Summary(ExperimentState state) =>
        new(ExperimentIdentifier, IndexName, "Boost recent", 50, state, ExperimentOutcome.None, null, null);
}
