using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Lists an index's content types from its stored configuration.
/// </summary>
/// <remarks>
/// <c>LuceneIndex</c> exposes only <c>IncludedReusableContentTypes</c> publicly in
/// <c>Kentico.Xperience.Lucene</c> 15.0.5 - the web page channel configuration is internal - so the
/// web page types have to come from <see cref="ILuceneConfigurationStorageService"/>, which reads the
/// same rows the admin Search application writes.
/// </remarks>
public sealed class LuceneIndexContentTypeSource : IIndexContentTypeSource
{
    private readonly ILuceneConfigurationStorageService storage;

    /// <summary>Initializes a new instance of the <see cref="LuceneIndexContentTypeSource"/> class.</summary>
    /// <param name="storage">The integration's index configuration store.</param>
    public LuceneIndexContentTypeSource(ILuceneConfigurationStorageService storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        this.storage = storage;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetContentTypesAsync(string indexName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);

        var configuration = await storage.GetIndexDataOrNullAsync(indexName).ConfigureAwait(false)
            ?? throw new IndexNotFoundException(indexName);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in configuration.Channels ?? [])
        {
            foreach (var path in channel.IncludedPaths ?? [])
            {
                foreach (var contentType in path.ContentTypes ?? [])
                {
                    names.Add(contentType.ContentTypeName);
                }
            }
        }

        foreach (string reusable in configuration.ReusableContentTypeNames ?? [])
        {
            names.Add(reusable);
        }

        return [.. names];
    }
}
