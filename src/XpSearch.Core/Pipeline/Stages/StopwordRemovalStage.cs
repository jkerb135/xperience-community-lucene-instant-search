namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Drops the index's configured stopwords from the query before it is parsed (spec §8.3).
/// </summary>
/// <remarks>
/// A query made entirely of stopwords is left alone: turning "the who" into an empty query would
/// silently return the whole index, which is the opposite of what the visitor asked for.
/// </remarks>
public sealed class StopwordRemovalStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.StopwordRemoval;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwords = context.Tuning.Stopwords;

        if (stopwords.Count == 0 || context.QueryText.Length == 0)
        {
            return Task.CompletedTask;
        }

        var set = new HashSet<string>(stopwords, StringComparer.OrdinalIgnoreCase);

        string[] kept = [.. context.QueryText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !set.Contains(token))];

        if (kept.Length == 0)
        {
            return Task.CompletedTask;
        }

        context.QueryText = string.Join(' ', kept);

        if (context.QuerySlots.Count > 0)
        {
            context.QuerySlots = [.. context.QuerySlots.Where(slot => !set.Contains(slot[0]))];
        }

        return Task.CompletedTask;
    }
}
