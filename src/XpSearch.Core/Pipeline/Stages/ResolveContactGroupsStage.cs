using XpSearch.Core.Personalization;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Puts the visitor's contact groups on the context, once per request, before any tuning stage reads
/// them (ADR-0021). <c>SynonymExpansionStage</c> uses them to drop the rules scoped to a group the
/// visitor is not in.
/// </summary>
public sealed class ResolveContactGroupsStage : ISearchStage
{
    private readonly IContactGroupResolver resolver;

    /// <summary>Initializes a new instance of the <see cref="ResolveContactGroupsStage"/> class.</summary>
    /// <param name="resolver">Answers which contact groups the visitor is in.</param>
    public ResolveContactGroupsStage(IContactGroupResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        this.resolver = resolver;
    }

    /// <inheritdoc />
    public int Order => SearchStageOrder.ResolveContactGroups;

    /// <inheritdoc />
    public async Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ContactGroups = await resolver.GetContactGroupsAsync(cancellationToken).ConfigureAwait(false);
    }
}
