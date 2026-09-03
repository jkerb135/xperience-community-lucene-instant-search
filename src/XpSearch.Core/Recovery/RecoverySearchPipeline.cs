using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search.Spell;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;

namespace XpSearch.Core.Recovery;

/// <summary>
/// Adds the two ways out of a dead end to a search that found nothing (SG-1): the corrected spelling
/// of <c>didYouMean</c> and the popular queries of <c>popularSearches</c>.
/// </summary>
/// <remarks>
/// <para>
/// It sits between the response cache and the pipeline, so the enrichment is part of the cached
/// entry - a dead end is answered from cache with its recovery intact, and the correction is spelled
/// once per query per TTL rather than once per visitor.
/// </para>
/// <para>
/// A probe request (ES-1) is never enriched: it exists to be counted, and its caller renders nothing.
/// That is also what keeps the verification search below from recursing.
/// </para>
/// </remarks>
public sealed class RecoverySearchPipeline : ISearchPipeline
{
    private readonly ISearchPipeline inner;
    private readonly IOptionsMonitor<XpSearchOptions> options;
    private readonly IQuerySuggestionSource querySuggestions;
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;

    /// <summary>Initializes a new instance of the <see cref="RecoverySearchPipeline"/> class.</summary>
    /// <param name="inner">The pipeline that answers the search, and the verification search.</param>
    /// <param name="options">The configured search options; both settings are per index.</param>
    /// <param name="querySuggestions">
    /// Supplies the popular queries. An empty prefix matches every logged query, so the popular
    /// searches are the same, already cached, computation the autocomplete uses.
    /// </param>
    /// <param name="accessor">The Lucene seam; the correction is spelled against the live index terms.</param>
    /// <param name="schemaProvider">Supplies the fields the query searched, which are the ones spelled against.</param>
    public RecoverySearchPipeline(
        ISearchPipeline inner,
        IOptionsMonitor<XpSearchOptions> options,
        IQuerySuggestionSource querySuggestions,
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(querySuggestions);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        this.inner = inner;
        this.options = options;
        this.querySuggestions = querySuggestions;
        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
    }

    /// <inheritdoc />
    public async Task<SearchResponse> ExecuteAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Total > 0 || request.Probe == true || string.IsNullOrWhiteSpace(request.Index))
        {
            return response;
        }

        var indexOptions = options.CurrentValue.Indexes[request.Index];

        if (indexOptions.PopularSearchesOnNoResults > 0)
        {
            var popular = await querySuggestions
                .SuggestAsync(request.Index, string.Empty, indexOptions.PopularSearchesOnNoResults, cancellationToken)
                .ConfigureAwait(false);

            if (popular.Count > 0)
            {
                response.PopularSearches = [.. popular];
            }
        }

        if (indexOptions.DidYouMean)
        {
            response.DidYouMean = await CorrectAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// Spells the query against the index and returns the correction only once a search has confirmed
    /// it finds something - an unverified correction is a second dead end with extra confidence.
    /// </summary>
    private async Task<string?> CorrectAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        string text = (request.Query ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return null;
        }

        var schema = await schemaProvider.GetSchemaAsync(request.Index, cancellationToken).ConfigureAwait(false);

        // The same field set the query was built over (BuildQueryStage): a correction spelled against
        // a field nobody searches would verify as another dead end.
        string[] fields = [.. schema.Fields.Where(field => field.Searchable).Select(LuceneFieldNames.SearchFieldName)];

        if (fields.Length == 0)
        {
            return null;
        }

        string? corrected = accessor.UseSearcher(request.Index, searcher => Correct(text, fields, searcher.IndexReader));

        if (corrected is null)
        {
            return null;
        }

        var verification = request.AsProbeFor(corrected);
        var verified = await inner.ExecuteAsync(verification, cancellationToken).ConfigureAwait(false);

        return verified.Total > 0 ? corrected : null;
    }

    /// <summary>
    /// Replaces every term the index does not know with its nearest known one, or returns
    /// <see langword="null"/> when nothing was worth changing.
    /// </summary>
    /// <remarks>
    /// <see cref="DirectSpellChecker"/> reads the live index terms, so there is no spell index to
    /// build or maintain. One suggestion per term is taken - the busiest field wins a tie, then the
    /// alphabetically first, so the same query always corrects to the same thing.
    /// </remarks>
    internal static string? Correct(string text, string[] fields, IndexReader reader)
    {
        var checker = new DirectSpellChecker();
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] corrected = [.. tokens];
        bool changed = false;

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].ToLowerInvariant();

            if (fields.Any(field => reader.DocFreq(new Term(field, token)) > 0))
            {
                continue;
            }

            SuggestWord? best = null;

            foreach (string field in fields)
            {
                var words = checker.SuggestSimilar(new Term(field, token), 1, reader);

                if (words.Length == 0)
                {
                    continue;
                }

                if (best is null
                    || words[0].Freq > best.Freq
                    || (words[0].Freq == best.Freq && string.CompareOrdinal(words[0].String, best.String) < 0))
                {
                    best = words[0];
                }
            }

            if (best is null)
            {
                continue;
            }

            corrected[i] = best.String;
            changed = true;
        }

        return changed ? string.Join(' ', corrected) : null;
    }
}
