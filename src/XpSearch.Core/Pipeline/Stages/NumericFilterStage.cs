using Lucene.Net.Search;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Filters;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Applies <c>numericFilters</c> as range queries ANDed onto the base query. <c>!=</c> becomes a
/// MUST_NOT of the equality range.
/// </summary>
public sealed class NumericFilterStage : ISearchStage
{
    /// <inheritdoc />
    public int Order => SearchStageOrder.NumericFilters;

    /// <inheritdoc />
    public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.NumericFilters.Count == 0)
        {
            return Task.CompletedTask;
        }

        var combined = new BooleanQuery { { context.BaseQuery, Occur.MUST } };

        foreach (var filter in context.NumericFilters)
        {
            var field = context.Schema.Find(filter.Attribute)!;
            var range = BuildRange(field, filter);

            combined.Add(range, filter.Operator == NumericOperator.NotEqual ? Occur.MUST_NOT : Occur.MUST);
        }

        context.BaseQuery = combined;
        return Task.CompletedTask;
    }

    private static Query BuildRange(SchemaField field, NumericFilter filter)
    {
        (double? min, double? max, bool minInclusive, bool maxInclusive) = filter.Operator switch
        {
            NumericOperator.LessThan => ((double?)null, (double?)filter.Value, true, false),
            NumericOperator.LessThanOrEqual => (null, filter.Value, true, true),
            NumericOperator.GreaterThan => (filter.Value, null, false, true),
            NumericOperator.GreaterThanOrEqual => (filter.Value, null, true, true),
            _ => (filter.Value, filter.Value, true, true)
        };

        // Dates are indexed as Unix epoch seconds in an Int64Field, everything else as a DoubleField;
        // the range query must match the field's numeric type or it silently matches nothing.
        if (field.Kind != SearchFieldKind.Date)
        {
            return NumericRangeQuery.NewDoubleRange(field.Name, min, max, minInclusive, maxInclusive);
        }

        // An exclusive bound on an integer field is folded into an inclusive one, so a fractional
        // comparand such as "publishedAt<50.5" still means "at most 50".
        long? intMin = min is null ? null : (long)(minInclusive ? Math.Ceiling(min.Value) : Math.Floor(min.Value) + 1);
        long? intMax = max is null ? null : (long)(maxInclusive ? Math.Floor(max.Value) : Math.Ceiling(max.Value) - 1);

        return NumericRangeQuery.NewInt64Range(field.Name, intMin, intMax, true, true);
    }
}
