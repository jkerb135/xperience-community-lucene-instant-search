using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

using Kentico.PageBuilder.Web.Mvc;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// Base class for a Page Builder widget that renders a single <c>.xps-mount</c> element (spec §5.7,
/// §7.1). It serializes the properties into <c>data-xps-config</c>, emits the instance grouping and
/// instance options, renders the editor-only instruction block when the widget is not configured and
/// the static preview when the page is open in the Page Builder - so a widget author only declares
/// the JavaScript widget type and, if needed, the property mapping.
/// </summary>
/// <typeparam name="TProperties">The widget's properties class.</typeparam>
/// <remarks>
/// View-component widget pattern per
/// https://docs.kentico.com/documentation/developers-and-admins/development/builders/page-builder/widgets-for-page-builder.
/// </remarks>
public abstract class XpSearchMountWidgetViewComponent<TProperties> : ViewComponent
    where TProperties : XpSearchMountWidgetProperties, new()
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ConfigProperties = new();

    private readonly IXpSearchMountRenderer renderer;
    private readonly IXpSearchEditorContext editorContext;
    private readonly IXpSearchIndexCatalog indexCatalog;

    /// <summary>Initializes a new instance of the <see cref="XpSearchMountWidgetViewComponent{TProperties}"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">Tells live-site rendering from Page Builder and preview rendering.</param>
    /// <param name="indexCatalog">Supplies the sole index when the editor left the index empty.</param>
    protected XpSearchMountWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(indexCatalog);

        this.renderer = renderer;
        this.editorContext = editorContext;
        this.indexCatalog = indexCatalog;
    }

    /// <summary>
    /// Gets the value of <c>data-xps-widget</c> - a first-party name such as <c>facetList</c>, or a
    /// dotted third-party identifier registered with <c>registerWidgetType()</c>.
    /// </summary>
    protected abstract string WidgetType { get; }

    /// <summary>
    /// Gets the index the widget will search - the editor's choice, or the project's only index when
    /// the editor left the field empty. Valid inside <see cref="ConfigurationHint"/>,
    /// <see cref="BuildConfig"/> and <see cref="BuildInstanceConfig"/>.
    /// </summary>
    protected string CurrentIndex { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the values the visitor arrived filtering by are called, rendered as
    /// <c>data-xps-labels</c> on the mount (FC-1). Set it from
    /// <see cref="BuildMountContentAsync"/>, which runs before the model is rebuilt with the content.
    /// </summary>
    protected IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? MountLabels { get; set; }

    /// <summary>Renders the widget.</summary>
    /// <param name="widget">The Page Builder component model.</param>
    /// <returns>The rendered mount view.</returns>
    public async Task<IViewComponentResult> InvokeAsync(ComponentViewModel<TProperties> widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        var model = await BuildModelAsync(
                widget.Properties,
                ViewComponentContext.ViewContext?.HttpContext?.RequestAborted ?? CancellationToken.None)
            .ConfigureAwait(false);

        return View(XpSearchWidgetConstants.MountViewPath, model);
    }

    /// <summary>
    /// Builds what the mount view renders, including any server-rendered content the widget puts
    /// inside its mount element (see <see cref="BuildMountContentAsync"/>).
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The view model.</returns>
    public async Task<XpSearchMountViewModel> BuildModelAsync(TProperties properties, CancellationToken cancellationToken)
    {
        var model = BuildModel(properties);

        if (model.Mount is null)
        {
            // No mount element means the Page Builder preview or an unconfigured widget; neither
            // renders search results server-side.
            return model;
        }

        var content = await BuildMountContentAsync(properties, cancellationToken).ConfigureAwait(false);

        // Building the model is pure and cheap, so the widget that has content re-runs it rather than
        // every widget threading a mount object through the synchronous API.
        return content is null ? model : BuildModel(properties, content);
    }

    /// <summary>
    /// Builds what the mount view renders. Public so widget output can be asserted without an
    /// Xperience application.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The view model.</returns>
    public XpSearchMountViewModel BuildModel(TProperties properties) => BuildModel(properties, content: null);

    private XpSearchMountViewModel BuildModel(TProperties properties, IHtmlContent? content)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string index = ResolveIndex(properties.Index);
        CurrentIndex = index;
        string? hint = string.IsNullOrEmpty(index) ? WidgetResources.Hint_SelectIndex : ConfigurationHint(properties);

        if (hint is not null)
        {
            return Unconfigured(hint);
        }

        if (editorContext.GetMode() is XpSearchEditorMode.Edit or XpSearchEditorMode.ReadOnly)
        {
            return new XpSearchMountViewModel { Preview = Preview(properties) };
        }

        var mount = new XpSearchMount(GetWidgetType(properties), ResolveInstanceId(properties.InstanceId))
        {
            Content = content,
            Labels = MountLabels
        };
        BuildConfig(properties, mount.Config);
        mount.InstanceConfig["index"] = index;
        BuildInstanceConfig(properties, mount.InstanceConfig);

        return new XpSearchMountViewModel { Mount = renderer.Render(mount) };
    }

    /// <summary>
    /// Gets the value of <c>data-xps-widget</c> for a specific configuration. Override when the
    /// JavaScript widget depends on a property; the default returns <see cref="WidgetType"/>.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The widget type identifier.</returns>
    protected virtual string GetWidgetType(TProperties properties) => WidgetType;

    /// <summary>
    /// Fills <c>data-xps-config</c>. The default serializes every public readable property except
    /// <c>Index</c>, <c>InstanceId</c> and those marked <see cref="Newtonsoft.Json.JsonIgnoreAttribute"/>,
    /// under its camel-cased name, skipping nulls and empty strings so a blank editor field leaves the
    /// JavaScript default in place.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="config">The config object to fill.</param>
    protected virtual void BuildConfig(TProperties properties, IDictionary<string, object?> config) =>
        ReflectConfig(properties, config);

    /// <summary>
    /// Adds instance options to <c>data-xps-instance-config</c> beyond <c>index</c>, which the base
    /// class always writes. Only override where an option really is instance-wide.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="instanceConfig">The instance config object to fill.</param>
    protected virtual void BuildInstanceConfig(TProperties properties, IDictionary<string, object?> instanceConfig)
    {
    }

    /// <summary>
    /// Builds markup rendered inside the mount element on a live or previewed page, or
    /// <see langword="null"/> - the default - for nothing. The results widget uses this to render the
    /// visitor's first page of results server-side (spec §5.8); the JavaScript widget replaces it on
    /// its first render, so it must never be the only way the widget works.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The markup, or <see langword="null"/>.</returns>
    protected virtual Task<IHtmlContent?> BuildMountContentAsync(TProperties properties, CancellationToken cancellationToken) =>
        Task.FromResult<IHtmlContent?>(null);

    /// <summary>
    /// Builds the body of the static preview an editor sees in the Page Builder instead of the mount
    /// element. Mirror the widget's live markup with disabled controls and placeholder bars, and add
    /// an <c>xps-editor-preview__note</c> paragraph for configuration the markup cannot show; the
    /// base class supplies the preview root and its badge. The default is that note alone, so a
    /// widget that does not override this still shows an editor something labelled.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The preview body.</returns>
    protected virtual IHtmlContent BuildEditorPreview(TProperties properties) =>
        EditorPreview.Note(WidgetResources.Preview_Note_Generic);

    /// <summary>
    /// Returns the instruction to show an editor when the widget still needs configuring beyond the
    /// index, or <see langword="null"/> when the widget is ready to render.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The hint, or <see langword="null"/>.</returns>
    protected virtual string? ConfigurationHint(TProperties properties) => null;

    /// <summary>Fills <paramref name="config"/> by reflection; see <see cref="BuildConfig"/>.</summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="config">The config object to fill.</param>
    protected static void ReflectConfig(TProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        foreach (var property in ConfigProperties.GetOrAdd(properties.GetType(), Discover))
        {
            object? value = property.GetValue(properties);
            if (value is null || (value is string text && text.Length == 0))
            {
                continue;
            }

            config[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = value;
        }
    }

    private static PropertyInfo[] Discover(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.Name is not (nameof(XpSearchMountWidgetProperties.Index) or nameof(XpSearchMountWidgetProperties.InstanceId))
                && property.GetCustomAttribute<Newtonsoft.Json.JsonIgnoreAttribute>() is null)
            .ToArray();

    private static string ResolveInstanceId(string? instanceId) =>
        string.IsNullOrWhiteSpace(instanceId) ? XpSearchWidgetConstants.DefaultInstanceId : instanceId.Trim();

    private string ResolveIndex(string? index)
    {
        if (!string.IsNullOrWhiteSpace(index))
        {
            return index.Trim();
        }

        // A project with exactly one index should not force every editor to pick it.
        var names = indexCatalog.GetIndexNames();

        return names.Count == 1 ? names[0] : string.Empty;
    }

    private IHtmlContent Preview(TProperties properties)
    {
        string widgetType = GetWidgetType(properties);

        return EditorPreview
            .El("div", $"xps xps-editor-preview xps-editor-preview--{EditorPreview.Kebab(widgetType)}")
            .Attr("data-xps-widget", widgetType)
            .Add(
                EditorPreview.El(
                    "span",
                    "xps-editor-preview__badge",
                    string.Format(CultureInfo.CurrentUICulture, WidgetResources.Preview_Badge, widgetType)),
                // The mirrored markup is a picture of the widget: only the badge is worth announcing.
                EditorPreview.El("div", "xps-editor-preview__body").Decorative().Add(BuildEditorPreview(properties)));
    }

    private XpSearchMountViewModel Unconfigured(string hint)
    {
        string? message = editorContext.GetMode() switch
        {
            XpSearchEditorMode.Edit => WidgetResources.Unconfigured_Edit,
            XpSearchEditorMode.ReadOnly => WidgetResources.Unconfigured_ReadOnly,
            XpSearchEditorMode.Preview => WidgetResources.Unconfigured_Preview,
            _ => null
        };

        return message is null
            ? new XpSearchMountViewModel()
            : new XpSearchMountViewModel
            {
                EditorTitle = WidgetResources.Unconfigured_Title,
                EditorMessage = $"{message} {hint}"
            };
    }
}
