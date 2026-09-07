using System.Globalization;
using System.Reflection;
using System.Text;

using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The attribute tables in <c>docs/guides/razor-tag-helpers.md</c> are generated from the tag helper
/// classes, not typed by hand: this test reflects over every <see cref="XpSearchMountTagHelper{TOptions}"/>
/// in the package and fails with the markdown the guide should carry. The same drift guard the
/// repository uses for the contract types and for the dropdown sample.
/// </summary>
[TestFixture]
internal sealed class TagHelperDocsTests
{
    private static readonly string GuidePath = Guide();

    /// <summary>One row of a widget's attribute table: what the guide must say before the notes column.</summary>
    private sealed record Row(string Attribute, string Type, string Default)
    {
        public override string ToString() => $"| `{Attribute}` | {Type} | {Default} |";
    }

    [Test]
    public void The_guide_documents_every_widget_tag()
    {
        string guide = File.ReadAllText(GuidePath);
        var documented = Tags()
            .Where(tag => guide.Contains(Heading(tag.Tag), StringComparison.Ordinal))
            .ToList();

        Assert.That(
            documented.Select(tag => tag.Tag),
            Is.EquivalentTo(Tags().Select(tag => tag.Tag)),
            $"every widget tag needs a `### \\`<tag>\\`` section in {GuidePath}");
    }

    [Test]
    public void Every_attribute_table_matches_the_tag_helper_it_documents()
    {
        string[] lines = File.ReadAllLines(GuidePath);
        var drift = new StringBuilder();

        foreach (var (tag, type) in Tags())
        {
            var expected = Rows(type);
            var actual = Table(lines, Heading(tag));

            if (!expected.SequenceEqual(actual))
            {
                drift.Append(CultureInfo.InvariantCulture, $"\n{Heading(tag)}\n");
                drift.Append("expected:\n").AppendJoin('\n', expected).Append('\n');
                drift.Append("found:\n").AppendJoin('\n', actual).Append('\n');
            }
        }

        Assert.That(
            drift.ToString(),
            Is.Empty,
            $"{GuidePath} has drifted from the tag helpers. Attribute, type and default are generated; "
                + "only the last (notes) column is written by hand.");
    }

    /// <summary>Every shipped widget tag, as its element name and its tag helper type.</summary>
    private static IEnumerable<(string Tag, Type Type)> Tags() =>
        typeof(XpSearchMountTagHelper<>).Assembly
            .GetExportedTypes()
            .Where(type => !type.IsAbstract && Base(type) is not null)
            .Select(type => (type.GetCustomAttribute<HtmlTargetElementAttribute>()!.Tag, type))
            .OrderBy(pair => pair.Tag, StringComparer.Ordinal);

    private static string Heading(string tag) => $"### `<{tag}>`";

    /// <summary>The rows the guide should carry for one tag helper: its own attributes, in declaration order.</summary>
    private static IReadOnlyList<Row> Rows(Type tagHelper)
    {
        var options = Base(tagHelper)!.GetGenericArguments()[0];
        object defaults = Activator.CreateInstance(options)!;

        return tagHelper
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => (property, name: property.GetCustomAttribute<HtmlAttributeNameAttribute>()?.Name))
            .Where(pair => pair.name is not null)
            .Select(pair => new Row(
                pair.name!,
                TypeName(pair.property.PropertyType),
                Default(options.GetProperty(pair.property.Name)?.GetValue(defaults))))
            .ToList();
    }

    /// <summary>The three generated cells of every table row under a heading, in order.</summary>
    private static IReadOnlyList<Row> Table(string[] lines, string heading)
    {
        // The heading may carry a few words after the tag, so it is matched by its start.
        int at = Array.FindIndex(lines, line => line.StartsWith(heading, StringComparison.Ordinal));
        if (at < 0)
        {
            return [];
        }

        var rows = new List<Row>();
        bool inTable = false;

        for (int line = at + 1; line < lines.Length && !lines[line].StartsWith("##", StringComparison.Ordinal); line++)
        {
            if (!lines[line].StartsWith("| `", StringComparison.Ordinal))
            {
                // The table ends at the first line after it that is not one of its rows.
                if (inTable && !lines[line].StartsWith('|'))
                {
                    break;
                }

                continue;
            }

            inTable = true;
            string[] cells = lines[line].Split('|', StringSplitOptions.TrimEntries);
            rows.Add(new Row(cells[1].Trim('`'), cells[2], cells[3]));
        }

        return rows;
    }

    private static Type? Base(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(XpSearchMountTagHelper<>))
            {
                return current;
            }
        }

        return null;
    }

    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying switch
        {
            _ when underlying == typeof(string) => "string",
            _ when underlying == typeof(bool) => "bool",
            _ when underlying == typeof(int) => "int",
            _ when underlying == typeof(decimal) => "decimal",
            _ when typeof(IEnumerable<string>).IsAssignableFrom(underlying) => "string list",
            _ => "object"
        };
    }

    private static string Default(object? value) => value switch
    {
        null => "–",
        string text when text.Length == 0 => "–",
        string text => $"`{text}`",
        bool flag => flag ? "`true`" : "`false`",
        System.Collections.ICollection { Count: 0 } => "–",
        IFormattable number => $"`{number.ToString(null, CultureInfo.InvariantCulture)}`",
        _ => "–"
    };

    /// <summary>The guide, found from the test assembly's location - the repository has no solution file.</summary>
    private static string Guide()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "guides")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "docs/guides was not found above the test assembly");

        return Path.Combine(directory!.FullName, "docs", "guides", "razor-tag-helpers.md");
    }
}
