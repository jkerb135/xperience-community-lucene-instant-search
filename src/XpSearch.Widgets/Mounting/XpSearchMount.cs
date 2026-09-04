using Microsoft.AspNetCore.Html;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// The data of one <c>.xps-mount</c> element (spec §7.1): which JavaScript widget to build, which
/// search instance it joins, and the two JSON blobs the bootstrap reads.
/// </summary>
public sealed class XpSearchMount
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchMount"/> class.</summary>
    /// <param name="widgetType">Value of <c>data-xps-widget</c>, e.g. <c>facetList</c> or <c>myCompany.dropdownFacet</c>.</param>
    /// <param name="instanceId">Value of <c>data-xps-instance</c>.</param>
    public XpSearchMount(string widgetType, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        WidgetType = widgetType;
        InstanceId = instanceId;
    }

    /// <summary>Gets the value of <c>data-xps-widget</c>.</summary>
    public string WidgetType { get; }

    /// <summary>Gets the value of <c>data-xps-instance</c>.</summary>
    public string InstanceId { get; }

    /// <summary>Gets the widget options serialized into <c>data-xps-config</c>.</summary>
    public IDictionary<string, object?> Config { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the instance options serialized into <c>data-xps-instance-config</c>. The attribute is
    /// omitted when this is empty. The bootstrap uses the first mount of an instance that carries an
    /// <c>index</c> here, so every mount of one instance must name the same index.
    /// </summary>
    public IDictionary<string, object?> InstanceConfig { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets what the values the visitor arrived filtering by are called,
    /// <c>attribute -&gt; value -&gt; label</c>, serialized into <c>data-xps-labels</c> (FC-1). The
    /// bootstrap seeds the client's label memory from it, so the first paint of a filtered URL never
    /// shows a stored code. The attribute is omitted when this is empty.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Labels { get; set; }

    /// <summary>
    /// Gets or sets markup rendered inside the mount element - the server-rendered first paint of the
    /// results widget (spec §5.8). The JavaScript widget replaces the mount's contents on its first
    /// render, so whatever is here is progressive enhancement only.
    /// </summary>
    public IHtmlContent? Content { get; set; }
}
