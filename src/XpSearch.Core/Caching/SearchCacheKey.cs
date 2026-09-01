using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using XpSearch.Core.Contract;
using XpSearch.Core.Experiments;

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
    /// <param name="contactGroups">
    /// Code names of the visitor's contact groups. They are part of the key because a group-scoped
    /// rule (ADR-0021) makes the response personal, and one visitor's results must not be served to
    /// another.
    /// </param>
    /// <param name="experiment">
    /// The running experiment and variant the request was bucketed into (XP-1), or
    /// <see langword="null"/>. Variant B is answered from different tuning, so it must not share an
    /// entry with A. Nothing is added to the key while no experiment runs, so cache efficiency is
    /// untouched in the normal case.
    /// </param>
    /// <returns>A hex SHA-256 of the canonical request.</returns>
    /// <remarks>
    /// <c>queryId</c> is deliberately excluded: it correlates analytics events with one search and
    /// would otherwise make every request a cache miss.
    /// </remarks>
    public static string Compute(
        SearchRequest request,
        string normalizedQuery,
        IReadOnlySet<string>? contactGroups = null,
        ExperimentAssignment? experiment = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = new
        {
            request.Index,
            Query = normalizedQuery,
            request.Page,
            request.PageSize,
            request.Facets,
            Filters = request.Filters is null
                ? null
                : new object[]
                {
                    request.Filters.Facets ?? [],
                    request.Filters.Numeric ?? []
                },
            request.Sort,
            request.Language,
            request.Fields,
            request.Explain,
            Highlight = request.Highlight is null
                ? null
                : new object[]
                {
                    request.Highlight.Fields ?? [],
                    request.Highlight.PreTag ?? string.Empty,
                    request.Highlight.PostTag ?? string.Empty,
                    request.Highlight.SnippetLength ?? 0
                },
            ContactGroups = contactGroups is null
                ? []
                : contactGroups.OrderBy(group => group, StringComparer.OrdinalIgnoreCase).ToArray(),
            Experiment = experiment is { IsActive: true }
                ? $"{experiment.ExperimentId}:{experiment.Variant}"
                : null
        };

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, KeyOptions)));

        return Convert.ToHexString(hash);
    }
}
