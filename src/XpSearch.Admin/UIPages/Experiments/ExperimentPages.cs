using System.Globalization;

using CMS.DataEngine;
using CMS.Helpers;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Core.Analytics;
using XpSearch.Core.Experiments;

using IFormItemCollectionProvider = Kentico.Xperience.Admin.Base.Forms.Internal.IFormItemCollectionProvider;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "experiments",
    uiPageType: typeof(ExperimentListing),
    name: "Experiments",
    templateName: TemplateNames.LISTING,
    order: 800)]

[assembly: UIPage(
    parentType: typeof(ExperimentListing),
    slug: "create",
    uiPageType: typeof(ExperimentCreate),
    name: "New experiment",
    templateName: TemplateNames.EDIT,
    order: 100)]

[assembly: UIPage(
    parentType: typeof(ExperimentListing),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(ExperimentSection),
    name: "Experiment",
    templateName: TemplateNames.SECTION_LAYOUT,
    order: 200)]

[assembly: UIPage(
    parentType: typeof(ExperimentSection),
    slug: "detail",
    uiPageType: typeof(ExperimentDetailPage),
    name: "Overview",
    templateName: ExperimentDetailPage.TemplateName,
    order: 100)]

namespace XpSearch.Admin.UIPages.Experiments;

/// <summary>Lists the experiments of one index (amendment 2026-08-25).</summary>
public class ExperimentListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="ExperimentListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    public ExperimentListing(ILuceneConfigurationStorageService storageService) =>
        this.storageService = storageService;

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchExperimentInfo.OBJECT_TYPE;

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    private string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string index = IndexName;

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchExperimentInfo.ExperimentDisplayName), "Experiment", searchable: true)
            .AddColumn(
                nameof(XpSearchExperimentInfo.ExperimentState),
                "State",
                formatter: (value, _) => ExperimentScope.Label((ExperimentState)ValidationHelper.GetInteger(value, 0, CultureInfo.InvariantCulture)))
            .AddColumn(
                nameof(XpSearchExperimentInfo.ExperimentSplitPercent),
                "Traffic to B",
                formatter: (value, _) => $"{ValidationHelper.GetInteger(value, 0, CultureInfo.InvariantCulture)}%")
            .AddColumn(nameof(XpSearchExperimentInfo.ExperimentStarted), "Started")
            .AddColumn(nameof(XpSearchExperimentInfo.ExperimentEnded), "Ended")
            .AddColumn(
                nameof(XpSearchExperimentInfo.ExperimentConcludedOutcome),
                "Outcome",
                formatter: (value, _) => ExperimentScope.Label((ExperimentOutcome)ValidationHelper.GetInteger(value, 0, CultureInfo.InvariantCulture)));

        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = "One experiment per index at a time",
                Content = "An experiment splits the index's traffic between its live tuning (A) and a draft copy of it (B). A new one can only be created once the current one has been concluded.",
                Type = CalloutType.QuickTip,
                Placement = CalloutPlacement.OnDesk
            }
        ];

        PageConfiguration.HeaderActions.AddLink<ExperimentCreate>("New experiment", parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.AddEditRowAction<ExperimentDetailPage>(parameters: IndexScope.Route(IndexIdentifier));
        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query.WhereEquals(nameof(XpSearchExperimentInfo.ExperimentIndexName), index));

        return base.ConfigurePage();
    }
}

/// <summary>The form behind a new experiment.</summary>
public class ExperimentModel : IIndexScopedModel
{
    /// <summary>Gets or sets the code name of the index the experiment tests. Set from the URL, not editable.</summary>
    [TextInputComponent(Label = "Index", Order = 1)]
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets what the editor calls the experiment.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Name", Order = 2, Tooltip = "What is being tested, for example: boost recent articles.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the percentage of traffic sent to variant B.</summary>
    [RequiredValidationRule]
    [NumberInputComponent(Label = "Traffic to variant B (%)", Order = 3, Tooltip = "Between 1 and 99. The rest of the traffic keeps the live tuning.")]
    public int SplitPercent { get; set; } = 50;
}

/// <summary>
/// Creates a draft experiment, which clones the index's whole live tuning into the experiment's
/// variant B.
/// </summary>
[UIEvaluatePermission(SystemPermissions.CREATE)]
public class ExperimentCreate : IndexScopedEditPage<ExperimentModel>
{
    private readonly IExperimentService experiments;

    /// <summary>Initializes a new instance of the <see cref="ExperimentCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="storageService">Reads the stored index configuration.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="experiments">Creates the experiment and clones the live tuning into it.</param>
    public ExperimentCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneConfigurationStorageService storageService,
        IPageLinkGenerator pageLinkGenerator,
        IExperimentService experiments)
        : base(formItemCollectionProvider, formDataBinder, storageService, pageLinkGenerator) =>
        this.experiments = experiments;

    /// <inheritdoc />
    protected override Type? RedirectTo => typeof(ExperimentListing);

    /// <inheritdoc />
    protected override ExperimentModel CreateModel() => new();

    /// <summary>
    /// The service refuses a second unfinished experiment and a split that starves a variant. Both are
    /// the editor's mistake, not a bug, so they come back as a validation failure rather than a 500.
    /// </summary>
    /// <inheritdoc />
    protected override async Task<ICommandResponse> ProcessFormData(ExperimentModel model, ICollection<IFormItem> formItems)
    {
        try
        {
            return await base.ProcessFormData(model, formItems).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ResponseFrom(new FormSubmissionResult(FormSubmissionStatus.ValidationFailure))
                .AddErrorMessage(exception.Message);
        }
    }

    /// <inheritdoc />
    protected override async Task<string> PersistAsync(ExperimentModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var created = await experiments
            .CreateDraftAsync(submitted.IndexName, submitted.Name.Trim(), submitted.SplitPercent, cancellationToken)
            .ConfigureAwait(false);

        return $"Experiment '{created.ExperimentDisplayName}' created. Its variant B is a copy of the live tuning; edit it, then start the experiment.";
    }
}

/// <summary>Carries the experiment's identifier in the URL, for the detail page and every variant-B editor.</summary>
public class ExperimentSection : EditSectionPage<XpSearchExperimentInfo>
{
}

/// <summary>
/// One experiment: its split while it is a draft, the live comparison report while it runs, and the
/// final snapshot once it is over (amendment 2026-08-25).
/// </summary>
/// <remarks>
/// A custom client template, because the page is a state machine with two irreversible actions behind
/// confirmation dialogs and a two-column report - no built-in template expresses that. Every command is
/// declared here, on the final page class, because inherited or re-annotated ones have failed
/// discovery on the host (see docs/internal/agent-primer.md).
/// </remarks>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class ExperimentDetailPage : Page<ExperimentDetailClientProperties>
{
    /// <summary>Name the registration uses for the client template.</summary>
    public const string TemplateName = "@xperience-community/xperience-search/ExperimentDetail";

    /// <summary>
    /// How many rows each of the analytics service's top-N lists is asked for. The report shows totals
    /// only, so one row is all that has to be built.
    /// </summary>
    private const int ReportRowLimit = 1;

    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IExperimentCatalog catalog;
    private readonly IExperimentService experiments;
    private readonly ISearchAnalyticsService analytics;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="ExperimentDetailPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="catalog">Reads the experiment in the URL.</param>
    /// <param name="experiments">Runs the state transitions.</param>
    /// <param name="analytics">Produces each variant's side of the report.</param>
    /// <param name="time">Clock, so a running experiment's report ends now.</param>
    public ExperimentDetailPage(
        ILuceneConfigurationStorageService storageService,
        IExperimentCatalog catalog,
        IExperimentService experiments,
        ISearchAnalyticsService analytics,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(time);

        this.storageService = storageService;
        this.catalog = catalog;
        this.experiments = experiments;
        this.analytics = analytics;
        this.time = time;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets or sets the identifier of the experiment, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ExperimentSection))]
    public int ExperimentIdentifier { get; set; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    private string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    public override Task<ExperimentDetailClientProperties> ConfigureTemplateProperties(ExperimentDetailClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        properties.IndexName = IndexName;
        properties.MinSplit = ExperimentRules.MinSplit;
        properties.MaxSplit = ExperimentRules.MaxSplit;

        return Task.FromResult(properties);
    }

    /// <summary>Reads the experiment and both variants' observed rates.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<ExperimentReportDto>> Load(CancellationToken cancellationToken) =>
        ResponseFrom(await ReportAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Changes how much traffic variant B will get. Only a draft can still be changed.</summary>
    /// <param name="request">The new split.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report as it is after the change.</returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<ExperimentReportDto>> SetSplit(SplitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await RunAsync(
            experiment => experiments.SetSplitAsync(experiment.Id, request.SplitPercent, cancellationToken),
            $"Variant B now gets {request.SplitPercent}% of the traffic.",
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Starts splitting the index's traffic between the live tuning and the draft.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report as it is after the start.</returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<ExperimentReportDto>> Start(CancellationToken cancellationToken) =>
        await RunAsync(
            experiment => experiments.StartAsync(experiment.Id, cancellationToken),
            "The experiment is running. Every visitor is bucketed from their next search on.",
            cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Ends the experiment, promoting variant B to live or throwing it away.</summary>
    /// <param name="request">Which way to conclude.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final report.</returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public async Task<ICommandResponse<ExperimentReportDto>> Conclude(ConcludeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await RunAsync(
            experiment => experiments.ConcludeAsync(experiment.Id, request.Promote, cancellationToken),
            request.Promote
                ? "Variant B is now the live tuning of the index."
                : "Variant B was deleted. The live tuning is unchanged.",
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one state transition on the experiment in the URL and answers with the report afterwards.
    /// The service owns the state machine; a transition it refuses comes back as a message.
    /// </summary>
    private async Task<ICommandResponse<ExperimentReportDto>> RunAsync(
        Func<ExperimentSummary, Task> transition,
        string success,
        CancellationToken cancellationToken)
    {
        if (ExperimentScope.Resolve(catalog, ExperimentIdentifier, IndexName) is not { } experiment)
        {
            return ResponseFrom(ExperimentReportDto.Failed(NotFound));
        }

        try
        {
            await transition(experiment).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ResponseFrom(await ReportAsync(cancellationToken).ConfigureAwait(false)).AddErrorMessage(exception.Message);
        }

        return ResponseFrom(await ReportAsync(cancellationToken).ConfigureAwait(false)).AddSuccessMessage(success);
    }

    private async Task<ExperimentReportDto> ReportAsync(CancellationToken cancellationToken)
    {
        string index = IndexName;

        if (ExperimentScope.Resolve(catalog, ExperimentIdentifier, index) is not { } experiment)
        {
            return ExperimentReportDto.Failed(NotFound);
        }

        // A draft has answered nothing yet, so there is no range to report on at all.
        var sides = experiment.State == ExperimentState.Draft
            ? (A: VariantStatsDto.Empty(nameof(SearchVariant.A)), B: VariantStatsDto.Empty(nameof(SearchVariant.B)))
            : (A: await SideAsync(index, experiment, SearchVariant.A, cancellationToken).ConfigureAwait(false),
               B: await SideAsync(index, experiment, SearchVariant.B, cancellationToken).ConfigureAwait(false));

        return new ExperimentReportDto(
            experiment.DisplayName,
            ExperimentScope.Label(experiment.State),
            ExperimentScope.Label(experiment.Outcome),
            experiment.SplitPercent,
            ExperimentScope.Moment(experiment.Started),
            ExperimentScope.Moment(experiment.Ended),
            sides.A,
            sides.B,
            string.Empty);
    }

    /// <summary>
    /// One variant's totals, over the searches that variant answered between the start of the
    /// experiment and its end - or now, while it is still running.
    /// </summary>
    private async Task<VariantStatsDto> SideAsync(
        string index,
        ExperimentSummary experiment,
        SearchVariant variant,
        CancellationToken cancellationToken)
    {
        var report = await analytics.GetReportAsync(
            new SearchAnalyticsQuery(
                index,
                experiment.Started ?? time.GetUtcNow().UtcDateTime,
                experiment.Ended ?? time.GetUtcNow().UtcDateTime,
                ReportRowLimit,
                experiment.Id,
                variant.ToString()),
            cancellationToken)
            .ConfigureAwait(false);

        return VariantStatsDto.From(variant.ToString(), report);
    }

    private static string NotFound => "This experiment does not exist, or it belongs to a different search index.";
}
