using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using XpSearch.Admin.UIPages;
using XpSearch.Ingestion.Persistence;
using XpSearch.Ingestion.Security;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "api-keys",
    uiPageType: typeof(ApiKeyListing),
    name: "API keys",
    templateName: TemplateNames.LISTING,
    order: 500)]

[assembly: UIPage(
    parentType: typeof(ApiKeyListing),
    slug: "create",
    uiPageType: typeof(ApiKeyCreate),
    name: "New API key",
    templateName: TemplateNames.EDIT,
    order: 100)]

namespace XpSearch.Admin.UIPages;

/// <summary>Lists the ingestion API keys (spec §10.8). The key itself is never stored, so never shown.</summary>
public class ApiKeyListing : ListingPage
{
    /// <inheritdoc />
    protected override string ObjectType => XpSearchApiKeyInfo.OBJECT_TYPE;

    /// <summary>
    /// Revokes one key. Keys are not index-scoped, so there is nothing to check beyond the permission:
    /// deleting the row is what stops the key authenticating.
    /// </summary>
    /// <param name="id">The identifier of the row to delete.</param>
    /// <returns>The row action result.</returns>
    [PageCommand(Permission = SystemPermissions.DELETE)]
    public override Task<ICommandResponse<RowActionResult>> Delete(int id) => base.Delete(id);

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        PageConfiguration.ColumnConfigurations
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyName), "Name", searchable: true)
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyPrefix), "Prefix")
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyScopes), "Scopes")
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyEnabled), "Enabled")
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyExpiresAt), "Expires")
            .AddColumn(nameof(XpSearchApiKeyInfo.KeyLastUsedAt), "Last used", sortable: true);

        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = "Keys are shown once",
                Content = "Only the hash of a key is stored. If a key is lost, create a new one and disable the old.",
                Type = CalloutType.QuickTip,
                Placement = CalloutPlacement.OnDesk
            }
        ];

        PageConfiguration.HeaderActions.AddLink<ApiKeyCreate>("New API key");
        PageConfiguration.TableActions.AddDeleteAction(nameof(Delete), "Delete");

        return base.ConfigurePage();
    }
}

/// <summary>The form behind a new ingestion API key (spec §10.4).</summary>
public class ApiKeyModel
{
    /// <summary>Gets or sets the display name of the key.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Name", Order = 1, Tooltip = "Who or what uses this key, for example: PIM import.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the comma-separated indexes the key may write to.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Indexes", Order = 2, Tooltip = "Comma-separated index code names, or * for every index.")]
    public string Indexes { get; set; } = "*";

    /// <summary>Gets or sets the comma-separated operations the key may perform.</summary>
    [RequiredValidationRule]
    [TextInputComponent(Label = "Operations", Order = 3, Tooltip = "Comma-separated: write, delete, rebuild, read - or * for all.")]
    public string Operations { get; set; } = "write,delete";

    /// <summary>Gets or sets when the key stops working, in UTC. Empty never expires.</summary>
    [DateTimeInputComponent(Label = "Expires", Order = 4)]
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Creates an ingestion API key and shows its plaintext exactly once (spec §10.8).
/// </summary>
/// <remarks>
/// The page deliberately does not redirect after a successful submit: the success message is the one
/// and only place the key exists outside <see cref="IApiKeyService"/>.
/// </remarks>
public class ApiKeyCreate : TuningEditPage<ApiKeyModel>
{
    private readonly IApiKeyService keys;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyCreate"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="keys">Creates and hashes the key.</param>
    public ApiKeyCreate(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        IPageLinkGenerator pageLinkGenerator,
        IApiKeyService keys)
        : base(formItemCollectionProvider, formDataBinder, pageLinkGenerator) =>
        this.keys = keys;

    /// <inheritdoc />
    protected override ApiKeyModel CreateModel() => new();

    /// <inheritdoc />
    protected override async Task<string> PersistAsync(ApiKeyModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var scopes = new ApiKeyScopes
        {
            Indexes = [.. Split(submitted.Indexes)],
            Ops = [.. Split(submitted.Operations)]
        };

        var created = await keys.CreateAsync(submitted.Name, scopes, submitted.ExpiresAt, cancellationToken).ConfigureAwait(false);

        return $"Copy this key now - it is not stored and cannot be shown again: {created.Key}";
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
