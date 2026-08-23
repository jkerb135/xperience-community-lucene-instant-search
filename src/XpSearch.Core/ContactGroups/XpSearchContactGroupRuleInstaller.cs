using CMS.DataEngine;
using CMS.MacroEngine;

namespace XpSearch.Core.ContactGroups;

/// <summary>
/// Creates the three search contact group conditions on startup, so they appear in the condition
/// picker of <em>Digital marketing -> Contact groups</em>.
/// </summary>
/// <remarks>
/// Contact group conditions are <c>MacroRuleInfo</c> objects with
/// <see cref="MacroRuleUsageLocation.ContactGroupCondition"/>; the administration has no UI for
/// creating them, so they are written through the generic provider API. Of a rule that already
/// exists only the text, the macro and the parameters are rewritten - the enabled flag is left
/// alone, so a marketer who hid a rule keeps that decision across restarts (the same policy
/// <c>XpSearchActivityTypeInstaller</c> uses for activity types).
/// </remarks>
public sealed class XpSearchContactGroupRuleInstaller
{
    private readonly IInfoProvider<MacroRuleInfo> rules;
    private readonly IInfoProvider<MacroRuleCategoryInfo> categories;
    private readonly IInfoProvider<MacroRuleMacroRuleCategoryInfo> ruleCategories;

    /// <summary>Initializes a new instance of the <see cref="XpSearchContactGroupRuleInstaller"/> class.</summary>
    /// <param name="rules">Provider of macro rules.</param>
    /// <param name="categories">Provider of macro rule categories.</param>
    /// <param name="ruleCategories">Provider of the rule to category bindings.</param>
    public XpSearchContactGroupRuleInstaller(
        IInfoProvider<MacroRuleInfo> rules,
        IInfoProvider<MacroRuleCategoryInfo> categories,
        IInfoProvider<MacroRuleMacroRuleCategoryInfo> ruleCategories)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(ruleCategories);

        this.rules = rules;
        this.categories = categories;
        this.ruleCategories = ruleCategories;
    }

    /// <summary>
    /// Creates the missing rules and refreshes the ones that already exist. Running it again on an
    /// unchanged database writes nothing.
    /// </summary>
    public void Install()
    {
        var category = categories.Get(XpSearchContactGroupRules.CategoryName);

        foreach (var rule in XpSearchContactGroupRules.All)
        {
            var info = Upsert(rule);

            if (category is not null)
            {
                LinkToCategory(info, category);
            }
        }
    }

    private MacroRuleInfo Upsert(XpSearchContactGroupRule rule)
    {
        var info = rules.Get(rule.Name);

        if (info is null)
        {
            info = new MacroRuleInfo
            {
                MacroRuleName = rule.Name,
                MacroRuleEnabled = true
            };
        }

        info.MacroRuleDisplayName = rule.DisplayName;
        info.MacroRuleText = rule.Text;
        info.MacroRuleCondition = rule.Condition;
        info.MacroRuleIsCustom = true;
        info.MacroRuleUsageLocation = MacroRuleUsageLocation.ContactGroupCondition;
        info.MacroRuleParameters = rule.Parameters;

        if (info.HasChanged)
        {
            rules.Set(info);
        }

        return info;
    }

    private void LinkToCategory(MacroRuleInfo rule, MacroRuleCategoryInfo category)
    {
        var existing = ruleCategories.Get()
            .WhereEquals(nameof(MacroRuleMacroRuleCategoryInfo.MacroRuleID), rule.MacroRuleID)
            .WhereEquals(nameof(MacroRuleMacroRuleCategoryInfo.MacroRuleCategoryID), category.MacroRuleCategoryID)
            .TopN(1)
            .FirstOrDefault();

        if (existing is null)
        {
            ruleCategories.Set(new MacroRuleMacroRuleCategoryInfo
            {
                MacroRuleID = rule.MacroRuleID,
                MacroRuleCategoryID = category.MacroRuleCategoryID
            });
        }
    }
}
