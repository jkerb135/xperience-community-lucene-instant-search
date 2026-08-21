using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Options;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Projects the raw Lucene facet counts onto the dimensions the request asked for.
/// </summary>
public sealed class CollectFacetsStage : ISearchStage
{
    private readonly IFacetProvider provider;
    private readonly XpSearchOptions options;

    /// <summary>Initializes a new instance of the <see cref="CollectFacetsStage"/> class.</summary>
    /// <param name="provider">The facet provider to read counts from.</param>
    /// <param name="options">The configured search options.</param>
    public CollectFacetsStage(IFacetProvider provider, IOptions<XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);

        this.provider = provider;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.CollectFacets;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RequestedFacets.Count > 0)
        {
            context.FacetValues = provider.GetFacets(context, context.RequestedFacets, options.MaxFacetValues);
        }

        return Task.CompletedTask;
    }
}
