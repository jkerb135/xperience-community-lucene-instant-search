using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests that stages run in ascending order and that a consumer can insert one at a chosen slot.
/// </summary>
[TestFixture]
internal sealed class StageOrderingTests
{
    /// <summary>
    /// Analytics must stay out of the pipeline: it is journaled by the caching decorator, so a cached
    /// search is recorded too, and so a pipeline assembled by hand - the query tester - cannot enter
    /// the aggregate query log (spec §9.2).
    /// </summary>
    [Test]
    public void NoShippedStage_WritesAnalytics()
    {
        var journaling = typeof(SearchStageOrder).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ISearchStage).IsAssignableFrom(type))
            .Where(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(Analytics.ISearchRequestJournal)
                    || parameter.ParameterType == typeof(Analytics.IQueryLogQueue)
                    || parameter.ParameterType == typeof(Analytics.ISearchActivityLogger)));

        Assert.That(journaling, Is.Empty);
    }

    [Test]
    public void ShippedStages_OccupyTheDocumentedSlotsInSpecOrder()
    {
        ISearchStage[] stages =
        [
            new ProjectResponseStage(),
            new BuildQueryStage(new Fuzzy.DisabledTypoToleranceSource()),
            new FacetFilterStage(),
            new NumericFilterStage()
        ];

        var ordered = stages.OrderBy(stage => stage.Order).Select(stage => stage.Order);

        Assert.That(
            ordered,
            Is.EqualTo(new[]
            {
                SearchStageOrder.BuildQuery,
                SearchStageOrder.FacetFilters,
                SearchStageOrder.NumericFilters,
                SearchStageOrder.Project
            }).AsCollection);
    }

    [Test]
    public void ReservedSlots_LeaveRoomForTheLaterPhases() =>
        Assert.That(
            new[]
            {
                SearchStageOrder.Normalize,
                SearchStageOrder.ResolveContactGroups,
                SearchStageOrder.SynonymExpansion,
                SearchStageOrder.StopwordRemoval,
                SearchStageOrder.BuildQuery,
                SearchStageOrder.FacetFilters,
                SearchStageOrder.NumericFilters,
                SearchStageOrder.BoostRules,
                SearchStageOrder.Execute,
                SearchStageOrder.ScoreBreakdown,
                SearchStageOrder.PinnedAndBuried,
                SearchStageOrder.CollectFacets,
                SearchStageOrder.Highlight,
                SearchStageOrder.Project,
                SearchStageOrder.LogActivity
            },
            Is.Ordered.Ascending);

    /// <summary>
    /// The breakdown has to run inside the same picture the search produced: after the execute stage
    /// filled the documents and their Lucene ids, and before pinning moves them (QT-2).
    /// </summary>
    [Test]
    public void ScoreBreakdown_IsRegisteredBetweenExecuteAndPinning()
    {
        var registered = new ServiceCollection().AddXpSearch()
            .Where(descriptor => descriptor.ServiceType == typeof(ISearchStage))
            .Select(descriptor => descriptor.ImplementationType);

        Fixtures.Expect.Multiple(() =>
        {
            Assert.That(registered, Does.Contain(typeof(ScoreBreakdownStage)));
            Assert.That(SearchStageOrder.ScoreBreakdown, Is.EqualTo(850));
            Assert.That(SearchStageOrder.ScoreBreakdown, Is.GreaterThan(SearchStageOrder.Execute));
            Assert.That(SearchStageOrder.ScoreBreakdown, Is.LessThan(SearchStageOrder.PinnedAndBuried));
        });
    }

    [Test]
    public void AddXpSearchStage_RegistersACustomStageAtItsOwnOrder()
    {
        var services = new ServiceCollection();
        services.AddXpSearchStage<CustomStage>();

        var stage = services.BuildServiceProvider().GetServices<ISearchStage>().Single();

        Assert.That(stage.Order, Is.EqualTo(4242));
    }

    [Test]
    public void AddXpSearchStage_WithAnExplicitOrderOverridesTheTypesOwn()
    {
        var services = new ServiceCollection();
        services.AddXpSearchStage<CustomStage>(SearchStageOrder.BoostRules);

        var stage = services.BuildServiceProvider().GetServices<ISearchStage>().Single();

        Assert.That(stage.Order, Is.EqualTo(SearchStageOrder.BoostRules));
    }

    private sealed class CustomStage : ISearchStage
    {
        public int Order => 4242;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
