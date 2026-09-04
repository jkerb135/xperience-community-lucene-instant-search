using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Tuning;
using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Admin.UIPages.RuleBuilder;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;
using XpSearch.Core.Tuning;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "query-tester",
    uiPageType: typeof(QueryTesterPage),
    name: "Query tester",
    templateName: "@xperience-community/xperience-search/QueryTester",
    order: 600)]

namespace XpSearch.Admin.UIPages.QueryTester;

/// <summary>Initial state of the query tester client template.</summary>
public class QueryTesterClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the index under test. It comes from the URL and is never a choice.</summary>
    public string SelectedIndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the content language names the index is configured for, in configuration order.</summary>
    public IEnumerable<string> Languages { get; set; } = [];

    /// <summary>Gets or sets the contact groups the tester can simulate, by display name (ADR-0021).</summary>
    public IEnumerable<ContactGroupOption> ContactGroups { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the index's draft or running experiment, so the Variant select can
    /// offer its variant B (XP-1). Empty when the index has no experiment to try.
    /// </summary>
    public string ExperimentName { get; set; } = string.Empty;
}

/// <summary>
/// The query tester (spec §8.4): runs one query twice - with the index's relevance tuning and
/// without any of it - and shows both rankings side by side with their score explanations.
/// </summary>
/// <remarks>
/// A custom client template, because no built-in template can express two ranked lists with
/// per-hit explanations
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
/// </remarks>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class QueryTesterPage : Page<QueryTesterClientProperties>
{
    /// <summary>Largest page size the tester will run, so a mistyped number cannot pull a whole index.</summary>
    public const int MaxPageSize = 50;

    /// <summary>The rule action a "Pin for this query" seeds; one of <see cref="RuleActionDto.Types"/>.</summary>
    public const string PinAction = "pin";

    /// <summary>The rule action a "Bury for this query" seeds; one of <see cref="RuleActionDto.Types"/>.</summary>
    public const string BuryAction = "bury";

    /// <summary>The message an "Open rule" for a rule this index does not have is refused with.</summary>
    public const string ForeignRuleRefusal = "This rule belongs to a different search index and was not opened.";

    /// <summary>The message an "Open rule" for a rule of the experiment's variant B is refused with.</summary>
    public const string VariantRuleRefusal = "This is a variant rule; open it from the experiment's own rule listing.";

    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IQueryTesterSearch search;
    private readonly IPageLinkGenerator pageLinkGenerator;
    private readonly IContactGroupCatalog contactGroups;
    private readonly IExperimentCatalog experiments;
    private readonly IRelevanceTuningSource tuning;

    /// <summary>Initializes a new instance of the <see cref="QueryTesterPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="search">Runs the two sides of the comparison.</param>
    /// <param name="pageLinkGenerator">Generates the URL the error callout's "Open status" action navigates to.</param>
    /// <param name="contactGroups">Supplies the contact groups the simulation drop-down offers.</param>
    /// <param name="experiments">Supplies the index's unfinished experiment, whose variant B can be tried (XP-1).</param>
    /// <param name="tuning">Reads the index's rules, so an "Open rule" can only reach one this index has.</param>
    public QueryTesterPage(
        ILuceneConfigurationStorageService storageService,
        IQueryTesterSearch search,
        IPageLinkGenerator pageLinkGenerator,
        IContactGroupCatalog contactGroups,
        IExperimentCatalog experiments,
        IRelevanceTuningSource tuning)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(pageLinkGenerator);
        ArgumentNullException.ThrowIfNull(contactGroups);
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(tuning);

        this.storageService = storageService;
        this.search = search;
        this.pageLinkGenerator = pageLinkGenerator;
        this.contactGroups = contactGroups;
        this.experiments = experiments;
        this.tuning = tuning;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    private string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    public override async Task<QueryTesterClientProperties> ConfigureTemplateProperties(QueryTesterClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var index = IndexScope.ResolveModel(storageService, IndexIdentifier);

        properties.SelectedIndexName = index?.IndexName ?? string.Empty;
        properties.Languages = [.. index?.LanguageNames ?? []];

        properties.ContactGroups = await contactGroups.GetAllAsync(CancellationToken.None).ConfigureAwait(false);

        var experiment = await experiments.GetUnfinishedAsync(properties.SelectedIndexName, CancellationToken.None).ConfigureAwait(false);

        properties.ExperimentName = experiment?.DisplayName ?? string.Empty;

        return properties;
    }

    /// <summary>Opens the status page of the same index, the action the "could not be run" callout offers.</summary>
    /// <returns>A navigation to the status page.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> OpenStatus() =>
        Task.FromResult(NavigateTo(pageLinkGenerator.GetPath<IndexStatusPage>(IndexScope.Route(IndexIdentifier))));

    /// <summary>Runs the query on both sides of the comparison.</summary>
    /// <param name="request">What to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Both rankings, marked with how they differ.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<QueryTesterResult>> Run(QueryTesterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The index is the one the URL points at, never one the client asked for.
        string indexName = IndexName;

        if (string.IsNullOrWhiteSpace(indexName))
        {
            return ResponseFrom(QueryTesterResult.Failed("This index is not registered."));
        }

        try
        {
            string contactGroup = request.ContactGroup ?? string.Empty;

            // Which experiment the variant belongs to is the server's answer, never the client's: the
            // page offers variant B only for the index's own unfinished experiment.
            var variant = request.VariantB
                ? ExperimentScope.Variant(
                    (await experiments.GetUnfinishedAsync(indexName, cancellationToken).ConfigureAwait(false))?.Id ?? 0)
                : TuningVariant.Live;

            var withRules = await search.ExecuteAsync(Build(request, indexName), applyTuning: true, contactGroup, variant, cancellationToken).ConfigureAwait(false);
            var withoutRules = await search.ExecuteAsync(Build(request, indexName), applyTuning: false, contactGroup, variant, cancellationToken).ConfigureAwait(false);

            return ResponseFrom(QueryTesterDiff.Compare(withRules, withoutRules));
        }
        catch (IndexNotFoundException)
        {
            return ResponseFrom(QueryTesterResult.Failed($"The index '{indexName}' is not registered."));
        }
        catch (SearchValidationException exception)
        {
            return ResponseFrom(QueryTesterResult.Failed(exception.Message));
        }
    }

    /// <summary>Opens the rule builder with a rule for this query, and nothing else filled in.</summary>
    /// <param name="request">The query the tester ran.</param>
    /// <returns>A navigation to the seeded create page.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> CreateRule(CreateRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(NavigateTo(SeededPath(request.Query, string.Empty, string.Empty, 0)));
    }

    /// <summary>Opens the rule builder with a pin of one result pre-filled (QT-2).</summary>
    /// <param name="request">The query, the result and the position to pin it to.</param>
    /// <returns>A navigation to the seeded create page.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> PinResult(PinResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(NavigateTo(SeededPath(request.Query, PinAction, request.TargetId, Math.Max(1, request.Position))));
    }

    /// <summary>Opens the rule builder with a bury of one result pre-filled (QT-2).</summary>
    /// <param name="request">The query and the result to bury.</param>
    /// <returns>A navigation to the seeded create page.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> BuryResult(BuryResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(NavigateTo(SeededPath(request.Query, BuryAction, request.TargetId, 1)));
    }

    /// <summary>Opens the rule that touched a result, in this index's own rule builder (QT-2).</summary>
    /// <param name="request">The rule to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A navigation to the rule's edit page, or a refusal when it belongs to another index.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse> OpenRule(OpenRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The rule edit page under this index only holds live rules; a variant-B run lists the
        // experiment's own, which are edited from the experiment section (XP-1).
        if (request.VariantB)
        {
            return Refuse(VariantRuleRefusal);
        }

        var rules = await tuning
            .GetRulesAsync(IndexName, cancellationToken, TuningVariant.Live)
            .ConfigureAwait(false);

        // The rules are read per index, so a rule reached through another index's tester is simply
        // not in this list; it is refused rather than silently opened here.
        if (!rules.Any(rule => rule.Id == request.RuleId))
        {
            return Refuse(ForeignRuleRefusal);
        }

        var parameters = IndexScope.Route(IndexIdentifier);
        parameters.Add(typeof(RuleEditSection), request.RuleId);

        return NavigateTo(pageLinkGenerator.GetPath<RuleEdit>(parameters));
    }

    /// <summary>Answers a command with an error message and no navigation.</summary>
    /// <param name="message">What to tell the user.</param>
    /// <returns>The refusal.</returns>
    private ICommandResponse Refuse(string message) =>
        ResponseFrom(new RowActionResult(false)).AddErrorMessage(message);

    /// <summary>The URL of the create page, seeded with this index, the query and an optional action.</summary>
    /// <param name="query">The query the rule should fire on.</param>
    /// <param name="action">The action to pre-fill, or an empty string for none.</param>
    /// <param name="targetId">Result id the action names.</param>
    /// <param name="position">Position a pin moves the document to.</param>
    /// <returns>The path.</returns>
    private string SeededPath(string? query, string action, string? targetId, int position)
    {
        string text = query ?? string.Empty;
        var parameters = IndexScope.Route(IndexIdentifier);

        parameters.Add(
            typeof(ZeroResultRuleCreatePage),
            action.Length == 0
                ? RuleSeed.Encode(IndexName, text)
                : RuleSeed.Encode(IndexName, text, action, targetId ?? string.Empty, position));

        return pageLinkGenerator.GetPath<ZeroResultRuleCreatePage>(parameters);
    }

    /// <summary>Builds the search request one side runs. Each side needs its own instance.</summary>
    /// <param name="request">What the client asked for.</param>
    /// <param name="indexName">The index the URL resolves to.</param>
    /// <returns>The request.</returns>
    private static SearchRequest Build(QueryTesterRequest request, string indexName) =>
        new()
        {
            Index = indexName,
            Query = request.Query ?? string.Empty,
            Language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language,
            Page = 1,
            PageSize = Math.Clamp(request.PageSize, 1, MaxPageSize),
            Explain = true
        };
}
