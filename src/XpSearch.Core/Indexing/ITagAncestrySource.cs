namespace XpSearch.Core.Indexing;

/// <summary>One ancestor of a taxonomy tag.</summary>
/// <param name="Name">The ancestor's code name, which is what a facet filter refers to.</param>
/// <param name="Title">The ancestor's title, which is what a widget displays.</param>
public readonly record struct TagAncestor(string Name, string Title);

/// <summary>
/// Resolves a taxonomy tag's ancestry, which the indexing strategy writes onto every document so
/// facet counts roll up and a drill-down on a parent matches its descendants (ADR-0018).
/// </summary>
/// <remarks>
/// A seam rather than a direct call, because resolving ancestry needs the whole tag table:
/// <c>ITaxonomyRetriever.RetrieveTags</c> returns <c>Tag.ParentID</c> but not the parent itself, and
/// nothing maps a tag identifier to its taxonomy, so there is no way to ask the retriever for the
/// tags above one tag. See <see cref="TagAncestrySource"/> for the default implementation.
/// </remarks>
public interface ITagAncestrySource
{
    /// <summary>Gets the ancestors of one tag, root first, excluding the tag itself.</summary>
    /// <param name="tagIdentifier">The tag GUID, as carried by a <c>TagReference</c>.</param>
    /// <returns>The ancestors, or an empty list for a root-level or unknown tag.</returns>
    IReadOnlyList<TagAncestor> AncestorsOf(Guid tagIdentifier);
}
