namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Drops the index's configured stopwords from the query before it is parsed (spec §8.3).
/// </summary>
/// <remarks>
/// A query made entirely of stopwords is left alone: turning "the who" into an empty query would
/// silently return the whole index, which is the opposite of what the visitor asked for. A quoted
/// phrase keeps every word it holds (PH-1): the visitor asked for those words in that order, and
/// whether "the" is worth a position there is the analyzer's decision, not the tuning list's.
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
        var segments = QueryPhrases.Split(context.QueryText);

        if (stopwords.Count == 0 || segments.Count == 0)
        {
            return Task.CompletedTask;
        }

        var set = new HashSet<string>(stopwords, StringComparer.OrdinalIgnoreCase);

        string[] kept = [.. segments
            .Select(segment => segment.IsPhrase ? '"' + segment.Text + '"' : Strip(segment.Text, set))
            .Where(part => part.Length > 0)];

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

    private static string Strip(string text, HashSet<string> stopwords) =>
        string.Join(' ', text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !stopwords.Contains(token)));
}
