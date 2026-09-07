using Microsoft.AspNetCore.Http;

using XpSearch.Core.Rendering;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>
/// The search the results widget already ran, shared with the widgets of the same instance that
/// render from it - pagination, result stats, active filters (SK-1 §2.4). Nobody runs a second one.
/// </summary>
/// <remarks>
/// Tag helpers are transient, so the render lives on <see cref="HttpContext.Items"/> for the rest of
/// the request. It is therefore document-order dependent: a widget rendered before the results sees
/// nothing and paints its skeleton (docs/internal/KNOWN-LIMITATIONS.md).
/// </remarks>
internal static class ServerFirstPaint
{
    private const string Key = "XpSearch.FirstPaint:";

    /// <summary>Publishes what the results widget of <paramref name="instanceId"/> painted.</summary>
    internal static void Publish(HttpContext? httpContext, string instanceId, ServerResultsRender render)
    {
        if (httpContext is not null)
        {
            httpContext.Items[Key + instanceId] = render;
        }
    }

    /// <summary>What the results widget of <paramref name="instanceId"/> painted, if it has already.</summary>
    internal static ServerResultsRender? Find(HttpContext? httpContext, string instanceId) =>
        httpContext?.Items.TryGetValue(Key + instanceId, out object? render) == true
            ? render as ServerResultsRender
            : null;
}
