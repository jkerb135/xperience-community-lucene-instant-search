namespace XpSearch.Widgets.Templates;

/// <summary>
/// Registers a result template so editors can pick it in the Results widget's "Result template"
/// drop-down (spec §5.8).
/// </summary>
/// <remarks>
/// <para>
/// <c>[assembly: RegisterSearchResultTemplate("MyCompany.ProductCard", "Product card",
/// "~/Components/Search/_ProductCard.cshtml", contentTypes: ["MyCompany.Product"])]</c>
/// </para>
/// <para>
/// Registration and selection only: the chosen identifier is written into <c>data-xps-config</c> as
/// <c>template</c>. Applying the view server-side on the initial page load is not implemented yet.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RegisterSearchResultTemplateAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="RegisterSearchResultTemplateAttribute"/> class.</summary>
    /// <param name="identifier">Unique identifier of the template. Use a company prefix.</param>
    /// <param name="name">The name editors see.</param>
    /// <param name="viewName">Path of the partial view rendering one result.</param>
    /// <param name="contentTypes">Content types the template applies to; omit for all of them.</param>
    public RegisterSearchResultTemplateAttribute(string identifier, string name, string viewName, params string[] contentTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        Identifier = identifier;
        Name = name;
        ViewName = viewName;
        ContentTypes = contentTypes ?? [];
    }

    /// <summary>Gets the unique identifier of the template.</summary>
    public string Identifier { get; }

    /// <summary>Gets the name editors see.</summary>
    public string Name { get; }

    /// <summary>Gets the path of the partial view rendering one result.</summary>
    public string ViewName { get; }

    /// <summary>Gets the content types the template applies to; empty means all of them.</summary>
    public IReadOnlyList<string> ContentTypes { get; }
}
