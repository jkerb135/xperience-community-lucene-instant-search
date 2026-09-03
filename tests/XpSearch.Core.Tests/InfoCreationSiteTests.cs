using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using CMS.FormEngine;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Guards the defect class behind RK-2: a Kentico Info object only writes the fields that were SET,
/// so a required (not <c>allowEmpty</c>) column left out of an object initializer inserts NULL and
/// the write fails on the host. Constructing an Info needs Kentico's IoC container, so the rule is
/// checked against the source of the creation sites instead.
/// </summary>
[TestFixture]
public class InfoCreationSiteTests
{
    /// <summary>Every creation site in <c>XpSearch.Core</c> sets every required column of its form.</summary>
    [Test]
    public void EveryCreationSiteSetsEveryRequiredColumn()
    {
        var forms = new Dictionary<string, FormInfo>(StringComparer.Ordinal)
        {
            [nameof(XpSearchQueryLogInfo)] = XpSearchAnalyticsModuleInstaller.QueryLogForm(),
            [nameof(XpSearchPopularityIndexInfo)] = XpSearchAnalyticsModuleInstaller.PopularityIndexForm(),
            [nameof(XpSearchPopularityScoreInfo)] = XpSearchAnalyticsModuleInstaller.PopularityScoreForm(),
            [nameof(XpSearchPopularitySuggestionInfo)] = XpSearchAnalyticsModuleInstaller.PopularitySuggestionForm(),
            [nameof(XpSearchSynonymSuggestionInfo)] = XpSearchAnalyticsModuleInstaller.SynonymSuggestionForm(),
            [nameof(Core.Fuzzy.XpSearchFuzzyIndexInfo)] = XpSearchAnalyticsModuleInstaller.FuzzyIndexForm(),
            [nameof(Core.Options.XpSearchSettingsInfo)] = XpSearchAnalyticsModuleInstaller.SettingsForm(),
        };

        string root = SourceRoot();

        Assume.That(Directory.Exists(root), $"the sources are not next to the test assembly's source ({root})");

        var missing = new List<string>();
        int sites = 0;

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);

            foreach (var site in Regex.Matches(source, @"new (XpSearch\w+Info)\s*\{").Cast<Match>())
            {
                if (!forms.TryGetValue(site.Groups[1].Value, out var form))
                {
                    continue;
                }

                sites++;

                string initializer = Initializer(source, (site.Index + site.Length) - 1);

                missing.AddRange(form.GetFields(true, true)
                    .Where(field => !field.AllowEmpty && !field.PrimaryKey)
                    .Select(field => field.Name)
                    .Where(name => !initializer.Contains(name + " =", StringComparison.Ordinal))
                    .Select(name => $"{Path.GetFileName(file)}: {site.Groups[1].Value}.{name}"));
            }
        }

        Expect.Multiple(() =>
        {
            Assert.That(sites, Is.GreaterThan(0), "the scan found no creation sites at all - it stopped checking anything");
            Assert.That(missing, Is.Empty, "required columns left unset by an object initializer insert NULL");
        });
    }

    /// <summary>Reads the object initializer that starts at <paramref name="open"/>, braces balanced.</summary>
    private static string Initializer(string source, int open)
    {
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        return source[open..];
    }

    /// <summary>The <c>src/XpSearch.Core</c> directory, found relative to this file.</summary>
    private static string SourceRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "src", "XpSearch.Core"));
}
