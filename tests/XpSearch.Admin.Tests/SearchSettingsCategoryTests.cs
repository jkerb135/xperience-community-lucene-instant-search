using System.Reflection;

using Kentico.Xperience.Admin.Base.FormAnnotations;

using NUnit.Framework;

using XpSearch.Admin.UIPages;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The Search settings page groups its fields with <c>FormCategory</c>, and a category owns the
/// fields ordered after it until the next category. Nothing but the numbers says so, and the numbers
/// are easy to get wrong while adding a field, so this pins which bucket every field lands in.
/// </summary>
[TestFixture]
internal sealed class SearchSettingsCategoryTests
{
    /// <summary>The bucket each editable field belongs in, by its label.</summary>
    private static readonly (string Category, string Label)[] Expected =
    [
        ("Search", "Response cache lifetime (seconds)"),
        ("Search", "Maximum query length"),
        ("Search", "Maximum page size"),
        ("Search", "Maximum values per facet"),
        ("Search", "Maximum result window"),
        ("Suggestions", "Maximum suggestion count"),
        ("Suggestions", "Query suggestion window (days)"),
        ("Analytics retention", "Retention: remove search analytics older than X days"),
        ("Analytics retention", "Cleanup batch size (rows per delete)"),
        ("Popularity boosts", "Popularity lookback (days)"),
        ("Popularity boosts", "Popularity documents per index"),
        ("Popularity boosts", "Popularity suggestion queries"),
        ("Synonym suggestions", "Synonym reformulation window (seconds)"),
        ("Synonym suggestions", "Synonym minimum occurrences"),
    ];

    [Test]
    public void EveryFieldFallsIntoItsCategory()
    {
        var categories = typeof(SearchSettingsModel)
            .GetCustomAttributes<FormCategoryAttribute>()
            .OrderBy(category => category.Order)
            .ToList();

        var fields = typeof(SearchSettingsModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.GetCustomAttribute<FormComponentAttribute>())
            .Where(component => component is not null)
            .Select(component => component!)
            .OrderBy(component => component.Order)
            .ToList();

        // A field before the first category is uncategorised - only the index name may be.
        string? CategoryOf(int order) =>
            categories.LastOrDefault(category => category.Order < order)?.Label;

        Expect.Multiple(() =>
        {
            Assert.That(
                categories.Select(category => category.Label),
                Is.EqualTo(new[] { "Search", "Suggestions", "Analytics retention", "Popularity boosts", "Synonym suggestions" }),
                "the five buckets, in the order the page shows them");

            Assert.That(CategoryOf(fields[0].Order), Is.Null, "the index name stands above the first category");
            Assert.That(fields[0].Label, Is.EqualTo("Index"));

            Assert.That(
                fields.Skip(1).Select(field => (CategoryOf(field.Order), field.Label)),
                Is.EqualTo(Expected));

            Assert.That(
                categories.All(category => !category.Collapsible),
                "fourteen fields are short enough to show at once");
        });
    }
}
