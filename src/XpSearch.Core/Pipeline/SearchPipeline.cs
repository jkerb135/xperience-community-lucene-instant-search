using System.Diagnostics;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

namespace XpSearch.Core.Pipeline;

/// <summary>
/// The default <see cref="ISearchPipeline"/>: resolves the index, then runs every registered
/// <see cref="ISearchStage"/> in ascending <see cref="ISearchStage.Order"/>.
/// </summary>
/// <remarks>
/// Resolving the index happens before the first stage because a stage cannot run without a schema and
/// an analyzer, and an unknown index is a 404 rather than a validation error. Everything else,
/// including request validation, is a stage a consumer can replace or wrap.
/// </remarks>
public sealed class SearchPipeline : ISearchPipeline
{
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;
    private readonly ISearchStage[] stages;

    /// <summary>Initializes a new instance of the <see cref="SearchPipeline"/> class.</summary>
    /// <param name="accessor">The Lucene seam.</param>
    /// <param name="schemaProvider">Supplies the schema of the index being searched.</param>
    /// <param name="stages">Every registered stage; ordering is applied here, not by DI.</param>
    public SearchPipeline(
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        IEnumerable<ISearchStage> stages)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(stages);

        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
        this.stages = [.. stages.OrderBy(stage => stage.Order)];
    }

    /// <inheritdoc />
    public async Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Index))
        {
            throw new SearchValidationException("index", "index is required.");
        }

        if (!accessor.Exists(request.Index))
        {
            throw new IndexNotFoundException(request.Index);
        }

        var schema = await schemaProvider.GetSchemaAsync(request.Index, cancellationToken).ConfigureAwait(false);
        var context = new SearchContext(
            request,
            schema,
            accessor.GetAnalyzer(request.Index),
            accessor.GetFacetsConfig(request.Index),
            cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();

        var response = context.Response
            ?? throw new InvalidOperationException("The search pipeline produced no response; a stage removed or replaced the projection stage.");

        response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        return response;
    }
}
