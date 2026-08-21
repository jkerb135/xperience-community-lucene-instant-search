namespace XpSearch.Core.Indexing;

/// <summary>
/// Converts the app-relative URLs Xperience URL retrievers return into the form the contract allows
/// on the wire.
/// </summary>
public static class WebUrl
{
    /// <summary>
    /// Turns <c>~/products/x</c> into <c>/products/x</c> and leaves root-relative and absolute URLs
    /// alone. <c>IWebPageUrlRetriever.Retrieve(...).RelativePath</c> returns the <c>~/</c> form, which
    /// a browser cannot follow, so it must never reach a hit.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>A root-relative or absolute URL; an empty string stays empty.</returns>
    public static string ToRootRelative(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        string trimmed = url.Trim();

        if (trimmed.StartsWith("~/", StringComparison.Ordinal))
        {
            return trimmed[1..];
        }

        return trimmed == "~" ? "/" : trimmed;
    }
}
