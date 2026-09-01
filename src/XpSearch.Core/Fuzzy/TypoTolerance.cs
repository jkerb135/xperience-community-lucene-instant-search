namespace XpSearch.Core.Fuzzy;

/// <summary>
/// The fixed typo tolerance policy (FZ-1): how many edits a query term may be off by. One toggle, no
/// knobs - a per-term or per-request override is a future contract change, see KNOWN-LIMITATIONS.
/// </summary>
public static class FuzzyPolicy
{
    /// <summary>
    /// How many leading characters must match exactly. One: it keeps precision up (a typo in the first
    /// letter is rare) and the Levenshtein automaton cheap.
    /// </summary>
    public const int PrefixLength = 1;

    /// <summary>The number of edits a term of this length may be off by.</summary>
    /// <param name="token">One whitespace-separated query token.</param>
    /// <returns>0 for a term that must match exactly, otherwise the maximum edit distance.</returns>
    /// <remarks>
    /// Short words are skipped because almost every other short word is one edit away, and an all-digit
    /// token is skipped because a wrong digit is a different value, not a misspelling.
    /// </remarks>
    public static int MaxEdits(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length <= 2 || token.All(char.IsDigit))
        {
            return 0;
        }

        return token.Length <= 5 ? 1 : 2;
    }

    /// <summary>The Lucene query-syntax suffix that makes a term fuzzy, or an empty string when it stays exact.</summary>
    /// <param name="token">One whitespace-separated query token.</param>
    /// <returns><c>~1</c>, <c>~2</c> or an empty string.</returns>
    public static string Suffix(string token) =>
        MaxEdits(token) switch
        {
            0 => string.Empty,
            int edits => "~" + edits.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
}

/// <summary>
/// Whether an index answers free-text queries with typo tolerance (FZ-1). The query stage reads it to
/// build the query, and the response cache reads it to key the answer.
/// </summary>
public interface ITypoToleranceSource
{
    /// <summary>Reads one index's typo tolerance setting.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the index opted in.</returns>
    Task<bool> IsEnabledAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>Typo tolerance off for every index: the behaviour of a host that never turned it on.</summary>
public sealed class DisabledTypoToleranceSource : ITypoToleranceSource
{
    /// <inheritdoc />
    public Task<bool> IsEnabledAsync(string indexName, CancellationToken cancellationToken) => Task.FromResult(false);
}
