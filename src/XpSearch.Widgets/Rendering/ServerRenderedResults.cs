using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Widgets.Templates;

namespace XpSearch.Widgets.Rendering;

/// <summary>What the Results widget wants server-rendered (spec §5.8).</summary>
/// <param name="Index">The index to search.</param>
/// <param name="ResultsPerPage">Page size; zero keeps the index's own.</param>
/// <param name="Fields">Attributes to retrieve; empty keeps the index defaults.</param>
/// <param name="TemplateIdentifier">The registered result template the editor picked, if any.</param>
/// <param name="TitleAttribute">Attribute the default card's title comes from; empty means <c>title</c>.</param>
/// <param name="UrlAttribute">Attribute the default card links to; empty means <c>url</c>.</param>
/// <param name="SnippetAttributes">Attributes the default card tries for its snippet, in order.</param>
public sealed record ServerResultsOptions(
    string Index,
    int ResultsPerPage,
    IReadOnlyList<string> Fields,
    string? TemplateIdentifier,
    string? TitleAttribute,
    string? UrlAttribute,
    IReadOnlyList<string> SnippetAttributes);

/// <summary>
/// Runs the visitor's initial search and renders the result cards on the server, so a shared result
/// URL paints before the client bundle runs and a visitor without JavaScript still sees results
/// (spec §5.8). The markup goes inside the mount element and the client replaces it on its first
/// render.
/// </summary>
public sealed class ServerRenderedResults
{
    private const string ListOpen = "<div data-xps-server-rendered class=\"xps xps-results\"><ol class=\"xps-results__list\">";
    private const string ListClose = "</ol></div>";
    private const string ItemOpen = "<li class=\"xps-results__item\">";
    private const string ItemClose = "</li>";
    private const string Empty = "<div data-xps-server-rendered class=\"xps xps-results xps-results--empty\">"
        + "<div class=\"xps-results__empty\"><p>No results.</p></div></div>";

    private readonly ISearchPipeline pipeline;
    private readonly ICompositeViewEngine viewEngine;
    private readonly ISearchResultTemplateRegistry templates;
    private readonly ILogger<ServerRenderedResults> logger;
    private readonly IIndexSchemaProvider? schemas;

    /// <summary>Initializes a new instance of the <see cref="ServerRenderedResults"/> class.</summary>
    /// <param name="pipeline">The query pipeline the public endpoint uses; the same rules, personalization and journaling apply.</param>
    /// <param name="viewEngine">Resolves the result partial.</param>
    /// <param name="templates">The registered result templates.</param>
    /// <param name="logger">Records a failed search or an unresolvable template.</param>
    /// <param name="schemas">Tells which query-string parameters are attributes of the index; without it every parameter is read as a filter.</param>
    public ServerRenderedResults(
        ISearchPipeline pipeline,
        ICompositeViewEngine viewEngine,
        ISearchResultTemplateRegistry templates,
        ILogger<ServerRenderedResults> logger,
        IIndexSchemaProvider? schemas = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(viewEngine);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(logger);

        this.pipeline = pipeline;
        this.viewEngine = viewEngine;
        this.templates = templates;
        this.logger = logger;
        this.schemas = schemas;
    }

    /// <summary>
    /// Searches with the state in the request's query string and renders the cards, or returns
    /// <see langword="null"/> when the search could not be run - a broken search leaves an empty
    /// mount for the client to fill, it never breaks the page.
    /// </summary>
    /// <param name="viewContext">The widget's view context; supplies the request and the view engine's search paths.</param>
    /// <param name="options">What to search and how to render it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The markup, or <see langword="null"/>.</returns>
    public async Task<IHtmlContent?> RenderAsync(
        ViewContext viewContext,
        ServerResultsOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewContext);
        ArgumentNullException.ThrowIfNull(options);

        SearchResponse response;
        try
        {
            var request = await BuildRequestAsync(viewContext, options, cancellationToken).ConfigureAwait(false);
            response = await pipeline.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The initial search of index '{Index}' could not be rendered on the server.", options.Index);

            return null;
        }

        if (response.Results is not { Length: > 0 })
        {
            return new HtmlString(Empty);
        }

        var template = ResolveTemplate(options.TemplateIdentifier);
        var content = new HtmlContentBuilder().AppendHtml(ListOpen);
        // Resolved once per render, so a template pointing at a missing view logs one warning for the
        // page rather than one per result.
        var views = new Dictionary<string, IView?>(StringComparer.Ordinal);
        IView? Resolve(string viewName) =>
            views.TryGetValue(viewName, out var found) ? found : views[viewName] = FindView(viewContext, viewName);

        foreach (var result in response.Results)
        {
            var model = new SearchResultViewModel(result, options.TitleAttribute, options.UrlAttribute, options.SnippetAttributes);
            // A template scoped to content types only renders the results of those types; the rest,
            // and every result when no template applies, get the built-in card.
            var view = (template is not null && Applies(template, model.ContentType) ? Resolve(template.ViewName) : null)
                ?? Resolve(XpSearchWidgetConstants.DefaultResultViewPath);

            if (view is null)
            {
                continue;
            }

            content
                .AppendHtml(ItemOpen)
                .AppendHtml(await RenderViewAsync(viewContext, view, model).ConfigureAwait(false))
                .AppendHtml(ItemClose);
        }

        return content.AppendHtml(ListClose);
    }

    private static bool Applies(SearchResultTemplate template, string? contentType) =>
        template.ContentTypes is not { Count: > 0 }
        || (contentType is not null && template.ContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase));

    private SearchResultTemplate? ResolveTemplate(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var template = templates.Find(identifier.Trim());

        if (template is null)
        {
            logger.LogWarning(
                "Result template '{Template}' is not registered; the default template was used instead.",
                identifier);
        }

        return template;
    }

    private async Task<SearchRequest> BuildRequestAsync(
        ViewContext viewContext,
        ServerResultsOptions options,
        CancellationToken cancellationToken)
    {
        var request = new SearchRequest { Index = options.Index };

        if (options.ResultsPerPage > 0)
        {
            request.PageSize = options.ResultsPerPage;
        }

        if (options.Fields.Count > 0)
        {
            request.Fields = [.. options.Fields];
        }

        SearchQueryState.Apply(
            request,
            viewContext.HttpContext.Request.Query,
            await ResolveSchemaAsync(options.Index, cancellationToken).ConfigureAwait(false));

        return request;
    }

    /// <summary>
    /// The schema of the index, or <see langword="null"/> when it cannot be resolved - an
    /// unresolvable schema falls back to reading every parameter as a filter, which the pipeline
    /// then rejects into an empty mount rather than a broken page.
    /// </summary>
    private async Task<IndexSchema?> ResolveSchemaAsync(string index, CancellationToken cancellationToken)
    {
        if (schemas is null)
        {
            return null;
        }

        try
        {
            return await schemas.GetSchemaAsync(index, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The schema of index '{Index}' could not be read; every query-string parameter is read as a filter.", index);

            return null;
        }
    }

    private static async Task<IHtmlContent> RenderViewAsync(ViewContext viewContext, IView view, SearchResultViewModel model)
    {
        var metadata = viewContext.HttpContext.RequestServices.GetRequiredService<IModelMetadataProvider>();
        using var writer = new StringWriter();

        var viewData = new ViewDataDictionary<SearchResultViewModel>(metadata, viewContext.ModelState) { Model = model };

        await view.RenderAsync(new ViewContext(viewContext, view, viewData, writer)).ConfigureAwait(false);

        return new HtmlString(writer.ToString());
    }

    private IView? FindView(ViewContext viewContext, string viewName)
    {
        var result = viewName.StartsWith('~') || viewName.StartsWith('/')
            ? viewEngine.GetView(executingFilePath: null, viewName, isMainPage: false)
            : viewEngine.FindView(viewContext, viewName, isMainPage: false);

        if (result.Success)
        {
            return result.View;
        }

        logger.LogWarning(
            "Result template view '{ViewName}' was not found; searched {Locations}.",
            viewName,
            string.Join(", ", result.SearchedLocations ?? []));

        return null;
    }
}
