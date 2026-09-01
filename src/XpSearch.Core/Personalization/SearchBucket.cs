using XpSearch.Core.Experiments;

namespace XpSearch.Core.Personalization;

/// <summary>
/// Decides whether a visitor is in bucket A or B of a named percentage split, the whole logic of
/// the "search A/B bucket" personalization condition (PS-1). Pure, so it is tested without a cookie.
/// </summary>
/// <remarks>
/// The visitor is bucketed by XP-1's <c>xpsearch_bucket</c> cookie hashed with the split name, so
/// two conditions carrying the same name bucket a visitor identically - that is what pairs variants
/// across widgets into one page-level A/B test.
/// </remarks>
public static class SearchBucket
{
    /// <summary>The bucket holding the visitors outside the split percentage.</summary>
    public const string BucketA = "A";

    /// <summary>The bucket holding the visitors inside the split percentage.</summary>
    public const string BucketB = "B";

    /// <summary>The split name used when the editor does not choose one.</summary>
    public const string DefaultGroupName = "default";

    /// <summary>Default percentage of visitors in bucket B.</summary>
    public const int DefaultSplitPercent = 50;

    /// <summary>Whether the visitor is in the configured bucket.</summary>
    /// <param name="bucketId">The visitor's bucket id, or <see langword="null"/> when they have none.</param>
    /// <param name="bucket">The configured bucket, <c>A</c> or <c>B</c>.</param>
    /// <param name="groupName">Name of the split; blank falls back to <see cref="DefaultGroupName"/>.</param>
    /// <param name="splitPercent">Percentage of visitors in bucket B, clamped to 1-99.</param>
    /// <returns>
    /// <see langword="true"/> when the visitor is in the configured bucket; <see langword="false"/>
    /// for a visitor with no bucket id, who therefore has no sticky bucket at all.
    /// </returns>
    public static bool IsInBucket(string? bucketId, string? bucket, string? groupName, int splitPercent)
    {
        if (string.IsNullOrEmpty(bucketId))
        {
            return false;
        }

        var variant = ExperimentBucketing.Variant(
            bucketId,
            string.IsNullOrWhiteSpace(groupName) ? DefaultGroupName : groupName.Trim(),
            Math.Clamp(splitPercent, 1, 99));

        return string.Equals(
            variant == SearchVariant.B ? BucketB : BucketA,
            bucket?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
