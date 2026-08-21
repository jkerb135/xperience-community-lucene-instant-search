namespace XpSearch.Core.Abstractions;

/// <summary>
/// Thrown when a request names an index that is not registered in the Xperience Search application.
/// The endpoints translate it to a 404 Problem Details response.
/// </summary>
public sealed class IndexNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IndexNotFoundException"/> class.</summary>
    /// <param name="indexName">Code name of the index that could not be resolved.</param>
    public IndexNotFoundException(string indexName)
        : base($"Search index '{indexName}' is not registered.") => IndexName = indexName;

    /// <summary>Gets the code name of the index that could not be resolved.</summary>
    public string IndexName { get; }
}

/// <summary>
/// Thrown when a request is syntactically valid JSON but semantically invalid (an unknown attribute,
/// a malformed filter, an out-of-range page size). The endpoints translate it to a 400
/// <c>ValidationProblemDetails</c> response keyed by the offending JSON field.
/// </summary>
public sealed class SearchValidationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SearchValidationException"/> class.</summary>
    /// <param name="field">JSON field name the error belongs to, for example <c>filters.numeric[0].attribute</c>.</param>
    /// <param name="message">Human-readable description of the problem. Must not leak internals.</param>
    public SearchValidationException(string field, string message)
        : base(message) => Errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] };

    /// <summary>Initializes a new instance of the <see cref="SearchValidationException"/> class.</summary>
    /// <param name="errors">Messages keyed by JSON field name.</param>
    public SearchValidationException(IDictionary<string, string[]> errors)
        : base("The search request is not valid.") => Errors = new Dictionary<string, string[]>(errors, StringComparer.Ordinal);

    /// <summary>Gets the validation messages, keyed by the JSON field they belong to.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
