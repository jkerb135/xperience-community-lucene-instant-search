using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Helpers;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Resolves tag ancestry from the tag table, cached whole.
/// </summary>
/// <remarks>
/// <para>
/// Ancestry is a property of the taxonomy, not of the document, so the whole table is read once and
/// walked in memory: an index rebuild resolves ancestors for every tag of every document, and one
/// query per document would dominate it. The entry is held in Xperience's own data cache with a
/// dependency on <c>cms.tag|all</c>, so editing, moving or deleting a tag drops it - see
/// https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies.
/// </para>
/// <para>
/// The title is <c>TagTitle</c>, the tag's title in the default language.
/// <c>TagInfo.TagMetadata</c> carries the translations, keyed by content-language GUID, but reading
/// them means deserializing the metadata of every tag and resolving the language, which the
/// per-document write cannot afford. See <c>docs/internal/KNOWN-LIMITATIONS.md</c>.
/// </para>
/// </remarks>
public sealed class TagAncestrySource : ITagAncestrySource
{
    private const double CacheMinutes = 60;

    private readonly IInfoProvider<TagInfo> tags;
    private readonly IProgressiveCache cache;

    /// <summary>Initializes a new instance of the <see cref="TagAncestrySource"/> class.</summary>
    /// <param name="tags">The tag provider.</param>
    /// <param name="cache">Xperience's progressive cache.</param>
    public TagAncestrySource(IInfoProvider<TagInfo> tags, IProgressiveCache cache)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(cache);

        this.tags = tags;
        this.cache = cache;
    }

    /// <inheritdoc />
    public IReadOnlyList<TagAncestor> AncestorsOf(Guid tagIdentifier)
    {
        var map = Load();

        if (!map.ByIdentifier.TryGetValue(tagIdentifier, out var tag))
        {
            return [];
        }

        var path = new List<TagAncestor>();
        var seen = new HashSet<int>();
        int parentId = tag.ParentId;

        // A parent chain is data, so it is walked defensively: an unknown or repeated identifier
        // ends the walk instead of looping.
        while (parentId != 0 && seen.Add(parentId) && map.ById.TryGetValue(parentId, out var parent))
        {
            path.Add(new TagAncestor(parent.Name, parent.Title));
            parentId = parent.ParentId;
        }

        path.Reverse();

        return path;
    }

    private TagMap Load() => cache.Load(
        settings =>
        {
            settings.CacheDependency = CacheHelper.GetCacheDependency($"{TagInfo.OBJECT_TYPE}|all");

            var rows = tags
                .Get()
                .Columns(
                    nameof(TagInfo.TagID),
                    nameof(TagInfo.TagParentID),
                    nameof(TagInfo.TagGUID),
                    nameof(TagInfo.TagName),
                    nameof(TagInfo.TagTitle))
                .Select(tag => new TagRow(
                    tag.TagID,
                    tag.TagParentID,
                    tag.TagGUID,
                    tag.TagName,
                    string.IsNullOrEmpty(tag.TagTitle) ? tag.TagName : tag.TagTitle))
                .ToList();

            return new TagMap(
                rows.GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First()),
                rows.GroupBy(row => row.Identifier).ToDictionary(group => group.Key, group => group.First()));
        },
        new CacheSettings(CacheMinutes, "xpsearch", "tag-ancestry"));

    private sealed record TagRow(int Id, int ParentId, Guid Identifier, string Name, string Title);

    private sealed record TagMap(
        IReadOnlyDictionary<int, TagRow> ById,
        IReadOnlyDictionary<Guid, TagRow> ByIdentifier);
}
