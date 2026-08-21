using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>Fixed set of index names, standing in for the project's Lucene indexes.</summary>
internal sealed class FakeIndexCatalog : IXpSearchIndexCatalog
{
    private readonly string[] names;

    public FakeIndexCatalog(params string[] names) => this.names = names;

    public IReadOnlyList<string> GetIndexNames() => names;
}

/// <summary>A fixed editing mode.</summary>
internal sealed class FakeEditorContext : IXpSearchEditorContext
{
    public FakeEditorContext(XpSearchEditorMode mode) => Mode = mode;

    public XpSearchEditorMode Mode { get; set; }

    public XpSearchEditorMode GetMode() => Mode;
}

/// <summary>Helpers shared by the widget tests.</summary>
internal static class Rendered
{
    /// <summary>Renders HTML content the way Razor would.</summary>
    public static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);

        return writer.ToString();
    }

    /// <summary>Reads one HTML attribute of a rendered single-element markup string, decoded.</summary>
    public static string Attribute(string markup, string name)
    {
        string start = $"{name}=\"";
        int at = markup.IndexOf(start, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"attribute {name} is missing from: {markup}");
        int from = at + start.Length;
        int to = markup.IndexOf('"', from);

        return System.Net.WebUtility.HtmlDecode(markup[from..to]);
    }

    /// <summary>Parses the JSON of an attribute into a dictionary of raw JSON elements.</summary>
    public static JsonElement Json(string markup, string name) =>
        JsonDocument.Parse(Attribute(markup, name)).RootElement.Clone();
}

/// <summary>
/// Wrappers around the NUnit assertions whose overloads a lambda cannot disambiguate; the same
/// helper the core test project keeps for the same reason.
/// </summary>
internal static class Expect
{
    internal static void Multiple(Action assertions) => Assert.Multiple(assertions);
}
