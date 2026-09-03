using System.Collections;
using System.Reflection;

using Kentico.Xperience.Admin.Base;

using NUnit.Framework;

using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Admin.UIPages.Experiments;
using XpSearch.Admin.UIPages.QueryTester;
using XpSearch.Admin.UIPages.RuleBuilder;

namespace XpSearch.Admin.Tests;

/// <summary>
/// Answers "command not found" (CD-1) with the platform's own answer: these tests read the commands
/// out of Kentico's real <c>UITree</c> singleton, built by <c>UITree.BuildPages</c> from the same
/// discoverable assemblies the host uses, and ask it the question
/// <c>PageCommandInvoker.TryGetCommand</c> asks - <c>node.Commands.TryGetValue(name)</c>, an ordinal
/// dictionary. See docs/adr/0027-page-command-discovery.md for the decompiled rule; in short,
/// <c>UITree.GetPageCommands</c> reflects with <c>BindingFlags.Instance | BindingFlags.Public</c>,
/// so inherited and re-annotated overridden commands are found, and a name registered twice on one
/// page throws while the tree is being built rather than going missing.
/// </summary>
[TestFixture]
internal sealed class PageCommandDiscoveryTests
{
    /// <summary>
    /// Every command name our React client sends (<c>usePageCommand</c> in
    /// src/XpSearch.Admin/Client/src) or a page configuration wires to a header/table action, against
    /// the page that has to answer it. This is the list the owner clicks through on the host.
    /// </summary>
    private static readonly (Type Page, string[] Commands)[] Invoked =
    [
        (typeof(FieldWeightListing), ["Delete", "TogglePopularityBoost"]),
        (typeof(SynonymListing), ["Delete", "ToggleTypoTolerance"]),
        (typeof(StopwordListing), ["Delete"]),
        (typeof(RuleListing), ["Delete"]),
        (typeof(ApiKeyListing), ["Delete"]),
        (typeof(VariantFieldWeightListing), ["DeleteRow"]),
        (typeof(VariantSynonymListing), ["DeleteRow"]),
        (typeof(VariantStopwordListing), ["DeleteRow"]),
        (typeof(VariantRuleListing), ["DeleteRow"]),
        (typeof(PopularitySuggestionListing), ["Approve", "Dismiss"]),
        (typeof(SynonymSuggestionListing), ["Approve", "Dismiss"]),
        (typeof(RuleEdit), ["Load", "Save", "Cancel", "Delete", "SearchItems", "GetAttributeValues"]),
        (typeof(RuleCreate), ["Load", "Save", "Cancel", "Delete", "SearchItems", "GetAttributeValues"]),
        (typeof(VariantRuleEdit), ["Load", "Save", "Cancel", "Delete", "SearchItems", "GetAttributeValues"]),
        (typeof(VariantRuleCreate), ["Load", "Save", "Cancel", "Delete", "SearchItems", "GetAttributeValues"]),
        (typeof(ZeroResultRuleCreatePage), ["Load", "Save", "Cancel", "Delete", "SearchItems", "GetAttributeValues"]),
        (typeof(AnalyticsDashboardPage), ["Load", "CreateRule"]),
        (typeof(ExperimentDetailPage), ["Load", "SetSplit", "Start", "Conclude"]),
        (typeof(IndexStatusPage), ["Load", "Rebuild"]),
        (typeof(QueryTesterPage), ["Run", "OpenStatus"]),

        // AR-2: the per-index settings form submits through ModelEditPage's own command.
        (typeof(SearchSettingsPage), ["Submit"]),
    ];

    /// <summary>Every page of ours the platform has registered in the UI tree.</summary>
    private static IEnumerable<Type> RegisteredPages =>
        Discovered.Keys.Where(page => page.Assembly == typeof(SearchTuningApplication).Assembly);

    private static readonly IReadOnlyDictionary<Type, IReadOnlyCollection<string>> Discovered = ReadUITree();

    [TestCaseSource(nameof(Invoked))]
    public void EveryCommandTheClientInvokesResolvesOnItsPage((Type Page, string[] Commands) invocation)
    {
        Assert.That(Discovered.Keys, Does.Contain(invocation.Page), $"{invocation.Page.Name} is not a registered UI page");

        Assert.That(
            Discovered[invocation.Page],
            Is.SupersetOf(invocation.Commands),
            $"a click on {invocation.Page.Name} would answer 'command not found'");
    }

    /// <summary>
    /// The assembly-wide guard: whatever shape a page command is written in - plain method, inherited
    /// from an abstract base, an override that re-annotates - the platform has to end up with it.
    /// A new page that breaks the rule fails here rather than on the host.
    /// </summary>
    [TestCaseSource(nameof(RegisteredPages))]
    public void EveryAnnotatedMethodOnAPageIsRegisteredAsACommand(Type page)
    {
        // UITree.GetPageCommands, verbatim: public instance methods, attribute looked up with
        // inheritance, name from the attribute or the method.
        var annotated = page
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.GetCustomAttribute<PageCommandAttribute>() is { } command
                ? command.CommandName ?? method.Name
                : null)
            .OfType<string>()
            .ToList();

        Assert.That(annotated, Is.Unique, $"{page.Name} registers a command name twice - the UI tree refuses to build");
        Assert.That(Discovered[page], Is.EquivalentTo(annotated));
    }

    /// <summary>
    /// Reads Kentico's own singleton UI tree - the one the host serves commands from - as
    /// page type to command names. The tree types are internal, hence the reflection.
    /// </summary>
    private static IReadOnlyDictionary<Type, IReadOnlyCollection<string>> ReadUITree()
    {
        var treeType = typeof(PageCommandAttribute).Assembly.GetType("Kentico.Xperience.Admin.Base.UITree")!;
        object tree = treeType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var nodes = (IEnumerable)treeType.GetProperty("Nodes")!.GetValue(tree)!;

        var commands = new Dictionary<Type, IReadOnlyCollection<string>>();

        foreach (object entry in nodes)
        {
            var pair = entry.GetType();
            var page = (Type)pair.GetProperty("Key")!.GetValue(entry)!;
            object node = pair.GetProperty("Value")!.GetValue(entry)!;

            commands[page] = ((IEnumerable)node.GetType().GetProperty("Commands")!.GetValue(node)!)
                .Cast<object>()
                .Select(command => (string)command.GetType().GetProperty("Key")!.GetValue(command)!)
                .ToList();
        }

        return commands;
    }
}
