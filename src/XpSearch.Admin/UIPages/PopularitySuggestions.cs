using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.UIPages;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tuning;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "suggestions",
    uiPageType: typeof(PopularitySuggestionListing),
    name: "Suggestions",
    templateName: TemplateNames.LISTING,
    order: 250)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The ordinary rule an approved suggestion turns into (RK-1). Separate from the page so what
/// approval writes can be asserted without an administration.
/// </summary>
public static class PopularitySuggestionRule
{
    /// <summary>The multiplier an approved suggestion boosts its document by.</summary>
    /// <remarks>
    /// The same ceiling the query-time signal is capped at, so approving a suggestion cannot push a
    /// document further than the automatic boost would have.
    /// </remarks>
    public const double Multiplier = PopularitySignal.MaxFactor;

    /// <summary>Builds the rule an approved suggestion creates.</summary>
    /// <param name="query">The query the suggestion is about.</param>
    /// <param name="documentId">Result id of the document that wins its clicks.</param>
    /// <returns>The rule's display name, its <c>if</c> and its <c>then</c>.</returns>
    /// <remarks>
    /// Exactly what a marketer would have typed: this query, that document, boosted. It is created
    /// enabled - approval is the human decision the amendment asks for - and is then an ordinary rule,
    /// editable and deletable like any other.
    /// </remarks>
    public static (string Name, RuleConditions Conditions, IReadOnlyList<RuleAction> Actions) For(string query, string documentId)
    {
        string text = (query ?? string.Empty).Trim();

        return (
            $"Popular for '{text}'",
            new RuleConditions(new QueryCondition(Core.Tuning.QueryOperator.Is, text, MatchAnalyzed: false), [], string.Empty, string.Empty),
            [new RuleAction.Boost((documentId ?? string.Empty).Trim(), string.Empty, Multiplier)]);
    }
}

/// <summary>
/// The suggested boost rules of one index (RK-1): what the popularity task noticed, waiting for a
/// human to approve or dismiss. Nothing here affects a search until somebody approves.
/// </summary>
/// <remarks>
/// A listing of its own next to <see cref="RuleListing"/> rather than a section inside it: the rules
/// listing uses the stock LISTING template, and its <c>RoutingContentPlaceholder</c> is what makes
/// <c>ZeroResultRuleCreatePage</c> render at all (HW-10 defect 3) - replacing it with a custom React
/// template to gain a panel above the table would break that. The rules listing links here instead.
/// Live rules only; an experiment's variant B has no suggestions.
/// </remarks>
public class PopularitySuggestionListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IInfoProvider<XpSearchPopularitySuggestionInfo> provider;
    private readonly IInfoProvider<XpSearchRuleInfo> rules;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="PopularitySuggestionListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of suggestion objects.</param>
    /// <param name="rules">Provider of rule objects, which an approval writes to.</param>
    public PopularitySuggestionListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchPopularitySuggestionInfo> provider,
        IInfoProvider<XpSearchRuleInfo> rules)
    {
        this.storageService = storageService;
        this.provider = provider;
        this.rules = rules;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchPopularitySuggestionInfo.OBJECT_TYPE;

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <summary>Turns one suggestion into an ordinary rule and takes it off the list for good.</summary>
    /// <param name="id">The identifier of the suggestion.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.CREATE)]
    public Task<ICommandResponse<RowActionResult>> Approve(int id)
    {
        var row = Scoped(id);

        if (row is null)
        {
            return Refuse();
        }

        var (name, conditions, actions) = PopularitySuggestionRule.For(row.SuggestionQuery, row.SuggestionDocumentID);

        rules.Set(new XpSearchRuleInfo
        {
            RuleGuid = Guid.NewGuid(),
            RuleIndexName = row.SuggestionIndexName,
            RuleName = name,
            RuleEnabled = true,
            RuleMigrated = false,
            RulePriority = 100,
            RuleConditions = RuleJson.Write(conditions),
            RuleActions = RuleJson.Write(actions),
            RuleExperimentID = null
        });

        return Answer(row, PopularitySuggestionState.Approved, $"Rule \"{name}\" created.");
    }

    /// <summary>Turns one suggestion down. It never comes back for that query and document.</summary>
    /// <param name="id">The identifier of the suggestion.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.UPDATE)]
    public Task<ICommandResponse<RowActionResult>> Dismiss(int id)
    {
        var row = Scoped(id);

        return row is null
            ? Refuse()
            : Answer(row, PopularitySuggestionState.Dismissed, "Suggestion dismissed.");
    }

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        string index = IndexName;

        PageConfiguration.Caption = "Suggested boost rules";
        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = "Suggestions are never applied on their own",
                Content = "The popularity task lists the frequent queries where one result clearly wins the clicks. "
                    + "Approving one creates an ordinary rule you can edit or delete; dismissing one hides it for good.",
                ContentAsHtml = false,
            }
        ];

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchPopularitySuggestionInfo.SuggestionQuery), "Query", searchable: true)
            .AddColumn(nameof(XpSearchPopularitySuggestionInfo.SuggestionDocumentID), "Document")
            .AddColumn(
                nameof(XpSearchPopularitySuggestionInfo.SuggestionClicks),
                "Evidence",
                formatter: (value, row) => Evidence(
                    CMS.Helpers.ValidationHelper.GetInteger(value, 0),
                    CMS.Helpers.ValidationHelper.GetInteger(
                        row?.GetValue(nameof(XpSearchPopularitySuggestionInfo.SuggestionSharePercent)),
                        0)))
            .AddColumn(nameof(XpSearchPopularitySuggestionInfo.SuggestionComputed), "Computed");

        // The label is what a screen reader announces; the icon is what the action cell renders.
        PageConfiguration.TableActions.AddCommand("Approve", nameof(Approve), icon: "icon-check-circle");
        PageConfiguration.TableActions.AddCommand("Dismiss", nameof(Dismiss), icon: "icon-times-circle");

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionIndexName), index)
                .WhereEquals(nameof(XpSearchPopularitySuggestionInfo.SuggestionState), (int)PopularitySuggestionState.Pending)
                .OrderByDescending(nameof(XpSearchPopularitySuggestionInfo.SuggestionClicks)));

        return base.ConfigurePage();
    }

    /// <summary>Renders the evidence of one suggestion: how many clicks, and what share of the query's click mass.</summary>
    /// <param name="clicks">How many clicks the document took on the query.</param>
    /// <param name="sharePercent">Its share of the query's damped click mass, in whole percent.</param>
    /// <returns>The text the Evidence column shows.</returns>
    public static string Evidence(int clicks, int sharePercent) =>
        $"{clicks} clicks, {sharePercent}% of the query's clicks";

    /// <summary>Reads the suggestion of a command, refusing one that belongs to another index (ADR-0017).</summary>
    private XpSearchPopularitySuggestionInfo? Scoped(int id)
    {
        var row = provider.Get(id);

        return IndexScope.Matches(row?.SuggestionIndexName, IndexName) ? row : null;
    }

    private Task<ICommandResponse<RowActionResult>> Answer(
        XpSearchPopularitySuggestionInfo row,
        PopularitySuggestionState state,
        string message)
    {
        row.SuggestionState = (int)state;
        provider.Set(row);

        return Task.FromResult(ResponseFrom(new RowActionResult(true)).AddSuccessMessage(message));
    }

    private Task<ICommandResponse<RowActionResult>> Refuse() =>
        Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
}
