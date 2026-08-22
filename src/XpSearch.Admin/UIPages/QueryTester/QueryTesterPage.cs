using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

[assembly: UIPage(
    parentType: typeof(IndexTuningSection),
    slug: "query-tester",
    uiPageType: typeof(QueryTesterPage),
    name: "Query tester",
    templateName: "@yourco/xperience-search-admin/QueryTester",
    order: 600)]

namespace XpSearch.Admin.UIPages.QueryTester;

/// <summary>Initial state of the query tester client template.</summary>
public class QueryTesterClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code names of every registered index.</summary>
    public IEnumerable<string> IndexNames { get; set; } = [];

    /// <summary>Gets or sets the index selected when the page opens.</summary>
    public string SelectedIndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the index comes from the URL and cannot be changed on the page.</summary>
    public bool IndexLocked { get; set; }
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

    private readonly ILuceneConfigurationStorageService storageService;
    private readonly IQueryTesterSearch search;

    /// <summary>Initializes a new instance of the <see cref="QueryTesterPage"/> class.</summary>
    /// <param name="storageService">Reads the stored index configuration, to resolve the index in the URL.</param>
    /// <param name="search">Runs the two sides of the comparison.</param>
    public QueryTesterPage(ILuceneConfigurationStorageService storageService, IQueryTesterSearch search)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(search);

        this.storageService = storageService;
        this.search = search;
    }

    /// <summary>Gets or sets the identifier of the index the page is scoped to, taken from the URL.</summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(IndexTuningSection))]
    public int IndexIdentifier { get; set; }

    /// <summary>Gets the code name of the index in the URL, or an empty string when it is not registered.</summary>
    private string IndexName => IndexScope.Resolve(storageService, IndexIdentifier);

    /// <inheritdoc />
    public override Task<QueryTesterClientProperties> ConfigureTemplateProperties(QueryTesterClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string indexName = IndexName;

        properties.IndexNames = [indexName];
        properties.SelectedIndexName = indexName;
        properties.IndexLocked = true;

        return Task.FromResult(properties);
    }

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
            var withRules = await search.ExecuteAsync(Build(request, indexName), applyTuning: true, cancellationToken).ConfigureAwait(false);
            var withoutRules = await search.ExecuteAsync(Build(request, indexName), applyTuning: false, cancellationToken).ConfigureAwait(false);

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
