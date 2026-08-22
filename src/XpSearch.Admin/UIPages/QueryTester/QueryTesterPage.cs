using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Core.Abstractions;
using XpSearch.Core.Contract;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "query-tester",
    uiPageType: typeof(QueryTesterPage),
    name: "Query tester",
    templateName: "@yourco/xperience-search-admin/QueryTester",
    order: 500)]

namespace XpSearch.Admin.UIPages.QueryTester;

/// <summary>Initial state of the query tester client template.</summary>
public class QueryTesterClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code names of every registered index.</summary>
    public IEnumerable<string> IndexNames { get; set; } = [];

    /// <summary>Gets or sets the index selected when the page opens.</summary>
    public string SelectedIndexName { get; set; } = string.Empty;
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

    private readonly ILuceneIndexManager indexManager;
    private readonly IQueryTesterSearch search;

    /// <summary>Initializes a new instance of the <see cref="QueryTesterPage"/> class.</summary>
    /// <param name="indexManager">The integration's index registry, used to fill the index selector.</param>
    /// <param name="search">Runs the two sides of the comparison.</param>
    public QueryTesterPage(ILuceneIndexManager indexManager, IQueryTesterSearch search)
    {
        ArgumentNullException.ThrowIfNull(indexManager);
        ArgumentNullException.ThrowIfNull(search);

        this.indexManager = indexManager;
        this.search = search;
    }

    /// <inheritdoc />
    public override Task<QueryTesterClientProperties> ConfigureTemplateProperties(QueryTesterClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var names = indexManager.GetAllIndices()
            .Select(index => index.IndexName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        properties.IndexNames = names;
        properties.SelectedIndexName = names.FirstOrDefault() ?? string.Empty;

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

        if (string.IsNullOrWhiteSpace(request.IndexName))
        {
            return ResponseFrom(QueryTesterResult.Failed("Choose an index to test against."));
        }

        try
        {
            var withRules = await search.ExecuteAsync(Build(request), applyTuning: true, cancellationToken).ConfigureAwait(false);
            var withoutRules = await search.ExecuteAsync(Build(request), applyTuning: false, cancellationToken).ConfigureAwait(false);

            return ResponseFrom(QueryTesterDiff.Compare(withRules, withoutRules));
        }
        catch (IndexNotFoundException)
        {
            return ResponseFrom(QueryTesterResult.Failed($"The index '{request.IndexName}' is not registered."));
        }
        catch (SearchValidationException exception)
        {
            return ResponseFrom(QueryTesterResult.Failed(exception.Message));
        }
    }

    /// <summary>Builds the search request one side runs. Each side needs its own instance.</summary>
    /// <param name="request">What the client asked for.</param>
    /// <returns>The request.</returns>
    private static SearchRequest Build(QueryTesterRequest request) =>
        new()
        {
            Index = request.IndexName,
            Query = request.Query ?? string.Empty,
            Language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language,
            Page = 1,
            PageSize = Math.Clamp(request.PageSize, 1, MaxPageSize),
            Explain = true
        };
}
