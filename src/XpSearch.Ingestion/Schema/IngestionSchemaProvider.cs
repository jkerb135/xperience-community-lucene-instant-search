using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;
using XpSearch.Ingestion.Options;

namespace XpSearch.Ingestion.Schema;

/// <summary>
/// An index's schema as ingestion sees it: the fields, plus whether undeclared attributes are allowed.
/// </summary>
/// <param name="Fields">The fields a pushed document may carry.</param>
/// <param name="AllowDynamicFields">Whether an attribute the schema does not declare is accepted.</param>
public sealed record IngestionSchema(IndexSchema Fields, bool AllowDynamicFields);

/// <summary>
/// Supplies the schema pushed documents are validated against.
/// </summary>
public interface IIngestionSchemaProvider
{
    /// <summary>Gets the ingestion schema of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="IndexNotFoundException">The index is not registered.</exception>
    Task<IngestionSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>
/// Lists the registered indexes and the strategy class each one uses, so declared schemas can be read
/// off the strategy's attributes.
/// </summary>
/// <remarks>
/// A seam over <c>ILuceneIndexManager</c>, which cannot be stood up outside a running Xperience
/// application - the same reason <c>XpSearch.Core</c>'s <c>ILuceneIndexAccessor</c> exists.
/// </remarks>
public interface IIndexStrategySource
{
    /// <summary>Gets the code names of every registered index.</summary>
    /// <returns>The index names.</returns>
    IReadOnlyList<string> GetIndexNames();

    /// <summary>Gets the indexing strategy class an index is configured with.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The strategy type, or <see langword="null"/> when the index is not registered.</returns>
    Type? GetStrategyType(string indexName);
}

/// <summary>
/// Merges the schema auto-detected from the Xperience content types an index covers with the schema
/// declared in code on the index's strategy class, and with the per-index configuration (spec §10.3).
/// </summary>
/// <remarks>
/// Precedence, highest first: configured fields (<see cref="XpSearchIngestionIndexOptions.Fields"/>),
/// fields declared with <see cref="XpSearchFieldAttribute"/>, detected content type fields. Editing a
/// schema in the admin UI is the Search tuning application's job (spec §10.8) and is not implemented
/// here; a code-declared schema is the supported route today.
/// </remarks>
public sealed class IngestionSchemaProvider : IIngestionSchemaProvider
{
    private readonly IIndexSchemaProvider detected;
    private readonly IIndexStrategySource strategies;
    private readonly XpSearchIngestionOptions options;

    /// <summary>Initializes a new instance of the <see cref="IngestionSchemaProvider"/> class.</summary>
    /// <param name="detected">The core schema provider, which detects fields from content types.</param>
    /// <param name="strategies">Resolves an index's strategy class.</param>
    /// <param name="options">Ingestion configuration.</param>
    public IngestionSchemaProvider(
        IIndexSchemaProvider detected,
        IIndexStrategySource strategies,
        IOptions<XpSearchIngestionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(options);

        this.detected = detected;
        this.strategies = strategies;
        this.options = options.Value;
    }

    /// <summary>Reads the fields declared on a strategy class with <see cref="XpSearchFieldAttribute"/>.</summary>
    /// <param name="strategy">The strategy class, or <see langword="null"/> when the index has none.</param>
    /// <returns>The declared fields and whether the class allows undeclared attributes.</returns>
    public static (IReadOnlyList<SchemaField> Fields, bool? AllowDynamicFields) Declared(Type? strategy)
    {
        if (strategy is null)
        {
            return ([], null);
        }

        var fields = strategy
            .GetCustomAttributes(typeof(XpSearchFieldAttribute), inherit: false)
            .Cast<XpSearchFieldAttribute>()
            .Select(attribute => attribute.ToSchemaField())
            .ToList();

        bool? allowDynamic = strategy
            .GetCustomAttributes(typeof(XpSearchSchemaAttribute), inherit: false)
            .Cast<XpSearchSchemaAttribute>()
            .FirstOrDefault()?.AllowDynamicFields;

        return (fields, allowDynamic);
    }

    /// <inheritdoc />
    public async Task<IngestionSchema> GetSchemaAsync(string indexName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);

        var index = options.Indexes.TryGetValue(indexName, out var configured) ? configured : null;
        var (declared, declaredAllowDynamic) = Declared(strategies.GetStrategyType(indexName));
        var detectedSchema = await detected.GetSchemaAsync(indexName, cancellationToken).ConfigureAwait(false);

        // IndexSchema keeps the first definition of a name, so the highest-precedence source is listed
        // first: configuration, then the strategy's attributes, then the detected content type fields.
        var fields = new List<SchemaField>(index?.Fields ?? []);
        fields.AddRange(declared);
        fields.AddRange(detectedSchema.Fields);

        return new IngestionSchema(
            new IndexSchema(indexName, fields),
            index?.AllowDynamicFields ?? declaredAllowDynamic ?? false);
    }
}
