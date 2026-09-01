using XpSearch.Core.Experiments;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Puts the index's running experiment and the visitor's variant on the context, before any tuning
/// stage reads tuning (XP-1).
/// </summary>
/// <remarks>
/// The resolver memoizes its answer on the request, so this costs nothing beyond a lookup: the
/// caching decorator has already asked the same question to build the cache key.
/// </remarks>
public sealed class ResolveExperimentStage : ISearchStage
{
    private readonly IExperimentAssignmentResolver resolver;

    /// <summary>Initializes a new instance of the <see cref="ResolveExperimentStage"/> class.</summary>
    /// <param name="resolver">Answers which experiment and variant apply to the request.</param>
    public ResolveExperimentStage(IExperimentAssignmentResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        this.resolver = resolver;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.ResolveExperiment;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Experiment = await resolver
            .GetAssignmentAsync(context.Request.Index ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }
}
