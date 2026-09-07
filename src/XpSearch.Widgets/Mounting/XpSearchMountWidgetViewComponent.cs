using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using XpSearch.Widgets.Resources;
using XpSearch.Widgets.TagHelpers;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// Base class for a Page Builder widget that scaffolds on a widget's tag helper (RZ-1 §2). It keeps
/// the editor concerns only - the property mapping, the static preview and the unconfigured
/// instruction block - and hands everything else to <see cref="XpSearchMountTagHelper{TOptions}"/>,
/// so "options in, mount out" lives in exactly one place per widget.
/// </summary>
/// <typeparam name="TProperties">The widget's editor-facing properties class.</typeparam>
/// <typeparam name="TOptions">The widget's options record, shared with the tag helper.</typeparam>
/// <remarks>
/// View-component widget pattern per
/// https://docs.kentico.com/documentation/developers-and-admins/development/builders/page-builder/widgets-for-page-builder.
/// </remarks>
public abstract class XpSearchMountWidgetViewComponent<TProperties, TOptions> : ViewComponent
    where TProperties : XpSearchMountWidgetProperties, new()
    where TOptions : XpSearchMountOptions, new()
{
    private readonly IXpSearchEditorContext editorContext;

    /// <summary>Initializes a new instance of the <see cref="XpSearchMountWidgetViewComponent{TProperties, TOptions}"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper - the single implementation of its mount.</param>
    /// <param name="editorContext">Tells live-site rendering from Page Builder and preview rendering.</param>
    protected XpSearchMountWidgetViewComponent(
        XpSearchMountTagHelper<TOptions> tagHelper,
        IXpSearchEditorContext editorContext)
    {
        ArgumentNullException.ThrowIfNull(tagHelper);
        ArgumentNullException.ThrowIfNull(editorContext);

        TagHelper = tagHelper;
        this.editorContext = editorContext;
    }

    /// <summary>Gets the widget's tag helper, which owns index resolution, validation and the mount.</summary>
    protected XpSearchMountTagHelper<TOptions> TagHelper { get; }

    /// <summary>Maps what the editor configured onto the widget's options.</summary>
    /// <param name="properties">The configured properties.</param>
    /// <returns>The options.</returns>
    public abstract TOptions ToOptions(TProperties properties);

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
    /// Builds what the mount view renders: the mount element, the Page Builder preview, the editor's
    /// instruction block, or nothing. Public so widget output can be asserted without an Xperience
    /// application.
    /// </summary>
    /// <param name="properties">The configured properties.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The view model.</returns>
    public async Task<XpSearchMountViewModel> BuildModelAsync(TProperties properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var options = ToOptions(properties);
        // Also resolves the index the preview and the config mapping need.
        string? hint = TagHelper.Validate(options);

        if (hint is not null)
        {
            return Unconfigured(hint);
        }

        if (editorContext.GetMode() is XpSearchEditorMode.Edit or XpSearchEditorMode.ReadOnly)
        {
            return new XpSearchMountViewModel { Preview = Preview(properties, options) };
        }

        // The tag helper renders the first paint through it (spec §5.8).
        TagHelper.ViewContext = ViewComponentContext.ViewContext!;

        return new XpSearchMountViewModel
        {
            Mount = await TagHelper.RenderAsync(options, cancellationToken).ConfigureAwait(false)
        };
    }

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

    private IHtmlContent Preview(TProperties properties, TOptions options)
    {
        string widgetType = TagHelper.GetWidgetType(options);

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
