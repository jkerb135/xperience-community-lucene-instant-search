using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

using Kentico.PageBuilder.Web.Mvc;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// Base class for a Page Builder widget that renders a single <c>.xps-mount</c> element (spec §5.7,
/// §7.1). It serializes the properties into <c>data-xps-config</c>, emits the instance grouping and
/// instance options, and renders the editor-only instruction block when the widget is not configured
/// - so a widget author only declares the JavaScript widget type and, if needed, the property mapping.
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

    /// <summary>Renders the widget.</summary>
    /// <param name="widget">The Page Builder component model.</param>
    /// <returns>The rendered mount view.</returns>
    public Task<IViewComponentResult> InvokeAsync(ComponentViewModel<TProperties> widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        return Task.FromResult<IViewComponentResult>(
            View(XpSearchWidgetConstants.MountViewPath, BuildModel(widget.Properties)));
    }

    /// <summary>
    /// Builds what the mount view renders. Public so widget output can be asserted without an
    /// Xperience application.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The view model.</returns>
    public XpSearchMountViewModel BuildModel(TProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string index = ResolveIndex(properties.Index);
        CurrentIndex = index;
        string? hint = string.IsNullOrEmpty(index) ? WidgetResources.Hint_SelectIndex : ConfigurationHint(properties);

        if (hint is not null)
        {
            return Unconfigured(hint);
        }

        var mount = new XpSearchMount(GetWidgetType(properties), ResolveInstanceId(properties.InstanceId));
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
