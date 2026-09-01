using CMS.Core;

using Kentico.PageBuilder.Web.Mvc.Personalization;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Core.Experiments;
using XpSearch.Core.Personalization;
using XpSearch.Widgets.Components.PageBuilder.PersonalizationConditions;

[assembly: RegisterPersonalizationConditionType(
    identifier: XpSearchConditionTypes.BucketIdentifier,
    type: typeof(SearchBucketConditionType),
    name: "Search - A/B bucket",
    Description = "Splits visitors into two sticky buckets, so a widget variant becomes one half of an A/B test.",
    IconClass = "icon-arrows-crooked",
    Hint = "Visitors stay in the same bucket on every visit. Give two widgets the same split name to make their variants flip together.")]

namespace XpSearch.Widgets.Components.PageBuilder.PersonalizationConditions;

/// <summary>
/// <em>The visitor is in bucket A or B of a named percentage split</em>, bucketed by the same
/// first-party cookie the search experiments use (PS-1, reusing XP-1's <c>xpsearch_bucket</c>).
/// </summary>
/// <remarks>
/// Deliberately dumb: no experiment entity, no report. Two widgets given the same split name bucket
/// a visitor identically, which turns their variants into one page-level A/B test, measured in
/// Analytics. A visitor whose bucket cookie cannot be read or written - below the Essential cookie
/// level, or a response that has already started - evaluates false and sees the original variant.
/// All of the logic is in <see cref="SearchBucket"/>.
/// </remarks>
public class SearchBucketConditionType : ConditionType
{
    /// <summary>Gets or sets the bucket this variant is shown to.</summary>
    [DropDownComponent(
        Label = "Bucket",
        Options = $"{SearchBucket.BucketA};Bucket A\r\n{SearchBucket.BucketB};Bucket B",
        Order = 0)]
    public string Bucket { get; set; } = SearchBucket.BucketB;

    /// <summary>Gets or sets the percentage of visitors in bucket B.</summary>
    [NumberInputComponent(
        Label = "Percentage in bucket B",
        ExplanationText = "1 to 99. The remaining visitors are in bucket A.",
        Order = 1)]
    public int SplitPercent { get; set; } = SearchBucket.DefaultSplitPercent;

    /// <summary>Gets or sets the name of the split.</summary>
    [TextInputComponent(
        Label = "Split name",
        ExplanationText = "Conditions with the same split name bucket a visitor the same way, so variants on different widgets flip together. Change the name to re-shuffle visitors.",
        Order = 2)]
    public string GroupName { get; set; } = SearchBucket.DefaultGroupName;

    /// <inheritdoc />
    public override bool Evaluate() =>
        SearchBucket.IsInBucket(Service.Resolve<IVisitorBucketProvider>().GetBucketId(), Bucket, GroupName, SplitPercent);
}
