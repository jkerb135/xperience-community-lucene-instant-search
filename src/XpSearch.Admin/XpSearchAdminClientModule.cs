using CMS;

using Kentico.Xperience.Admin.Base;

using XpSearch.Admin;

[assembly: RegisterModule(typeof(XpSearchAdminClientModule))]

namespace XpSearch.Admin;

/// <summary>
/// Makes the package's admin client module - the query tester and analytics dashboard templates -
/// available to the administration's React application.
/// </summary>
/// <remarks>
/// The organization and project names must match <c>orgName</c>/<c>projectName</c> in
/// <c>Client/webpack.config.js</c>, <c>AdminOrgName</c>/<c>ProjectName</c> in
/// <c>XpSearch.Admin.csproj</c> and the <c>@xperience-community/xperience-search/...</c> template names in
/// the <c>UIPage</c> registrations
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/prepare-your-environment-for-admin-development).
/// </remarks>
internal class XpSearchAdminClientModule : AdminModule
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchAdminClientModule"/> class.</summary>
    public XpSearchAdminClientModule()
        : base("XpSearch.Admin.Client")
    {
    }

    /// <inheritdoc />
    protected override void OnInit()
    {
        base.OnInit();

        RegisterClientModule("xperience-community", "xperience-search");
    }
}
