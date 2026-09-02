using NUnit.Framework;

namespace XpSearch.Client.Tests;

/// <summary>
/// Thin wrappers around the NUnit assertions whose overloads a lambda cannot disambiguate
/// (<c>Assert.Multiple</c>, <c>Assert.Throws</c> and <c>Assert.ThrowsAsync</c> each take two
/// structurally identical delegate types). Same fixture as the other suites.
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
