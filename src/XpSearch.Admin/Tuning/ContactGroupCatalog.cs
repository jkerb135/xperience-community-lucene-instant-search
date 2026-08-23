using CMS.ContactManagement;
using CMS.DataEngine;

namespace XpSearch.Admin.Tuning;

/// <summary>One contact group, as the admin UI shows it.</summary>
/// <param name="CodeName">Code name, which is what a rule stores and the query pipeline compares.</param>
/// <param name="DisplayName">What a marketer sees.</param>
public sealed record ContactGroupOption(string CodeName, string DisplayName);

/// <summary>
/// The contact groups a rule can be scoped to (ADR-0021): the whole list for the query tester's
/// simulation drop-down, and one display name for the rule listing.
/// </summary>
public interface IContactGroupCatalog
{
    /// <summary>Gets every contact group, ordered by display name.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups.</returns>
    Task<IReadOnlyList<ContactGroupOption>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Gets what to show for a rule's stored contact group.</summary>
    /// <param name="codeName">The stored code name; empty means the rule applies to everyone.</param>
    /// <returns>The group's display name, its code name when the group is gone, or "Everyone".</returns>
    string Label(string? codeName);
}

/// <summary>
/// The default catalog, over <see cref="ContactGroupInfo"/>
/// (https://docs.kentico.com/api/digital-marketing/contact-groups).
/// </summary>
public sealed class ContactGroupCatalog : IContactGroupCatalog
{
    /// <summary>What an unscoped rule shows in the listing.</summary>
    public const string Everyone = "Everyone";

    private readonly IInfoProvider<ContactGroupInfo> groups;

    /// <summary>Initializes a new instance of the <see cref="ContactGroupCatalog"/> class.</summary>
    /// <param name="groups">Provider of contact groups.</param>
    public ContactGroupCatalog(IInfoProvider<ContactGroupInfo> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        this.groups = groups;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContactGroupOption>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rows = await groups.Get()
            .OrderBy(nameof(ContactGroupInfo.ContactGroupDisplayName))
            .GetEnumerableTypedResultAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new ContactGroupOption(row.ContactGroupName, row.ContactGroupDisplayName))];
    }

    /// <inheritdoc />
    public string Label(string? codeName) =>
        string.IsNullOrWhiteSpace(codeName)
            ? Everyone
            : groups.Get(codeName)?.ContactGroupDisplayName ?? codeName;
}
