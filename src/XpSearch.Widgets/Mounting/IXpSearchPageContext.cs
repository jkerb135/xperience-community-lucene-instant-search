namespace XpSearch.Widgets.Mounting;

/// <summary>
/// What the page a widget sits on decides about its search: the language it is being viewed in and
/// the website channel it belongs to (LC-1), plus whether the index it points at agrees.
/// </summary>
/// <remarks>
/// A seam over <c>IPreferredLanguageRetriever</c> and <c>IWebsiteChannelContext</c> - see
/// https://docs.kentico.com/documentation/developers-and-admins/development/content-retrieval/retrieve-page-content
/// - and over the index definition the Search application stores, so widget output is testable
/// without an Xperience application. Both retrievers only answer inside a website channel request,
/// so every member is written to say "I do not know" rather than to fail.
/// </remarks>
public interface IXpSearchPageContext
{
    /// <summary>Gets the code name of the language the current page is being viewed in.</summary>
    /// <returns>The language code name, or <see langword="null"/> outside a website channel request.</returns>
    string? GetLanguage();

    /// <summary>Gets the code name of the website channel the current page belongs to.</summary>
    /// <returns>The channel code name, or <see langword="null"/> outside a website channel request.</returns>
    string? GetChannel();

    /// <summary>Gets whether an index covers the current website channel.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the index's definition lists the current channel, and also when
    /// there is no channel to compare against or the definition cannot be read - an unknown answer
    /// must not hide an index from the editor.
    /// </returns>
    Task<bool> CoversCurrentChannelAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>
    /// Warns once per index and value when the index does not cover the language or channel a widget
    /// on this page is about to search, which would silently return nothing. Never throws.
    /// </summary>
    /// <param name="indexName">Code name of the index the widget searches.</param>
    /// <param name="language">The resolved language, or <see langword="null"/> for every language.</param>
    /// <param name="channel">The resolved channel, or <see langword="null"/> for every channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the check is done.</returns>
    Task WarnIfNotCoveredAsync(string indexName, string? language, string? channel, CancellationToken cancellationToken);
}
