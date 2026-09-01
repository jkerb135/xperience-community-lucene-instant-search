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
    slug: "synonym-suggestions",
    uiPageType: typeof(SynonymSuggestionListing),
    name: "Synonym suggestions",
    templateName: TemplateNames.LISTING,
    order: 350)]

namespace XpSearch.Admin.UIPages;

/// <summary>
/// The ordinary synonym group an approved suggestion turns into (SY-1). Separate from the page so what
/// approval writes can be asserted without an administration.
/// </summary>
public static class SynonymSuggestionGroup
{
    /// <summary>Builds the group an approved suggestion creates.</summary>
    /// <param name="failedQuery">The query that got no click.</param>
    /// <param name="succeededQuery">The query visitors reformulated to.</param>
    /// <returns>The group's direction, its words and its replacements.</returns>
    /// <remarks>
    /// A two-way group, which is the honest reading of the evidence: the pair says the two phrases mean
    /// the same thing here, not which one is the "right" one. An editor who wants only the failed phrase
    /// rewritten switches the saved group to one-way, exactly as for a hand-written group. Commas are
    /// the term separator of the stored value, so a query containing one is stored as separate words.
    /// </remarks>
    public static (SynonymDirection Direction, string Input, string Output) For(string failedQuery, string succeededQuery) =>
        (SynonymDirection.TwoWay, $"{Term(failedQuery)}, {Term(succeededQuery)}", string.Empty);

    private static string Term(string? query) =>
        SynonymMiner.Text((query ?? string.Empty).Replace(',', ' '));
}

/// <summary>
/// The mined synonym candidates of one index (SY-1): reformulations the task noticed, waiting for a
/// human to approve or dismiss. Nothing here affects a search until somebody approves.
/// </summary>
/// <remarks>
/// A listing of its own next to <see cref="SynonymListing"/>, for the same reason
/// <see cref="PopularitySuggestionListing"/> is one next to the rules: the stock LISTING template's
/// <c>RoutingContentPlaceholder</c> is what makes the child editors render (HW-10 defect 3), so the
/// listing cannot be replaced by a custom template to gain a panel. Live synonyms only; an
/// experiment's variant B has no suggestions.
/// </remarks>
public class SynonymSuggestionListing : ListingPage
{
    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IInfoProvider<XpSearchSynonymSuggestionInfo> provider;
    private readonly IInfoProvider<XpSearchSynonymInfo> synonyms;
    private string? indexName;

    /// <summary>Initializes a new instance of the <see cref="SynonymSuggestionListing"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="provider">Provider of suggestion objects.</param>
    /// <param name="synonyms">Provider of synonym objects, which an approval writes to.</param>
    public SynonymSuggestionListing(
        ILuceneConfigurationStorageService storageService,
        IInfoProvider<XpSearchSynonymSuggestionInfo> provider,
        IInfoProvider<XpSearchSynonymInfo> synonyms)
    {
        this.storageService = storageService;
        this.provider = provider;
        this.synonyms = synonyms;
    }

    /// <summary>Gets or sets the identifier of the index the listing is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <inheritdoc />
    protected override string ObjectType => XpSearchSynonymSuggestionInfo.OBJECT_TYPE;

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    protected string IndexName => indexName ??= IndexScope.Resolve(storageService, IndexIdentifier);

    /// <summary>Turns one suggestion into an ordinary synonym group and takes it off the list for good.</summary>
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

        var (direction, input, output) = SynonymSuggestionGroup.For(row.SynonymSuggestionFailed, row.SynonymSuggestionSucceeded);

        synonyms.Set(new XpSearchSynonymInfo
        {
            SynonymGuid = Guid.NewGuid(),
            SynonymIndexName = row.SynonymSuggestionIndexName,
            SynonymType = (int)direction,
            SynonymInput = input,
            SynonymOutput = output,
            SynonymEnabled = true,
            SynonymExperimentID = null
        });

        return Answer(row, PopularitySuggestionState.Approved, $"Synonym group \"{input}\" created.");
    }

    /// <summary>Turns one suggestion down. It never comes back for that pair.</summary>
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

        PageConfiguration.Caption = "Suggested synonyms";
        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = "Suggestions are never applied on their own",
                Content = "The popularity task lists searches that got no click, followed within a minute by a "
                    + "different search that did. Approving one creates an ordinary two-way synonym group you can "
                    + "edit or delete; dismissing one hides it for good. The pairing is by timing, not by visitor, "
                    + "so read the evidence before approving.",
                ContentAsHtml = false,
            }
        ];

        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionFailed), "Searched for", searchable: true)
            .AddColumn(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionSucceeded), "Then found it with")
            .AddColumn(
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionOccurrences),
                "Evidence",
                formatter: (value, _) => Evidence(CMS.Helpers.ValidationHelper.GetInteger(value, 0)))
            .AddColumn(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionLastSeen), "Last seen");

        // Labelled buttons, no icon-only actions: the label is what a screen reader announces.
        PageConfiguration.TableActions.AddCommand("Approve", nameof(Approve));
        PageConfiguration.TableActions.AddCommand("Dismiss", nameof(Dismiss));

        PageConfiguration.QueryModifiers.AddModifier((query, _) =>
            query
                .WhereEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionIndexName), index)
                .WhereEquals(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionState), (int)PopularitySuggestionState.Pending)
                .OrderByDescending(nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionOccurrences)));

        return base.ConfigurePage();
    }

    /// <summary>Renders the evidence of one suggestion: how often the reformulation happened.</summary>
    /// <param name="occurrences">How often the pair happened in the window.</param>
    /// <returns>The text the Evidence column shows.</returns>
    public static string Evidence(int occurrences) =>
        occurrences == 1 ? "1 reformulation" : $"{occurrences} reformulations";

    /// <summary>Reads the suggestion of a command, refusing one that belongs to another index (ADR-0017).</summary>
    private XpSearchSynonymSuggestionInfo? Scoped(int id)
    {
        var row = provider.Get(id);

        return IndexScope.Matches(row?.SynonymSuggestionIndexName, IndexName) ? row : null;
    }

    private Task<ICommandResponse<RowActionResult>> Answer(
        XpSearchSynonymSuggestionInfo row,
        PopularitySuggestionState state,
        string message)
    {
        row.SynonymSuggestionState = (int)state;
        provider.Set(row);

        return Task.FromResult(ResponseFrom(new RowActionResult(true)).AddSuccessMessage(message));
    }

    private Task<ICommandResponse<RowActionResult>> Refuse() =>
        Task.FromResult(ResponseFrom(new RowActionResult(false)).AddErrorMessage(IndexScope.CrossIndexDeleteRefusal));
}
