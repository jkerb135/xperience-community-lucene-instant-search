using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>
/// Options of any widget registered with <c>registerWidgetType()</c>, for an author who does not want
/// a tag helper of their own (RZ-1 §1.4).
/// </summary>
public sealed record WidgetOptions : XpSearchMountOptions
{
    /// <summary>Gets the <c>registerWidgetType()</c> identifier, e.g. <c>myCompany.dropdownFacet</c>.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the widget's options. An anonymous object or POCO is serialized property by property with
    /// camel-cased names; a dictionary is copied key for key.
    /// </summary>
    public object? Config { get; init; }

    /// <summary>Gets the instance options to add beyond <c>index</c>, read the same way as <see cref="Config"/>.</summary>
    public object? InstanceConfig { get; init; }
}

/// <summary><c>&lt;xps-widget type="myCompany.dropdownFacet" /&gt;</c> - the generic mount.</summary>
[HtmlTargetElement("xps-widget")]
public sealed class XpSearchWidgetTagHelper : XpSearchMountTagHelper<WidgetOptions>
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchWidgetTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public XpSearchWidgetTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="WidgetOptions.Type"/>.</summary>
    [HtmlAttributeName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets <see cref="WidgetOptions.Config"/>.</summary>
    [HtmlAttributeName("config")]
    public object? Config { get; set; }

    /// <summary>Gets or sets <see cref="WidgetOptions.InstanceConfig"/>.</summary>
    [HtmlAttributeName("instance-config")]
    public object? InstanceConfig { get; set; }

    /// <inheritdoc />
    /// <remarks>Never used: the <c>type</c> attribute names the widget, through <see cref="GetWidgetType"/>.</remarks>
    protected override string WidgetType => "widget";

    /// <inheritdoc />
    public override string GetWidgetType(WidgetOptions options) => options?.Type?.Trim() ?? WidgetType;

    /// <inheritdoc />
    public override string? Validate(WidgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (string.IsNullOrWhiteSpace(options.Type) ? WidgetResources.Hint_WidgetType : null);
    }

    /// <inheritdoc />
    protected override WidgetOptions Merge(WidgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Type = Type ?? options.Type,
            Config = Config ?? options.Config,
            InstanceConfig = InstanceConfig ?? options.InstanceConfig
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(WidgetOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Config is not null)
        {
            ReflectConfig(options.Config, config);
        }
    }

    /// <inheritdoc />
    protected override void BuildInstanceConfig(WidgetOptions options, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.InstanceConfig is not null)
        {
            ReflectConfig(options.InstanceConfig, instanceConfig);
        }
    }
}
