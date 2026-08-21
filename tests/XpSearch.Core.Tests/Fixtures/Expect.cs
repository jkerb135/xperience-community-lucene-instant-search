using NUnit.Framework;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>
/// Thin wrappers around the NUnit assertions whose overloads a lambda cannot disambiguate
/// (<c>Assert.Multiple</c> and <c>Assert.ThrowsAsync</c> each take two structurally identical
/// delegate types, one of them obsolete). Taking the delegate as a typed parameter picks the
/// non-obsolete overload once, here.
/// </summary>
internal static class Expect
{
    internal static void Multiple(Action assertions) => Assert.Multiple(assertions);

    internal static TException Throws<TException>(Action action)
        where TException : Exception =>
        Assert.Throws<TException>(action)!;

    internal static TException ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception =>
        Assert.ThrowsAsync<TException>(action)!;
}
