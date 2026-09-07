namespace XpSearch.Widgets.Mounting;

/// <summary>
/// What every widget needs whichever way it is placed: the index it searches and the search instance
/// it joins. One sealed record per JavaScript widget derives from it, and that record - not the Page
/// Builder properties class - is the surface a Razor developer and a unit test see (RZ-1 §1.1).
/// </summary>
/// <remarks>
/// Deliberately free of Kentico and Page Builder types: the same record is what
/// <c>&lt;xps-search-box … /&gt;</c> binds its attributes into, what <c>Html.XpSearchAsync</c> takes
/// and what a Page Builder widget's <c>ToOptions</c> produces.
/// </remarks>
public abstract record XpSearchMountOptions
{
    /// <summary>
    /// Gets the code name of the index to search. <see langword="null"/> or empty falls back to the
    /// enclosing <c>&lt;xps-search&gt;</c> and then to the project's only index.
    /// </summary>
    public string? Index { get; init; }

    /// <summary>
    /// Gets the identifier that couples this widget to the other widgets of the same search.
    /// <see langword="null"/> or empty falls back to the enclosing <c>&lt;xps-search&gt;</c> and then
    /// to <c>default</c>.
    /// </summary>
    public string? InstanceId { get; init; }
}
