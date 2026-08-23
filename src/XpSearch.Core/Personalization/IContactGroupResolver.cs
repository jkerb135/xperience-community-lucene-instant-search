using CMS.ContactManagement;
using CMS.DataEngine;
using CMS.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace XpSearch.Core.Personalization;

/// <summary>Shared instances of the "this visitor is in no contact group" answer.</summary>
public static class ContactGroupSets
{
    /// <summary>Gets the empty set: no contact, no consent, or a visitor in no group at all.</summary>
    /// <remarks>Code names are compared case-insensitively, as Xperience treats them.</remarks>
    public static IReadOnlySet<string> None { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a set of contact group code names with the comparison rules the pipeline uses.</summary>
    /// <param name="codeNames">The code names; blanks are dropped.</param>
    /// <returns>The set.</returns>
    public static IReadOnlySet<string> Of(IEnumerable<string?> codeNames)
    {
        ArgumentNullException.ThrowIfNull(codeNames);

        return new HashSet<string>(
            codeNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Answers "which contact groups is the visitor of this request in?", so a relevance rule can be
/// scoped to one of them (ADR-0021).
/// </summary>
public interface IContactGroupResolver
{
    /// <summary>Gets the contact groups of the current visitor.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Their code names, or an empty set when there is no contact or no consent.</returns>
    Task<IReadOnlySet<string>> GetContactGroupsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The production resolver: the current contact's group memberships, read once per HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// Consent gate: the visitor's contact is only looked at when their cookie level is <em>Visitor</em>
/// or higher, the same <see cref="ICurrentCookieLevelProvider"/> check
/// <c>XpSearch.Core.Analytics.SearchActivityLogger</c> makes before logging an activity
/// (https://docs.kentico.com/documentation/developers-and-admins/data-protection/consent-development).
/// </para>
/// <para>
/// The contact is read with <see cref="ICurrentContactProvider.GetExistingContact"/> rather than
/// <c>GetCurrentContact</c> so that searching never creates an anonymous contact
/// (https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/contact-recognition-logic).
/// Membership is one query over <see cref="ContactGroupMemberInfo"/> joined to the group code names
/// (https://docs.kentico.com/api/digital-marketing/contact-groups); the answer is memoized on
/// <see cref="HttpContext.Items"/> and never beyond the request, because group membership changes
/// under the visitor's feet.
/// </para>
/// <para>
/// Personalisation is best-effort: any failure - a missing request state, online marketing switched
/// off, no license - answers "no groups" and is logged at Debug, so a search never fails because a
/// contact could not be recognized.
/// </para>
/// </remarks>
public sealed class ContactGroupResolver : IContactGroupResolver
{
    private static readonly object ItemsKey = new();

    private readonly ICurrentContactProvider contacts;
    private readonly ICurrentCookieLevelProvider cookieLevelProvider;
    private readonly IInfoProvider<ContactGroupMemberInfo> members;
    private readonly IInfoProvider<ContactGroupInfo> groups;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ILogger<ContactGroupResolver> logger;

    /// <summary>Initializes a new instance of the <see cref="ContactGroupResolver"/> class.</summary>
    /// <param name="contacts">Recognizes the visitor's contact.</param>
    /// <param name="cookieLevelProvider">Supplies the current visitor's cookie level.</param>
    /// <param name="members">Provider of contact group membership bindings.</param>
    /// <param name="groups">Provider of contact groups, used to resolve their code names.</param>
    /// <param name="httpContextAccessor">Gives access to the request the answer is memoized on.</param>
    /// <param name="logger">Logger.</param>
    public ContactGroupResolver(
        ICurrentContactProvider contacts,
        ICurrentCookieLevelProvider cookieLevelProvider,
        IInfoProvider<ContactGroupMemberInfo> members,
        IInfoProvider<ContactGroupInfo> groups,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ContactGroupResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(cookieLevelProvider);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        this.contacts = contacts;
        this.cookieLevelProvider = cookieLevelProvider;
        this.members = members;
        this.groups = groups;
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetContactGroupsAsync(CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext;

        if (request is null)
        {
            return ContactGroupSets.None;
        }

        if (request.Items.TryGetValue(ItemsKey, out object? memoized) && memoized is IReadOnlySet<string> already)
        {
            return already;
        }

        var resolved = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        request.Items[ItemsKey] = resolved;

        return resolved;
    }

    private async Task<IReadOnlySet<string>> ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cookieLevelProvider.GetCurrentCookieLevel() < Kentico.Web.Mvc.CookieLevel.Visitor.Level)
            {
                logger.LogDebug("Not resolving contact groups: the visitor has not consented to tracking.");

                return ContactGroupSets.None;
            }

            if (contacts.GetExistingContact() is not { ContactID: > 0 } contact)
            {
                return ContactGroupSets.None;
            }

            var memberships = members.Get()
                .WhereEquals(nameof(ContactGroupMemberInfo.ContactGroupMemberRelatedID), contact.ContactID)
                .WhereEquals(nameof(ContactGroupMemberInfo.ContactGroupMemberType), ContactGroupMemberTypeEnum.Contact)
                .Column(nameof(ContactGroupMemberInfo.ContactGroupMemberContactGroupID));

            var codeNames = await groups.Get()
                .WhereIn(nameof(ContactGroupInfo.ContactGroupID), memberships)
                .Column(nameof(ContactGroupInfo.ContactGroupName))
                .GetListResultAsync<string>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ContactGroupSets.Of(codeNames);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The visitor's contact groups could not be resolved; no group-scoped rule will apply.");

            return ContactGroupSets.None;
        }
    }
}
