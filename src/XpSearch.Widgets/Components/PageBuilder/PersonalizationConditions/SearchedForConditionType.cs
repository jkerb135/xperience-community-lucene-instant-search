using CMS.Core;

using Kentico.PageBuilder.Web.Mvc.Personalization;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Core.Personalization;
using XpSearch.Widgets.Components.PageBuilder.PersonalizationConditions;

[assembly: RegisterPersonalizationConditionType(
    identifier: XpSearchConditionTypes.SearchedForIdentifier,
    type: typeof(SearchedForConditionType),
    name: "Search - searched for",
    Description = "Evaluates whether the current visitor searched for a term recently.",
    IconClass = "icon-magnifier",
    Hint = "The variant is shown to visitors whose search text contained the term. Visitors without contact tracking consent, and crawlers, see the original.")]

namespace XpSearch.Widgets.Components.PageBuilder.PersonalizationConditions;

/// <summary>Identifiers of the personalization condition types this package registers (PS-1).</summary>
public static class XpSearchConditionTypes
{
    /// <summary>Identifier of the "searched for" condition type.</summary>
    public const string SearchedForIdentifier = "XperienceCommunity.Search.SearchedFor";

    /// <summary>Identifier of the "search A/B bucket" condition type.</summary>
    public const string BucketIdentifier = "XperienceCommunity.Search.Bucket";
}

/// <summary>
/// <em>The visitor searched for {term} in the last {days} days</em>, over the search activities
/// this library logs (spec §9.1).
/// </summary>
/// <remarks>
/// A condition type is constructed by the Page Builder from its serialized properties, so its
/// services are resolved from the container rather than injected
/// (https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/content-personalization/develop-personalization-condition-types).
/// All of the logic is in <see cref="SearchedFor"/>.
/// </remarks>
public class SearchedForConditionType : ConditionType
{
    /// <summary>Gets or sets the searched text the visitor's query must contain.</summary>
    [TextInputComponent(
        Label = "Searched term",
        ExplanationText = "The variant applies when the visitor's search text contains this term, ignoring case. An empty term matches nobody.",
        Order = 0)]
    public string Term { get; set; } = string.Empty;

    /// <summary>Gets or sets how many days back the visitor's searches are considered.</summary>
    [NumberInputComponent(
        Label = "Within the last (days)",
        ExplanationText = "Only searches newer than this count. Only the visitor's 100 most recent searches are considered.",
        Order = 1)]
    public int Days { get; set; } = SearchedFor.DefaultDays;

    /// <inheritdoc />
    public override bool Evaluate() =>
        SearchedFor.Matches(
            Service.Resolve<IRecentSearchProvider>().GetRecentSearches(),
            Term,
            Days,
            DateTime.Now);
}
