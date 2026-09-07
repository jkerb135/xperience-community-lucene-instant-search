using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>categoryTree</c> widget - a taxonomy attribute as a drill-down tree.</summary>
public sealed record CategoryTreeOptions : XpSearchMountOptions
{
    /// <summary>Gets the index attribute the tree navigates.</summary>
    public string? Attribute { get; init; }

    /// <summary>Gets the heading shown above the tree.</summary>
    public string? Label { get; init; }

    /// <summary>Gets how many nodes are listed at each level of the tree.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Gets whether the tree's title folds it away.</summary>
    public bool Collapsible { get; init; } = true;
}

/// <summary><c>&lt;xps-category-tree /&gt;</c> - mounts the <c>categoryTree</c> widget.</summary>
[HtmlTargetElement("xps-category-tree")]
public sealed class CategoryTreeTagHelper : XpSearchMountTagHelper<CategoryTreeOptions>
{
    /// <summary>Initializes a new instance of the <see cref="CategoryTreeTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public CategoryTreeTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="CategoryTreeOptions.Attribute"/>.</summary>
    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    /// <summary>Gets or sets <see cref="CategoryTreeOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="CategoryTreeOptions.Limit"/>.</summary>
    [HtmlAttributeName("limit")]
    public int? Limit { get; set; }

    /// <summary>Gets or sets <see cref="CategoryTreeOptions.Collapsible"/>.</summary>
    [HtmlAttributeName("collapsible")]
    public bool? Collapsible { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "categoryTree";

    /// <inheritdoc />
    public override string? Validate(CategoryTreeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (string.IsNullOrWhiteSpace(options.Attribute) ? WidgetResources.Hint_SelectAttribute : null);
    }

    /// <inheritdoc />
    protected override CategoryTreeOptions Merge(CategoryTreeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Attribute = Attribute ?? options.Attribute,
            Label = Label ?? options.Label,
            Limit = Limit ?? options.Limit,
            Collapsible = Collapsible ?? options.Collapsible
        };
    }
}
