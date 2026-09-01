using System.Text.Json;

using Kentico.Xperience.Lucene.Core;

using Lucene.Net.Index;
using Lucene.Net.Search;

using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;

// Lucene.Net.Index declares a type of the same name.
using IndexNotFoundException = XpSearch.Core.Abstractions.IndexNotFoundException;

namespace XpSearch.Core.Search;

/// <summary>
/// Autocomplete by prefix-matching the index's suggest field and returning the matching documents -
/// the document-suggestion mode of spec §4.3.
/// </summary>
/// <remarks>
/// The other mode, query suggestions from logged popular queries (spec §13.6), is answered by
/// <see cref="IQuerySuggestionSource"/> from the Phase 6 query log.
/// </remarks>
public sealed class DocumentSuggestService : ISuggestService
{
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;
    private readonly IQuerySuggestionSource querySuggestions;
    private readonly XpSearchOptions options;

    /// <summary>Initializes a new instance of the <see cref="DocumentSuggestService"/> class.</summary>
    /// <param name="accessor">The Lucene seam.</param>
    /// <param name="schemaProvider">Supplies the schema of the index being suggested from.</param>
    /// <param name="querySuggestions">Answers for an index configured for query suggestions.</param>
    /// <param name="options">The configured search options.</param>
    public DocumentSuggestService(
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        IQuerySuggestionSource querySuggestions,
        IOptions<XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(querySuggestions);
        ArgumentNullException.ThrowIfNull(options);

        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
        this.querySuggestions = querySuggestions;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public async Task<SuggestResponse> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken)
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

        var indexOptions = options.Indexes[request.Index];

        string prefix = NormalizePrefix(request.Query);

        if (prefix.Length == 0)
        {
            return Empty();
        }

        int limit = ValidateLimit(request.Limit);

        if (indexOptions.SuggestMode == SuggestMode.QuerySuggestions)
        {
            return new SuggestResponse
            {
                Suggestions = [.. await SuggestQueriesAsync(request.Index, prefix, limit, cancellationToken).ConfigureAwait(false)]
            };
        }

        if (indexOptions.SuggestMode == SuggestMode.Mixed)
        {
            var queries = await SuggestQueriesAsync(request.Index, prefix, limit, cancellationToken).ConfigureAwait(false);
            var documents = await SuggestDocumentsAsync(request, indexOptions, prefix, limit, cancellationToken).ConfigureAwait(false);

            return new SuggestResponse { Suggestions = [.. Mix(queries, documents, limit)] };
        }

        return new SuggestResponse
        {
            Suggestions = [.. await SuggestDocumentsAsync(request, indexOptions, prefix, limit, cancellationToken).ConfigureAwait(false)]
        };
    }

    /// <summary>
    /// Interleaves the two sources into one response of at most <paramref name="limit"/> entries:
    /// queries lead with half of it (at least one whenever there is one), documents fill the rest, and
    /// whatever one source leaves unused is given back to the other.
    /// </summary>
    /// <param name="queries">The query suggestions, most searched first.</param>
    /// <param name="documents">The document suggestions, best match first.</param>
    /// <param name="limit">The most suggestions the response may carry.</param>
    /// <returns>The mixed suggestions, queries first.</returns>
    internal static IEnumerable<Suggestion> Mix(
        IReadOnlyList<Suggestion> queries,
        IReadOnlyList<Suggestion> documents,
        int limit)
    {
        int queryTake = Math.Min(queries.Count, Math.Max(1, limit / 2));
        int documentTake = Math.Min(documents.Count, limit - queryTake);

        // Whatever the documents did not use goes back to the queries, and vice versa through the
        // line above: neither source is padded beyond what it actually returned.
        queryTake = Math.Min(queries.Count, limit - documentTake);

        return queries.Take(queryTake).Concat(documents.Take(documentTake));
    }

    /// <summary>A query suggestion has no document behind it, so it carries text only.</summary>
    private async Task<IReadOnlyList<Suggestion>> SuggestQueriesAsync(
        string index,
        string prefix,
        int limit,
        CancellationToken cancellationToken)
    {
        var queries = await querySuggestions.SuggestAsync(index, prefix, limit, cancellationToken).ConfigureAwait(false);

        return [.. queries.Select(text => new Suggestion { Text = text, Group = Group.Query })];
    }

    private async Task<IReadOnlyList<Suggestion>> SuggestDocumentsAsync(
        SuggestRequest request,
        XpSearchIndexOptions indexOptions,
        string prefix,
        int limit,
        CancellationToken cancellationToken)
    {
        var schema = await schemaProvider.GetSchemaAsync(request.Index, cancellationToken).ConfigureAwait(false);
        var suggestField = schema.Find(indexOptions.SuggestField)
            ?? throw new SearchValidationException(
                "index",
                $"Index '{request.Index}' is configured to suggest from '{indexOptions.SuggestField}', which it has no such attribute for.");

        var query = BuildQuery(prefix, suggestField, request.Language);

        var suggestions = accessor.UseSearcher(request.Index, searcher =>
        {
            var matches = searcher.Search(query, limit);
            var suggested = new List<Suggestion>(matches.ScoreDocs.Length);

            foreach (var scoreDoc in matches.ScoreDocs)
            {
                var document = searcher.Doc(scoreDoc.Doc);
                string? text = document.Get(suggestField.LuceneName);

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                suggested.Add(new Suggestion
                {
                    Text = text,
                    Group = Group.Document,
                    Url = WebUrl.ToRootRelative(document.Get(BaseDocumentProperties.URL)),
                    Result = new Result
                    {
                        Id = document.Get(BaseDocumentProperties.ID) ?? string.Empty,
                        Score = scoreDoc.Score,
                        Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            [suggestField.Name] = JsonSerializer.SerializeToElement(text)
                        }
                    }
                });
            }

            return suggested;
        });

        return suggestions;
    }

    private static SuggestResponse Empty() => new() { Suggestions = [] };

    private static string NormalizePrefix(string? query) => (query ?? string.Empty).Trim().ToLowerInvariant();

    private int ValidateLimit(long? limit)
    {
        if (limit is null)
        {
            return options.DefaultSuggestLimit;
        }

        if (limit < 1)
        {
            throw new SearchValidationException("limit", "limit must be one or greater.");
        }

        return (int)Math.Min(limit.Value, options.MaxSuggestLimit);
    }

    private static Query BuildQuery(string prefix, SchemaField suggestField, string? language)
    {
        Query prefixQuery = new PrefixQuery(new Term(LuceneFieldNames.SearchFieldName(suggestField), prefix));

        if (string.IsNullOrWhiteSpace(language))
        {
            return prefixQuery;
        }

        return new BooleanQuery
        {
            { prefixQuery, Occur.MUST },
            { new TermQuery(new Term(BaseDocumentProperties.LANGUAGE_NAME, language)), Occur.MUST }
        };
    }
}
