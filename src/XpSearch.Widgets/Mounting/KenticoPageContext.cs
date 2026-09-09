using System.Collections.Concurrent;

using CMS.Websites.Routing;

using Kentico.Content.Web.Mvc.Routing;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// <see cref="IXpSearchPageContext"/> over the Xperience request context and the stored index
/// definition.
/// </summary>
/// <remarks>
/// <c>IPreferredLanguageRetriever.Get()</c> answers the language of the request, falling back to the
/// channel's primary language, and <c>IWebsiteChannelContext.WebsiteChannelName</c> the channel's code
/// name; both resolve only inside a website channel request
/// (https://docs.kentico.com/documentation/developers-and-admins/development/content-retrieval/retrieve-page-content).
/// A Razor page outside one - or a test host - gets no defaults rather than an exception, and the
/// search then covers everything the index does.
/// </remarks>
internal sealed class KenticoPageContext : IXpSearchPageContext
{
    // Once per process, per index and value: a warning is a development-time hint, not a per-render log.
    private static readonly ConcurrentDictionary<string, byte> Warned = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPreferredLanguageRetriever languages;
    private readonly IWebsiteChannelContext channels;
    private readonly ILuceneIndexAccessor accessor;
    private readonly ILogger<KenticoPageContext> logger;

    public KenticoPageContext(
        IPreferredLanguageRetriever languages,
        IWebsiteChannelContext channels,
        ILuceneIndexAccessor accessor,
        ILogger<KenticoPageContext> logger)
    {
        this.languages = languages;
        this.channels = channels;
        this.accessor = accessor;
        this.logger = logger;
    }

    public string? GetLanguage() => Safely(() => languages.Get(), "language");

    public string? GetChannel() => Safely(() => channels.WebsiteChannelName, "website channel");

    public async Task<bool> CoversCurrentChannelAsync(string indexName, CancellationToken cancellationToken)
    {
        string? channel = GetChannel();

        if (string.IsNullOrEmpty(indexName) || channel is null)
        {
            return true;
        }

        var definition = await DefinitionAsync(indexName, cancellationToken).ConfigureAwait(false);

        return definition is null || Covers(definition.WebsiteChannelNames, channel);
    }

    public async Task WarnIfNotCoveredAsync(
        string indexName,
        string? language,
        string? channel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(indexName) || (language is null && channel is null))
        {
            return;
        }

        var definition = await DefinitionAsync(indexName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return;
        }

        Warn(indexName, "language", language, definition.LanguageNames);
        Warn(indexName, "website channel", channel, definition.WebsiteChannelNames);
    }

    private void Warn(string indexName, string what, string? value, IReadOnlyList<string> covered)
    {
        if (value is null || Covers(covered, value) || !Warned.TryAdd($"{indexName}|{what}|{value}", 0))
        {
            return;
        }

        logger.LogWarning(
            "Index '{Index}' does not cover the {What} '{Value}' the page searches in, so the widgets on it will find nothing. "
            + "Point them at an index that covers it, or widen the search with the tag attribute.",
            indexName,
            what,
            value);
    }

    private async Task<IndexDefinition?> DefinitionAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            return await accessor.GetDefinitionAsync(indexName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "The definition of index {Index} could not be read.", indexName);

            return null;
        }
    }

    // An index that lists nothing covers everything: the definition simply says nothing about it.
    private static bool Covers(IReadOnlyList<string> covered, string value) =>
        covered.Count == 0 || covered.Contains(value, StringComparer.OrdinalIgnoreCase);

    private string? Safely(Func<string?> read, string what)
    {
        try
        {
            return read() is { Length: > 0 } value ? value : null;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The current {What} could not be resolved.", what);

            return null;
        }
    }
}
