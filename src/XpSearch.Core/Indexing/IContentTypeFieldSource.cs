using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Reads the field metadata of a content type. The seam that keeps
/// <c>DataClassInfoProvider</c> and <c>FormInfo</c> - neither of which stands up outside a running
/// Xperience application - out of the schema logic, so auto-detection is testable against a
/// hand-written class form definition.
/// </summary>
public interface IContentTypeFieldSource
{
    /// <summary>Gets the searchable fields of a content type.</summary>
    /// <param name="contentTypeName">Class name of the content type, for example <c>DancingGoat.ArticlePage</c>.</param>
    /// <returns>The fields, or an empty list when the content type is unknown.</returns>
    IReadOnlyList<SchemaField> GetFields(string contentTypeName);
}

/// <summary>
/// Lists the content types an index covers.
/// </summary>
public interface IIndexContentTypeSource
{
    /// <summary>Gets the class names of every content type the index is configured to hold.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The class names, web page and reusable types together, without duplicates.</returns>
    Task<IReadOnlyList<string>> GetContentTypesAsync(string indexName, CancellationToken cancellationToken);
}
