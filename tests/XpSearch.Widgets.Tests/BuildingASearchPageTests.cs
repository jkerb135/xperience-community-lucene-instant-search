using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// <c>docs/guides/building-a-search-page.md</c> shows the same results page in Razor that
/// <c>docs/guides/widget-reference.md</c> shows in plain HTML. This runs the guide's Razor tags the
/// way Razor would and asserts the mounts it produces are the ones the plain-HTML recipe hand-writes:
/// same widgets in the same order, same instance, and no config value that contradicts it.
/// </summary>
/// <remarks>
/// The tag elements are read out of the guide itself, so an edit to either page that breaks the
/// correspondence fails here rather than on a customer's page.
/// </remarks>
[TestFixture]
internal sealed class BuildingASearchPageTests
{
    private const string Index = "site-content";

    /// <summary>
    /// Config keys the plain-HTML recipe sets that no C# option can express today. Listed per widget
    /// so adding the option to a tag helper makes this test demand the list be shortened.
    /// </summary>
    private static readonly Dictionary<string, string[]> JavaScriptOnlyConfig = new(StringComparer.Ordinal)
    {
        ["pagination"] = ["padding", "showFirst", "showLast"],
        ["activeFilters"] = ["attributeLabels"]
    };

    /// <summary>The same, for the merged instance options of the page.</summary>
    private static readonly string[] JavaScriptOnlyInstanceConfig = ["searchOnInitialLoad"];

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index);

    /// <summary>One mount, however it was written: what the bootstrap reads off the element.</summary>
    private sealed record Mount(string Widget, string Instance, JsonElement Config, JsonElement? InstanceConfig);

    [Test]
    public void The_Razor_page_in_the_guide_builds_the_plain_HTML_recipes_mounts()
    {
        var expected = PlainHtmlRecipe();
        var actual = RazorPage();

        Assert.That(expected, Is.Not.Empty, "the plain-HTML recipe was not found in widget-reference.md");

        Expect.Multiple(() =>
        {
            Assert.That(
                actual.Select(mount => mount.Widget),
                Is.EqualTo(expected.Select(mount => mount.Widget)),
                "the Razor page must place the same widgets in the same order as the plain-HTML recipe");
            Assert.That(
                actual.Select(mount => mount.Instance),
                Is.EqualTo(expected.Select(mount => mount.Instance)),
                "the widgets must join the same search instance");
        });

        foreach (var (want, got) in expected.Zip(actual))
        {
            Assert.That(
                Missing(want.Config, got.Config),
                Is.EqualTo(JavaScriptOnlyConfig.TryGetValue(want.Widget, out string[]? known) ? known : []),
                $"{want.Widget}: the Razor tag's config differs from the plain-HTML recipe's."
                    + $" recipe: {want.Config} razor: {got.Config}");
        }

        // Instance options are merged across the group by mountAll(), so they are compared per page,
        // not per mount: which tag carries them is a free choice.
        Assert.That(
            Missing(Merged(expected), Merged(actual)),
            Is.EqualTo(JavaScriptOnlyInstanceConfig),
            "the Razor page's instance options differ from the plain-HTML recipe's");
    }

    /// <summary>The keys of <paramref name="want"/> the <paramref name="got"/> object does not carry with the same value.</summary>
    private static string[] Missing(JsonElement want, JsonElement got) =>
        want.EnumerateObject()
            .Where(property => !got.TryGetProperty(property.Name, out var value) || !Same(property.Value, value))
            .Select(property => property.Name)
            .ToArray();

    /// <summary>JSON equality that ignores the order of an object's keys, which no consumer sees.</summary>
    private static bool Same(JsonElement left, JsonElement right) => left.ValueKind switch
    {
        JsonValueKind.Object => right.ValueKind == JsonValueKind.Object
            && left.EnumerateObject().Count() == right.EnumerateObject().Count()
            && left.EnumerateObject().All(property =>
                right.TryGetProperty(property.Name, out var value) && Same(property.Value, value)),
        JsonValueKind.Array => right.ValueKind == JsonValueKind.Array
            && left.EnumerateArray().Count() == right.EnumerateArray().Count()
            && left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Same(pair.First, pair.Second)),
        JsonValueKind.Number => right.ValueKind == JsonValueKind.Number
            && left.GetDecimal() == right.GetDecimal(),
        // Compared decoded: the renderer escapes non-ASCII characters, a hand-written page does not.
        JsonValueKind.String => right.ValueKind == JsonValueKind.String
            && string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
        _ => left.ValueKind == right.ValueKind && left.GetRawText() == right.GetRawText()
    };

    /// <summary>The page's instance options as <c>mountAll()</c> merges them: the first definition of a key wins.</summary>
    private static JsonElement Merged(IEnumerable<Mount> mounts)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var mount in mounts.Where(mount => mount.InstanceConfig is not null))
        {
            foreach (var property in mount.InstanceConfig!.Value.EnumerateObject())
            {
                _ = merged.TryAdd(property.Name, property.Value);
            }
        }

        return JsonSerializer.SerializeToElement(merged);
    }

    /// <summary>The mounts the canonical plain-HTML recipe hand-writes, in document order.</summary>
    private static IReadOnlyList<Mount> PlainHtmlRecipe()
    {
        string html = Block(Guides("widget-reference.md"), "## Composing the results page", "html");

        return Regex.Matches(html, "<div class=\"xps-mount\"([^>]*)>", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups[1].Value)
            .Select(attributes => new Mount(
                Quoted(attributes, "data-xps-widget") ?? string.Empty,
                Quoted(attributes, "data-xps-instance") ?? XpSearchWidgetConstants.DefaultInstanceId,
                Parse(Quoted(attributes, "data-xps-config")) ?? default,
                Parse(Quoted(attributes, "data-xps-instance-config"))))
            .ToList();
    }

    /// <summary>The mounts the guide's Razor page produces, by running its tags the way Razor does.</summary>
    private IReadOnlyList<Mount> RazorPage()
    {
        string page = Block(Guides("building-a-search-page.md"), "### The same page in Razor", "cshtml");
        var items = Scope(page);
        var mounts = new List<Mount>();

        foreach (var element in Elements(page).Where(element => Helpers.ContainsKey(element.Tag)))
        {
            var helper = Activate(element.Tag);

            foreach (var (name, value) in element.Attributes)
            {
                var property = helper.GetType()
                    .GetProperties()
                    .SingleOrDefault(candidate =>
                        candidate.GetCustomAttribute<HtmlAttributeNameAttribute>()?.Name == name);

                Assert.That(property, Is.Not.Null, $"<{element.Tag}> has no attribute '{name}'");
                property!.SetValue(helper, Convert(property.PropertyType, value));
            }

            string markup = Render(helper, element.Tag, items);

            mounts.Add(new Mount(
                Rendered.Attribute(markup, "data-xps-widget"),
                Rendered.Attribute(markup, "data-xps-instance"),
                Rendered.Json(markup, "data-xps-config"),
                markup.Contains("data-xps-instance-config", StringComparison.Ordinal)
                    ? Rendered.Json(markup, "data-xps-instance-config")
                    : null));
        }

        return mounts;
    }

    /// <summary>The items the page's <c>&lt;xps-search&gt;</c> publishes to the tags inside it.</summary>
    private static IDictionary<object, object> Scope(string page)
    {
        var element = Elements(page).SingleOrDefault(candidate => candidate.Tag == "xps-search");
        var search = new XpSearchTagHelper();

        foreach (var (name, value) in element.Attributes ?? [])
        {
            var property = typeof(XpSearchTagHelper)
                .GetProperties()
                .Single(candidate => candidate.GetCustomAttribute<HtmlAttributeNameAttribute>()?.Name == name);
            property.SetValue(search, Convert(property.PropertyType, value));
        }

        var items = new Dictionary<object, object>();
        search.Process(
            new TagHelperContext([], items, "scope"),
            new TagHelperOutput("xps-search", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())));

        return items;
    }

    /// <summary>Every Xperience Search tag element in a Razor block, in document order, with its attributes.</summary>
    private static IEnumerable<(string Tag, IReadOnlyList<(string Name, string Value)> Attributes)> Elements(string page) =>
        Regex.Matches(
                page,
                "<(?<tag>xps-[a-z-]+)(?<attributes>(?:\\s+[a-z-]+=\"[^\"]*\")*)\\s*/?>",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(match => (
                match.Groups["tag"].Value,
                (IReadOnlyList<(string, string)>)Regex
                    .Matches(
                        match.Groups["attributes"].Value,
                        "(?<name>[a-z-]+)=\"(?<value>[^\"]*)\"",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(5))
                    .Select(attribute => (attribute.Groups["name"].Value, attribute.Groups["value"].Value))
                    .ToList()));

    private static object? Convert(Type type, string value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        return target switch
        {
            _ when target == typeof(string) => value,
            _ when target == typeof(bool) => bool.Parse(value),
            _ when target == typeof(int) => int.Parse(value, CultureInfo.InvariantCulture),
            _ when target == typeof(decimal) => decimal.Parse(value, CultureInfo.InvariantCulture),
            _ when typeof(IEnumerable<string>).IsAssignableFrom(target) =>
                value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => throw new NotSupportedException($"the guide's parser does not convert {target}")
        };
    }

    /// <summary>Every tag element a widget tag helper handles, by its element name.</summary>
    private static readonly IReadOnlyDictionary<string, Type> Helpers =
        typeof(XpSearchMountTagHelper<>).Assembly
            .GetExportedTypes()
            .Where(candidate => !candidate.IsAbstract && IsMountTagHelper(candidate))
            .ToDictionary(
                candidate => candidate.GetCustomAttribute<HtmlTargetElementAttribute>()!.Tag!,
                candidate => candidate,
                StringComparer.Ordinal);

    private object Activate(string tag)
    {
        var type = Helpers[tag];
        object?[] arguments = type.GetConstructors()[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType switch
            {
                _ when parameter.ParameterType == typeof(IXpSearchMountRenderer) => renderer,
                _ when parameter.ParameterType == typeof(IXpSearchIndexCatalog) => (object?)catalog,
                _ when parameter.ParameterType == typeof(IOptionsMonitor<XpSearchOptions>) => TagHelperTests.SearchOptions(),
                _ => null
            })
            .ToArray();

        return Activator.CreateInstance(type, arguments)!;
    }

    /// <summary>Runs one tag helper the way Razor would, whatever its options type is.</summary>
    private static string Render(object helper, string tag, IDictionary<object, object> items) =>
        (string)typeof(TagHelperTests)
            .GetMethod(nameof(TagHelperTests.Tag), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(OptionsType(helper.GetType()))
            .Invoke(null, [helper, tag, items])!;

    private static bool IsMountTagHelper(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(XpSearchMountTagHelper<>))
            {
                return true;
            }
        }

        return false;
    }

    private static Type OptionsType(Type tagHelper)
    {
        for (var current = tagHelper.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(XpSearchMountTagHelper<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"{tagHelper} is not a widget tag helper");
    }

    /// <summary>One attribute of a hand-written element. The JSON ones are single-quoted, the rest are not.</summary>
    private static string? Quoted(string attributes, string name)
    {
        foreach (char quote in "'\"")
        {
            var match = Regex.Match(
                attributes,
                $"{name}={quote}(?<value>[^{quote}]*){quote}",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            if (match.Success)
            {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>The first fenced code block of a language that follows a heading in a guide.</summary>
    private static string Block(string path, string heading, string language)
    {
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");
        int at = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"'{heading}' was not found in {path}");

        int open = text.IndexOf($"```{language}\n", at, StringComparison.Ordinal);
        Assert.That(open, Is.GreaterThanOrEqualTo(0), $"no ```{language} block follows '{heading}' in {path}");

        int from = open + language.Length + 4;

        return text[from..text.IndexOf("\n```", from, StringComparison.Ordinal)];
    }

    private static string Guides(string page)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "guides")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "docs/guides was not found above the test assembly");

        return Path.Combine(directory!.FullName, "docs", "guides", page);
    }
}
