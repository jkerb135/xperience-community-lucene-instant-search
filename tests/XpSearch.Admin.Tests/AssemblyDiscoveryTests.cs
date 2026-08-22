using System.Reflection;

using CMS;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Admin;

using NUnit.Framework;

using XpSearch.Admin.Forms;

namespace XpSearch.Admin.Tests;

/// <summary>
/// The admin assembly registers its form component configurator, its tuning object types and the
/// Search tuning application's UI pages by attribute, and Xperience only scans an assembly marked
/// discoverable
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/integrate-custom-code).
/// </summary>
[TestFixture]
internal sealed class AssemblyDiscoveryTests
{
    [Test]
    public void Admin_assembly_is_discoverable()
    {
        var assembly = typeof(FacetAttributeConfigurator).Assembly;

        Assert.That(
            assembly.GetCustomAttribute<AssemblyDiscoverableAttribute>(),
            Is.Not.Null,
            "XpSearch.Admin must carry CMS.AssemblyDiscoverableAttribute or Xperience ignores its registration attributes.");
    }

    /// <summary>
    /// Xperience refuses a main-content page whose parent uses the EDIT template — and the refusal
    /// takes down the whole admin UI tree, not just that page, so every admin page in the host
    /// becomes unreachable (docs/internal/host-pass-hw7-2026-08-22.md §6.1).
    /// </summary>
    [Test]
    public void No_page_renders_in_the_main_content_of_an_EDIT_template_parent()
    {
        var templates = typeof(FacetAttributeConfigurator).Assembly.GetCustomAttributes<UIPageAttribute>()
            .Concat(typeof(IndexListingPage).Assembly.GetCustomAttributes<UIPageAttribute>())
            .ToDictionary(page => page.Type, page => page.TemplateName);

        var offenders = typeof(FacetAttributeConfigurator).Assembly.GetCustomAttributes<UIPageAttribute>()
            .Where(page => page.ParentType is not null && templates.GetValueOrDefault(page.ParentType) == TemplateNames.EDIT)
            .Where(page => page.Type.GetCustomAttribute<UIPageLocationAttribute>() is null)
            .Select(page => page.Type.Name);

        Assert.That(offenders, Is.Empty, "such a page must declare UIPageLocation SidePanel or Dialog, or hang elsewhere");
    }
}
