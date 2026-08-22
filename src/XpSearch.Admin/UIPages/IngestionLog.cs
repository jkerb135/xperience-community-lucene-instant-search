using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Filters;

using XpSearch.Admin.UIPages;
using XpSearch.Ingestion.Persistence;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "ingestion-log",
    uiPageType: typeof(IngestionLogListing),
    name: "Ingestion log",
    templateName: TemplateNames.LISTING,
    order: 700)]

namespace XpSearch.Admin.UIPages;

/// <summary>Narrows the ingestion log to one index (spec §10.8).</summary>
public class IngestionLogFilterModel
{
    /// <summary>Gets or sets the index code name to filter by.</summary>
    [TextInputComponent(Label = "Index", Order = 1)]
    [FilterCondition(ColumnName = nameof(XpSearchIngestionLogInfo.LogIndexName))]
    [FilterLabel("Index")]
    public string IndexName { get; set; } = string.Empty;
}

/// <summary>
/// Recent ingestion writes, newest first: who wrote, to which index, how many documents and whether
/// it worked (spec §10.8).
/// </summary>
public class IngestionLogListing : ListingPage
{
    /// <inheritdoc />
    protected override string ObjectType => XpSearchIngestionLogInfo.OBJECT_TYPE;

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogCreatedAt), "When", sortable: true)
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogKeyPrefix), "Key")
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogIndexName), "Index", searchable: true)
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogOperation), "Operation")
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogDocumentCount), "Documents")
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogSucceeded), "Succeeded")
            .AddColumn(nameof(XpSearchIngestionLogInfo.LogMessage), "Outcome");

        PageConfiguration.FilterConfiguration.FormModel = new IngestionLogFilterModel();

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query.OrderByDescending(nameof(XpSearchIngestionLogInfo.LogCreatedAt)));

        return base.ConfigurePage();
    }
}
