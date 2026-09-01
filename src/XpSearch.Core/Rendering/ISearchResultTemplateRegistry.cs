using System.Reflection;

namespace XpSearch.Core.Rendering;

/// <summary>
/// The result templates registered with <see cref="RegisterSearchResultTemplateAttribute"/>.
/// </summary>
public interface ISearchResultTemplateRegistry
{
    /// <summary>Gets every registered template, ordered by name.</summary>
    /// <returns>The templates.</returns>
    IReadOnlyList<SearchResultTemplate> GetTemplates();

    /// <summary>Looks a template up by identifier.</summary>
    /// <param name="identifier">The template identifier.</param>
    /// <returns>The template, or <see langword="null"/> when nothing is registered under it.</returns>
    SearchResultTemplate? Find(string identifier);
}

/// <summary>
/// <see cref="ISearchResultTemplateRegistry"/> that reads the assembly attributes of the loaded
/// assemblies once, on first use.
/// </summary>
public sealed class SearchResultTemplateRegistry : ISearchResultTemplateRegistry
{
    private readonly Lazy<IReadOnlyDictionary<string, SearchResultTemplate>> templates;

    /// <summary>Initializes a new instance of the <see cref="SearchResultTemplateRegistry"/> class.</summary>
    public SearchResultTemplateRegistry()
        : this(() => AppDomain.CurrentDomain.GetAssemblies())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SearchResultTemplateRegistry"/> class over an explicit assembly set.</summary>
    /// <param name="assemblies">Supplies the assemblies to scan.</param>
    public SearchResultTemplateRegistry(Func<IEnumerable<Assembly>> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        templates = new Lazy<IReadOnlyDictionary<string, SearchResultTemplate>>(() => Discover(assemblies()));
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchResultTemplate> GetTemplates() =>
        templates.Value.Values.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <inheritdoc />
    public SearchResultTemplate? Find(string identifier) =>
        identifier is not null && templates.Value.TryGetValue(identifier, out var template) ? template : null;

    private static IReadOnlyDictionary<string, SearchResultTemplate> Discover(IEnumerable<Assembly> assemblies)
    {
        var found = new Dictionary<string, SearchResultTemplate>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            IEnumerable<RegisterSearchResultTemplateAttribute> attributes;
            try
            {
                attributes = assembly.GetCustomAttributes<RegisterSearchResultTemplateAttribute>();
            }
            catch (FileNotFoundException)
            {
                // An assembly whose dependencies are not on disk cannot carry our attribute either.
                continue;
            }

            foreach (var attribute in attributes)
            {
                found.TryAdd(
                    attribute.Identifier,
                    new SearchResultTemplate(attribute.Identifier, attribute.Name, attribute.ViewName, attribute.ContentTypes));
            }
        }

        return found;
    }
}
