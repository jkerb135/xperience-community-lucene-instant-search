using NUnit.Framework;

namespace XpSearch.Admin.Tests;

/// <summary>
/// A thin wrapper around <c>Assert.Multiple</c>, whose two structurally identical delegate overloads
/// a lambda cannot disambiguate. Same helper as the other test projects.
/// </summary>
internal static class Expect
{
    internal static void Multiple(Action assertions) => Assert.Multiple(assertions);
}
