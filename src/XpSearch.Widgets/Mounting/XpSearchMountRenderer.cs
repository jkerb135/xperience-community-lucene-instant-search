using System.Text.Json;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// Default <see cref="IXpSearchMountRenderer"/>: one empty <c>div</c> carrying HTML-encoded JSON.
/// </summary>
public sealed class XpSearchMountRenderer : IXpSearchMountRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <inheritdoc />
    public IHtmlContent Render(XpSearchMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        // TagBuilder HTML-encodes attribute values on write, so quotes and angle brackets inside the
        // JSON cannot break out of the attribute.
        var tag = new TagBuilder("div");
        tag.AddCssClass(XpSearchWidgetConstants.MountCssClass);
        tag.Attributes["data-xps-widget"] = mount.WidgetType;
        tag.Attributes["data-xps-instance"] = mount.InstanceId;
        tag.Attributes["data-xps-config"] = JsonSerializer.Serialize(mount.Config, JsonOptions);

        if (mount.InstanceConfig.Count > 0)
        {
            tag.Attributes["data-xps-instance-config"] = JsonSerializer.Serialize(mount.InstanceConfig, JsonOptions);
        }

        if (mount.Labels is { Count: > 0 })
        {
            tag.Attributes["data-xps-labels"] = JsonSerializer.Serialize(mount.Labels, JsonOptions);
        }

        if (mount.Content is not null)
        {
            tag.InnerHtml.SetHtmlContent(mount.Content);
        }

        return tag;
    }
}
