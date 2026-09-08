namespace XpSearch.Core.Pipeline;

/// <summary>One piece of the visitor's query: either a quoted phrase or free text (PH-1).</summary>
/// <param name="Text">The text of the segment - for a phrase, what stood between the quotes.</param>
/// <param name="IsPhrase">Whether the words must appear adjacent and in order.</param>
public readonly record struct QuerySegment(string Text, bool IsPhrase);

/// <summary>
/// Splits the visitor's query into quoted phrases and the loose text around them (PH-1). Double
/// quotes are the one piece of syntax the endpoint honours; every other character stays literal.
/// </summary>
/// <remarks>
/// An unbalanced quote is not syntax: the trailing text keeps its quote character and is escaped
/// with the rest of the loose text, so a visitor who typed one quote gets the pre-PH-1 result rather
/// than an error. An empty phrase contributes nothing. Smart quotes are folded to <c>"</c> first
/// because visitors paste from word processors.
/// </remarks>
public static class QueryPhrases
{
    /// <summary>Splits query text into phrase and loose segments, in the order they were typed.</summary>
    /// <param name="text">The normalized query text.</param>
    /// <returns>The segments; empty when the text holds nothing but blanks and empty quotes.</returns>
    public static IReadOnlyList<QuerySegment> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string normalized = text.Replace('“', '"').Replace('”', '"');
        var segments = new List<QuerySegment>();
        int position = 0;

        while (true)
        {
            int open = normalized.IndexOf('"', position);
            int close = open < 0 ? -1 : normalized.IndexOf('"', open + 1);

            if (close < 0)
            {
                break;
            }

            Add(segments, normalized.AsSpan()[position..open], phrase: false);
            Add(segments, normalized.AsSpan()[(open + 1)..close], phrase: true);
            position = close + 1;
        }

        Add(segments, normalized.AsSpan()[position..], phrase: false);

        return segments;
    }

    /// <summary>The text outside every phrase: what keeps the pre-PH-1 behaviour.</summary>
    /// <param name="text">The normalized query text.</param>
    /// <returns>The free text; empty when the query is nothing but phrases.</returns>
    public static string LooseText(string? text) =>
        string.Join(' ', Split(text).Where(segment => !segment.IsPhrase).Select(segment => segment.Text));

    private static void Add(List<QuerySegment> segments, ReadOnlySpan<char> text, bool phrase)
    {
        var trimmed = text.Trim();

        if (!trimmed.IsEmpty)
        {
            segments.Add(new QuerySegment(trimmed.ToString(), phrase));
        }
    }
}
