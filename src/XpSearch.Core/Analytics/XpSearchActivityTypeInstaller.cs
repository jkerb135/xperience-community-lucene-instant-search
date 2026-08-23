using CMS.Activities;
using CMS.DataEngine;

namespace XpSearch.Core.Analytics;

/// <summary>
/// Creates the four search activity types (spec §9.1) on startup, so a project does not have to add
/// them by hand in <em>Contact management → Activity types</em>.
/// </summary>
/// <remarks>
/// The documentation only describes the administration route for creating custom activity types
/// (https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/set-up-activities/custom-activities);
/// this creates the same <c>ActivityTypeInfo</c> objects through the generic provider API
/// (https://docs.kentico.com/documentation/developers-and-admins/api/database-table-api), with the
/// same fields the administration form fills in. Of a type that already exists only the
/// <c>ActivityTypeDescription</c> is rewritten - the description states what this library puts in the
/// activity's fields and would otherwise go stale after an upgrade. The enabled flag and the display
/// name are never touched, so a marketer who disabled or renamed a type keeps that decision across
/// restarts.
/// </remarks>
public sealed class XpSearchActivityTypeInstaller
{
    private readonly IInfoProvider<ActivityTypeInfo> provider;

    /// <summary>Initializes a new instance of the <see cref="XpSearchActivityTypeInstaller"/> class.</summary>
    /// <param name="provider">Provider of activity types.</param>
    public XpSearchActivityTypeInstaller(IInfoProvider<ActivityTypeInfo> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
    }

    /// <summary>
    /// Creates the missing activity types and refreshes the description of the ones that already
    /// exist. Running it again on an unchanged database writes nothing.
    /// </summary>
    public void Install()
    {
        foreach (var type in XpSearchActivityTypes.All)
        {
            var existing = provider.Get()
                .WhereEquals(nameof(ActivityTypeInfo.ActivityTypeName), type.CodeName)
                .TopN(1)
                .FirstOrDefault();

            if (existing is not null)
            {
                if (!string.Equals(existing.ActivityTypeDescription, type.Description, StringComparison.Ordinal))
                {
                    existing.ActivityTypeDescription = type.Description;

                    provider.Set(existing);
                }

                continue;
            }

            provider.Set(new ActivityTypeInfo
            {
                ActivityTypeName = type.CodeName,
                ActivityTypeDisplayName = type.DisplayName,
                ActivityTypeDescription = type.Description,
                ActivityTypeEnabled = true,
                ActivityTypeIsCustom = true
            });
        }
    }
}
