using NUnit.Framework;

namespace XpSearch.Ingestion.Tests.Fixtures;

/// <summary>
/// Thin wrappers around the NUnit assertions whose overloads a lambda cannot disambiguate
/// (<c>Assert.Multiple</c> and <c>Assert.ThrowsAsync</c> each take two structurally identical
/// delegate types, one of them obsolete). Taking the delegate as a typed parameter picks the
/// non-obsolete overload once, here.
/// </summary>
/// <summary>
/// One options instance behind <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>,
/// which is what the search pipeline stages take since AR-1.
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
internal sealed class StaticOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    internal StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

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
