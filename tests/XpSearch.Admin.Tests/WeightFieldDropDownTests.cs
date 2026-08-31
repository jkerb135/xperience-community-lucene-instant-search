using System.Reflection;

using Kentico.Xperience.Admin.Base.FormAnnotations;

using NUnit.Framework;

using XpSearch.Admin.Forms;
using XpSearch.Admin.UIPages;
using XpSearch.Core;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The Field input of a field weight is a drop-down of the index's searchable fields (AD-7), not a
/// free-text box.
/// </summary>
[TestFixture]
internal sealed class WeightFieldDropDownTests
{
    private static PropertyInfo FieldNameProperty =>
        typeof(FieldWeightModel).GetProperty(nameof(FieldWeightModel.FieldName))!;

    [Test]
    public void The_field_input_is_a_drop_down_fed_by_the_weight_field_configurator()
    {
        var dropDown = FieldNameProperty.GetCustomAttribute<DropDownComponentAttribute>();
        var configuration = FieldNameProperty.GetCustomAttribute<FormComponentConfigurationAttribute>();

        Expect.Multiple(() =>
        {
            Assert.That(dropDown, Is.Not.Null, "the Field input must be a drop-down");
            Assert.That(configuration?.Identifier, Is.EqualTo(XpSearchConstants.WeightFieldConfiguratorIdentifier));

            // The configurator reads the index through IFormFieldValueProvider, which only exposes
            // fields that precede the configured one.
            Assert.That(configuration!.DependencyFieldNames, Does.Contain(nameof(FieldWeightModel.IndexName)));
            Assert.That(
                dropDown!.Order,
                Is.GreaterThan(typeof(FieldWeightModel).GetProperty(nameof(FieldWeightModel.IndexName))!
                    .GetCustomAttribute<TextInputComponentAttribute>()!.Order));
        });
    }

    [Test]
    public void A_stored_field_the_schema_no_longer_offers_stays_selectable()
    {
        Expect.Multiple(() =>
        {
            Assert.That(
                WeightFieldConfigurator.WithStoredValue("title;title\r\nsummary;summary", "retired"),
                Is.EqualTo("retired;retired\r\ntitle;title\r\nsummary;summary"));

            // Already offered: the list is untouched, so no duplicate option appears.
            Assert.That(
                WeightFieldConfigurator.WithStoredValue("title;title\r\nsummary;summary", "title"),
                Is.EqualTo("title;title\r\nsummary;summary"));

            // Nothing stored (a new weight), or no schema options at all.
            Assert.That(WeightFieldConfigurator.WithStoredValue("title;title", ""), Is.EqualTo("title;title"));
            Assert.That(WeightFieldConfigurator.WithStoredValue(null, "retired"), Is.EqualTo("retired;retired"));
            Assert.That(WeightFieldConfigurator.WithStoredValue(null, null), Is.Empty);
        });
    }
}
