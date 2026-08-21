using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using XpSearch.Core.Contract;

namespace XpSearch.Core.Caching;

/// <summary>
/// Builds the cache key of a search request: a hash of the normalized request (spec §4.7).
/// </summary>
public static class SearchCacheKey
{
    private static readonly JsonSerializerOptions KeyOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Computes the cache key of a request.</summary>
    /// <param name="request">The request to key.</param>
    /// <param name="normalizedQuery">The query text after normalization, so that differently cased input shares an entry.</param>
    /// <returns>A hex SHA-256 of the canonical request.</returns>
    /// <remarks>
    /// <c>queryId</c> is deliberately excluded: it correlates analytics events with one search and
    /// would otherwise make every request a cache miss.
    /// </remarks>
    public static string Compute(SearchRequest request, string normalizedQuery)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = new
        {
            request.Index,
            Query = normalizedQuery,
            request.Page,
            request.HitsPerPage,
            request.Facets,
            request.FacetFilters,
            request.NumericFilters,
            request.Sort,
            request.Language,
            request.AttributesToRetrieve,
            request.Explain,
            Highlight = request.Highlight is null
                ? null
                : new object[]
                {
                    request.Highlight.Fields ?? [],
                    request.Highlight.PreTag ?? string.Empty,
                    request.Highlight.PostTag ?? string.Empty,
                    request.Highlight.SnippetLength ?? 0
                }
        };

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, KeyOptions)));

        return Convert.ToHexString(hash);
    }
}
