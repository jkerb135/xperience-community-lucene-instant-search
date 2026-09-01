namespace XpSearch.Core.Rendering;

/// <summary>
/// A result template a developer registered for editors to choose in the Results widget (spec §5.8).
/// </summary>
/// <param name="Identifier">Unique identifier; this is what ends up in <c>data-xps-config</c> as <c>template</c>.</param>
/// <param name="Name">The name editors see in the drop-down.</param>
/// <param name="ViewName">Path of the partial view that renders one result.</param>
/// <param name="ContentTypes">Content types the template applies to; empty means all of them.</param>
public sealed record SearchResultTemplate(
    string Identifier,
    string Name,
    string ViewName,
    IReadOnlyList<string> ContentTypes);
