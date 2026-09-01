using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Widgets.Rendering;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The server reads a shared result URL with the same mapping the client writes it with, so the
/// first paint shows what the link promised. Mirrors <c>routing.test.ts</c>.
/// </summary>
[TestFixture]
internal sealed class SearchQueryStateTests
{
    private static SearchRequest Parse(string queryString)
    {
        var request = new SearchRequest { Index = "site-content" };
        SearchQueryState.Apply(request, new QueryCollection(QueryHelpers.ParseQuery(queryString)));

        return request;
    }

    [Test]
    public void Query_page_sort_facets_and_numeric_filters_are_read()
    {
        var request = Parse("?q=espresso&page=2&sort=price_asc&tags=coffee&price_lte=50");

        Expect.Multiple(() =>
        {
            Assert.That(request.Query, Is.EqualTo("espresso"));
            Assert.That(request.Page, Is.EqualTo(2));
            Assert.That(request.Sort, Is.EqualTo("price_asc"));
            Assert.That(request.Filters!.Facets![0].Attribute, Is.EqualTo("tags"));
            Assert.That(request.Filters.Facets[0].Values, Is.EqualTo(new[] { "coffee" }));
            Assert.That(request.Filters.Numeric![0].Attribute, Is.EqualTo("price"));
            Assert.That(request.Filters.Numeric[0].Operator, Is.EqualTo(NumericOperator.Lte));
            Assert.That(request.Filters.Numeric[0].Value, Is.EqualTo(50));
        });
    }

    [Test]
    public void Absent_params_are_left_alone_so_the_widgets_own_defaults_apply()
    {
        var request = Parse("?utm_source=newsletter&q=");

        Expect.Multiple(() =>
        {
            // `utm_source` is a facet as far as the mapping is concerned - the client reads it the
            // same way - but page, sort and the numeric list stay untouched.
            Assert.That(request.Query, Is.Empty);
            Assert.That(request.Page, Is.Null);
            Assert.That(request.Sort, Is.Null);
            Assert.That(request.Filters!.Numeric, Is.Null);
        });

        Assert.That(Parse(string.Empty).Filters, Is.Null);
    }

    [Test]
    public void Facet_values_are_comma_joined_and_each_one_is_escaped()
    {
        // What `defaultStateToRoute` writes for ['Article', 'coffee, milk']: the comma inside a value
        // is percent-escaped, and the URL escapes that percent again.
        var request = Parse("?tags=Article%252Ccoffee%252C%2520milk");

        Assert.That(
            request.Filters!.Facets![0].Values,
            Is.EqualTo(new[] { "Article,coffee, milk" }));
    }

    [Test]
    public void An_and_operator_param_applies_to_its_attribute_and_is_not_a_facet_of_its_own()
    {
        var request = Parse("?tags=coffee,milk&tags_op=and&other=x&other_op=nonsense");

        Expect.Multiple(() =>
        {
            var facets = request.Filters!.Facets!;
            Assert.That(facets.Select(facet => facet.Attribute), Is.EqualTo(new[] { "tags", "other" }));
            Assert.That(facets[0].Values, Is.EqualTo(new[] { "coffee", "milk" }));
            Assert.That(facets[0].Operator, Is.EqualTo(FacetOperator.And));
            Assert.That(facets[1].Operator, Is.Null);
        });
    }

    [Test]
    public void The_first_page_and_an_unparsable_page_are_ignored()
    {
        Expect.Multiple(() =>
        {
            Assert.That(Parse("?page=1").Page, Is.Null);
            Assert.That(Parse("?page=abc").Page, Is.Null);
            Assert.That(Parse("?page=0").Page, Is.Null);
            Assert.That(Parse("?page=12").Page, Is.EqualTo(12));
        });
    }

    [Test]
    public void Repeated_comparisons_become_one_filter_each_and_a_non_numeric_one_is_a_facet()
    {
        var request = Parse("?price_gte=10&price_gte=20&size_gte=large");

        Expect.Multiple(() =>
        {
            Assert.That(request.Filters!.Numeric!.Select(filter => filter.Value), Is.EqualTo(new[] { 10d, 20d }));
            // Not a number, so it means what the client makes of it: a facet named `size_gte`.
            Assert.That(request.Filters.Facets!.Single().Attribute, Is.EqualTo("size_gte"));
        });
    }

    [Test]
    public void Only_attributes_the_index_has_become_filters_when_a_schema_is_supplied()
    {
        var request = new SearchRequest { Index = "site-content" };
        var schema = new IndexSchema(
            "site-content",
            [
                new SchemaField("tags", SearchFieldKind.Taxonomy, false, true, false, true),
                new SchemaField("price", SearchFieldKind.Number, false, false, true, true)
            ]);

        SearchQueryState.Apply(
            request,
            // `uh` is Kentico's own preview parameter; `weight` is not a field of this index.
            new QueryCollection(QueryHelpers.ParseQuery("?q=beans&uh=abc123&tags=coffee&price_lte=50&weight_gte=5")),
            schema);

        Expect.Multiple(() =>
        {
            Assert.That(request.Query, Is.EqualTo("beans"));
            Assert.That(request.Filters!.Facets!.Single().Attribute, Is.EqualTo("tags"));
            Assert.That(request.Filters.Numeric!.Single().Attribute, Is.EqualTo("price"));
        });
    }

    [Test]
    public void An_index_without_filterable_params_leaves_the_filters_unset()
    {
        var request = new SearchRequest { Index = "site-content" };
        var schema = new IndexSchema("site-content", [new SchemaField("title", SearchFieldKind.Text, true, false, false, true)]);

        SearchQueryState.Apply(request, new QueryCollection(QueryHelpers.ParseQuery("?q=beans&uh=abc123")), schema);

        Assert.That(request.Filters, Is.Null);
    }

    [Test]
    public void Without_a_schema_every_param_is_still_read_as_a_filter()
    {
        // The graceful fallback when the schema cannot be resolved: the old behaviour, which the
        // pipeline turns into an empty mount rather than a broken page.
        Assert.That(Parse("?uh=abc123").Filters!.Facets!.Single().Attribute, Is.EqualTo("uh"));
    }

    [Test]
    public void Every_operator_of_the_contract_is_recognized()
    {
        var request = Parse("?a_lt=1&a_lte=2&a_eq=3&a_ne=4&a_gte=5&a_gt=6");

        Assert.That(
            request.Filters!.Numeric!.Select(filter => filter.Operator),
            Is.EqualTo(new[]
            {
                NumericOperator.Lt,
                NumericOperator.Lte,
                NumericOperator.Eq,
                NumericOperator.Ne,
                NumericOperator.Gte,
                NumericOperator.Gt
            }));
    }
}
