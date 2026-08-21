namespace XpSearch.Ingestion.Abstractions;

/// <summary>
/// Thrown when a request names a document the index does not hold. The endpoints translate it to a
/// 404 Problem Details response.
/// </summary>
public sealed class DocumentNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DocumentNotFoundException"/> class.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="id">Identifier of the document that could not be found.</param>
    public DocumentNotFoundException(string indexName, string id)
        : base($"Index '{indexName}' holds no external document with id '{id}'.")
    {
        IndexName = indexName;
        Id = id;
    }

    /// <summary>Gets the code name of the index.</summary>
    public string IndexName { get; }

    /// <summary>Gets the identifier of the document that could not be found.</summary>
    public string Id { get; }
}

/// <summary>
/// Thrown when an ingestion request is well-formed JSON but not a valid request. The endpoints
/// translate it to a 400 <c>ValidationProblemDetails</c> response keyed by the offending field.
/// </summary>
public sealed class IngestionValidationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IngestionValidationException"/> class.</summary>
    /// <param name="field">JSON field or query parameter the error belongs to.</param>
    /// <param name="message">Human-readable description of the problem.</param>
    public IngestionValidationException(string field, string message)
        : base(message) => Errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] };

    /// <summary>Gets the validation messages, keyed by the field they belong to.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// Thrown when a request exceeds the batch limits of spec §10.2. The endpoints translate it to a 413
/// response with the limit spelled out, rather than truncating the batch.
/// </summary>
public sealed class IngestionTooLargeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IngestionTooLargeException"/> class.</summary>
    /// <param name="message">Which limit was exceeded, and what it is.</param>
    public IngestionTooLargeException(string message)
        : base(message)
    {
    }
}
