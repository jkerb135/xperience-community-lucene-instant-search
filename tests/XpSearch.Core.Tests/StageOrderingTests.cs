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
    [Test]
    public void ShippedStages_OccupyTheDocumentedSlotsInSpecOrder()
    {
        ISearchStage[] stages =
        [
            new ProjectResponseStage(),
            new BuildQueryStage(),
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
                SearchStageOrder.PinnedAndBuried,
                SearchStageOrder.CollectFacets,
                SearchStageOrder.Highlight,
                SearchStageOrder.Project,
                SearchStageOrder.LogActivity
            },
            Is.Ordered.Ascending);

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
