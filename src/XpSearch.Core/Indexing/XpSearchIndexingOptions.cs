using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// The per-field escape hatch for auto-detection (spec §4.5f): exclude a field, rename it, or change
/// its flags and boost.
/// </summary>
/// <example>
/// <code>
/// services.AddXpSearch(options =&gt; { }, indexing =&gt; indexing
///     .Exclude("DancingGoat.ArticlePage", "ArticlePageSummary")
///     .Configure("DancingGoat.ArticlePage", "ArticleTitle", field =&gt; field with { Boost = 3f }));
/// </code>
/// </example>
public sealed class XpSearchIndexingOptions
{
    private readonly List<Func<string, SchemaField, SchemaField?>> overrides = [];

    /// <summary>Drops a field from the schema, so it is neither indexed nor returned.</summary>
    /// <param name="contentTypeName">Class name of the content type the field belongs to.</param>
    /// <param name="fieldName">Name of the field to drop.</param>
    /// <returns>The same instance, for chaining.</returns>
    public XpSearchIndexingOptions Exclude(string contentTypeName, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);

        return Configure(contentTypeName, fieldName, _ => null);
    }

    /// <summary>Rewrites one detected field.</summary>
    /// <param name="contentTypeName">Class name of the content type the field belongs to.</param>
    /// <param name="fieldName">Name of the field to rewrite.</param>
    /// <param name="configure">Receives the detected field and returns the replacement, or <see langword="null"/> to drop it.</param>
    /// <returns>The same instance, for chaining.</returns>
    public XpSearchIndexingOptions Configure(string contentTypeName, string fieldName, Func<SchemaField, SchemaField?> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentTypeName);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(configure);

        overrides.Add((type, field) =>
            string.Equals(type, contentTypeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)
                ? configure(field)
                : field);

        return this;
    }

    /// <summary>Applies every registered override to a detected field.</summary>
    /// <param name="contentTypeName">Class name the field was detected on.</param>
    /// <param name="field">The detected field.</param>
    /// <returns>The field after the overrides, or <see langword="null"/> when one dropped it.</returns>
    public SchemaField? Apply(string contentTypeName, SchemaField field)
    {
        SchemaField? current = field;

        foreach (var @override in overrides)
        {
            if (current is null)
            {
                return null;
            }

            current = @override(contentTypeName, current);
        }

        return current;
    }
}
