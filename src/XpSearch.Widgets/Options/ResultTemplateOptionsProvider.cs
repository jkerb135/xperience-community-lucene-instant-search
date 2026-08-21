using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets.Templates;

namespace XpSearch.Widgets.Options;

/// <summary>
/// Fills the Results widget's "Result template" drop-down from
/// <see cref="ISearchResultTemplateRegistry"/> (spec §5.8).
/// </summary>
public sealed class ResultTemplateOptionsProvider : IDropDownOptionsProvider
{
    private readonly ISearchResultTemplateRegistry registry;

    /// <summary>Initializes a new instance of the <see cref="ResultTemplateOptionsProvider"/> class.</summary>
    /// <param name="registry">The registered templates.</param>
    public ResultTemplateOptionsProvider(ISearchResultTemplateRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
    }

    /// <inheritdoc />
    public Task<IEnumerable<DropDownOptionItem>> GetOptionItems() =>
        Task.FromResult(registry.GetTemplates()
            .Select(template => new DropDownOptionItem { Value = template.Identifier, Text = template.Name }));
}
