using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>
/// The single implementation of "options in, mount element out" (RZ-1 §1.2). A subclass names the
/// JavaScript widget, binds the tag's attributes into its options record and, where the mapping is
/// not simply the camel-cased property name, overrides <see cref="BuildConfig"/>,
/// <see cref="BuildInstanceConfig"/>, <see cref="GetWidgetType"/>, <see cref="BuildContentAsync"/> or
/// <see cref="Validate"/>.
/// </summary>
/// <typeparam name="TOptions">The widget's options record.</typeparam>
/// <remarks>
/// Everything above this class - the Page Builder widgets, <c>Html.XpSearchAsync</c> - goes through
/// <see cref="BuildAsync"/>, so a widget renders the same bytes however it was placed.
/// </remarks>
public abstract class XpSearchMountTagHelper<TOptions> : TagHelper
    where TOptions : XpSearchMountOptions, new()
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ConfigProperties = new();

    private readonly IXpSearchMountRenderer renderer;
    private readonly IXpSearchIndexCatalog indexCatalog;

    private XpSearchScope? scope;

    /// <summary>Initializes a new instance of the <see cref="XpSearchMountTagHelper{TOptions}"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">Supplies the sole index when none was named.</param>
    protected XpSearchMountTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(indexCatalog);

        this.renderer = renderer;
        this.indexCatalog = indexCatalog;
    }

    /// <summary>Gets or sets the whole options record, as <c>options="@model"</c>. Individual attributes win over it.</summary>
    [HtmlAttributeName("options")]
    public TOptions? Options { get; set; }

    /// <summary>Gets or sets the code name of the index to search. Empty uses the enclosing <c>&lt;xps-search&gt;</c> or the project's only index.</summary>
    [HtmlAttributeName("index")]
    public string? Index { get; set; }

    /// <summary>Gets or sets the search instance this widget joins. Empty uses the enclosing <c>&lt;xps-search&gt;</c> or <c>default</c>.</summary>
    [HtmlAttributeName("instance")]
    public string? InstanceId { get; set; }

    /// <summary>
    /// Gets or sets the current view context. Bound by Razor for a tag element; the Page Builder base
    /// class and <c>Html.XpSearchAsync</c> set it before they call <see cref="BuildAsync"/>, because
    /// <see cref="BuildContentAsync"/> renders through it.
    /// </summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// Gets the index the last <see cref="Validate"/> or <see cref="BuildAsync"/> call resolved: the
    /// named index, the enclosing scope's, or the project's only one. Empty when none could be found.
    /// </summary>
    public string CurrentIndex { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the value of <c>data-xps-widget</c> - a first-party name such as <c>facetList</c>, or a
    /// dotted third-party identifier registered with <c>registerWidgetType()</c>.
    /// </summary>
    protected abstract string WidgetType { get; }

    /// <summary>
    /// Gets or sets what the values the visitor arrived filtering by are called, rendered as
    /// <c>data-xps-labels</c> on the mount (FC-1). Set it from <see cref="BuildContentAsync"/>, which
    /// runs before the mount is built.
    /// </summary>
    protected IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? MountLabels { get; set; }

    /// <summary>
    /// Gets the value of <c>data-xps-widget</c> for a specific configuration. Override when the
    /// JavaScript widget depends on an option; the default returns <see cref="WidgetType"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The widget type identifier.</returns>
    public virtual string GetWidgetType(TOptions options) => WidgetType;

    /// <summary>
    /// Returns what is still missing before the widget can render, or <see langword="null"/> when it
    /// is ready. The base implementation resolves the index into <see cref="CurrentIndex"/>; an
    /// override chains through it, so the index is resolved before the widget's own checks run.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The message, or <see langword="null"/>.</returns>
    public virtual string? Validate(TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CurrentIndex = ResolveIndex(options);

        return CurrentIndex.Length == 0 ? WidgetResources.Hint_SelectIndex : null;
    }

    /// <summary>
    /// Builds the mount. On a Razor page this is what <c>ProcessAsync</c> renders; the Page Builder
    /// widgets and <c>Html.XpSearchAsync</c> call it directly, which is what makes the three surfaces
    /// byte-identical.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The mount.</returns>
    /// <exception cref="InvalidOperationException">The options are not renderable; the message is <see cref="Validate"/>'s.</exception>
    public async Task<XpSearchMount> BuildAsync(TOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A Razor developer wants the failure at render time, not an empty div (RZ-1 §1.2).
        string? message = Validate(options);
        if (message is not null)
        {
            throw new InvalidOperationException(message);
        }

        MountLabels = null;
        var content = await BuildContentAsync(options, cancellationToken).ConfigureAwait(false);

        var mount = new XpSearchMount(GetWidgetType(options), ResolveInstanceId(options))
        {
            Content = content,
            Labels = MountLabels
        };

        BuildConfig(options, mount.Config);
        mount.InstanceConfig["index"] = CurrentIndex;
        BuildInstanceConfig(options, mount.InstanceConfig);
        scope?.ApplyTo(mount.InstanceConfig);

        return mount;
    }

    /// <summary>Builds the mount and renders it.</summary>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The mount element.</returns>
    public async Task<IHtmlContent> RenderAsync(TOptions options, CancellationToken cancellationToken = default) =>
        renderer.Render(await BuildAsync(options, cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        scope = XpSearchScope.From(context);
        var options = Merge(Options ?? new TOptions());

        output.TagName = null;
        output.Content.SetHtmlContent(
            await RenderAsync(options, ViewContext?.HttpContext?.RequestAborted ?? CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>
    /// Returns the options the tag's own attributes describe, over the record <c>options="@model"</c>
    /// supplied: <c>options with { Limit = Limit ?? options.Limit, … }</c>. Index and instance are
    /// handled by the base class.
    /// </summary>
    /// <param name="options">The record the tag was given, or a fresh one.</param>
    /// <returns>The effective options.</returns>
    protected virtual TOptions Merge(TOptions options) => options;

    /// <summary>
    /// Fills <c>data-xps-config</c>. The default serializes every public readable option except
    /// <c>Index</c> and <c>InstanceId</c> under its camel-cased name, skipping nulls and empty strings
    /// so an unset option leaves the JavaScript default in place.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="config">The config object to fill.</param>
    protected virtual void BuildConfig(TOptions options, IDictionary<string, object?> config) =>
        ReflectConfig(options, config);

    /// <summary>
    /// Adds instance options to <c>data-xps-instance-config</c> beyond <c>index</c>, which the base
    /// class always writes. Only override where an option really is instance-wide.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="instanceConfig">The instance config object to fill.</param>
    protected virtual void BuildInstanceConfig(TOptions options, IDictionary<string, object?> instanceConfig)
    {
    }

    /// <summary>
    /// Builds markup rendered inside the mount element, or <see langword="null"/> - the default - for
    /// nothing. The results widget uses this for the server-rendered first paint (spec §5.8); the
    /// JavaScript widget replaces it on its first render, so it must never be the only way the widget
    /// works.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The markup, or <see langword="null"/>.</returns>
    protected virtual Task<IHtmlContent?> BuildContentAsync(TOptions options, CancellationToken cancellationToken) =>
        Task.FromResult<IHtmlContent?>(null);

    /// <summary>
    /// Fills <paramref name="config"/> from an object's public properties, camel-cased, skipping
    /// nulls and empty strings; see <see cref="BuildConfig"/>. A dictionary is copied key for key.
    /// </summary>
    /// <param name="source">The object to read, typically the options record.</param>
    /// <param name="config">The config object to fill.</param>
    protected static void ReflectConfig(object source, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(config);

        if (source is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                config[entry.Key.ToString()!] = entry.Value;
            }

            return;
        }

        foreach (var property in ConfigProperties.GetOrAdd(source.GetType(), Discover))
        {
            object? value = property.GetValue(source);
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
                && property.Name is not (nameof(XpSearchMountOptions.Index) or nameof(XpSearchMountOptions.InstanceId)))
            .ToArray();

    private static string? FirstSet(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private string ResolveIndex(TOptions options)
    {
        // The tag's own attribute, then the record, then the enclosing <xps-search>.
        string? named = FirstSet(Index, options.Index, scope?.Index);
        if (named is not null)
        {
            return named;
        }

        // A project with exactly one index should not force every widget to name it.
        var names = indexCatalog.GetIndexNames();

        return names.Count == 1 ? names[0] : string.Empty;
    }

    private string ResolveInstanceId(TOptions options) =>
        FirstSet(InstanceId, options.InstanceId, scope?.Instance) ?? XpSearchWidgetConstants.DefaultInstanceId;
}
