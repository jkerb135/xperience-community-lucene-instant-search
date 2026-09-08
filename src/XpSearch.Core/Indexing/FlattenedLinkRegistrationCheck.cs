using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Warns at startup about a <see cref="XpSearchIndexingOptions.FlattenLinkedItems"/> registration an
/// index cannot act on: Kentico only raises a reusable-item event for a content type the index lists
/// under its reusable content types, so a linked type missing from that list leaves the pages that
/// flatten it stale until a rebuild.
/// </summary>
/// <remarks>
/// It reports; it never edits the stored index configuration - what an index covers is the
/// administration's decision, and silently widening it would change what the index contains.
/// </remarks>
public sealed class FlattenedLinkRegistrationCheck
{
    private readonly ILuceneIndexAccessor accessor;
    private readonly XpSearchIndexingOptions options;
    private readonly ILogger<FlattenedLinkRegistrationCheck> logger;

    /// <summary>Initializes a new instance of the <see cref="FlattenedLinkRegistrationCheck"/> class.</summary>
    /// <param name="accessor">The Lucene seam, used to read each index's stored definition.</param>
    /// <param name="options">The flatten registrations to check.</param>
    /// <param name="logger">Logger.</param>
    public FlattenedLinkRegistrationCheck(
        ILuceneIndexAccessor accessor,
        XpSearchIndexingOptions options,
        ILogger<FlattenedLinkRegistrationCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.accessor = accessor;
        this.options = options;
        this.logger = logger;
    }

    /// <summary>Checks every registered index and logs one warning per index and missing content type.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every index has been checked.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var registrations = options.FlattenedLinks();

        if (registrations.Count == 0)
        {
            return;
        }

        foreach (string indexName in accessor.IndexNames())
        {
            IndexDefinition definition;

            try
            {
                definition = await accessor.GetDefinitionAsync(indexName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "The definition of index {Index} could not be read; its flatten registrations are not checked.", indexName);
                continue;
            }

            var covered = new HashSet<string>(definition.WebPageContentTypeNames, StringComparer.OrdinalIgnoreCase);
            var reusable = new HashSet<string>(definition.ReusableContentTypeNames, StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var link in registrations.Where(link => covered.Contains(link.ContentTypeName)))
            {
                foreach (string linkedContentType in link.LinkedContentTypeNames)
                {
                    if (reusable.Contains(linkedContentType) || !reported.Add(linkedContentType))
                    {
                        continue;
                    }

                    logger.LogWarning(
                        "Index {Index} indexes {ContentType}, which flattens the items linked from its {FieldName} field, but "
                        + "{LinkedContentType} is not among the index's reusable content types. Kentico raises no event for that type, "
                        + "so editing one of its items leaves those pages stale until the index is rebuilt. Add it to the index's "
                        + "reusable content types in the Search application, or drop it from the FlattenLinkedItems registration.",
                        indexName,
                        link.ContentTypeName,
                        link.LinkedFieldName,
                        linkedContentType);
                }
            }
        }
    }
}
