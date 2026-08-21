using CMS.DataEngine;

namespace XpSearch.Core.Indexing;

/// <summary>
/// Reads the class form definition of an Xperience class (a content type, a reusable field schema
/// carrier, a system class). The seam that keeps the platform's static class API out of the schema
/// logic, so field detection stays unit-testable.
/// </summary>
public interface IDataClassDefinitionSource
{
    /// <summary>Gets the class form definition XML of a class.</summary>
    /// <param name="className">Class name, for example <c>DancingGoat.ArticlePage</c>.</param>
    /// <returns>The <c>ClassFormDefinition</c> XML, or <see langword="null"/> when there is no such class.</returns>
    string? GetFormDefinition(string className);
}

/// <summary>
/// Reads class form definitions through <see cref="DataClassInfoProvider"/>.
/// </summary>
/// <remarks>
/// The static provider is used deliberately. <c>DataClassInfoProviderBase</c> is
/// <c>INotManagedByContainer</c>, so Xperience 31.8.0 registers no <c>IInfoProvider&lt;DataClassInfo&gt;</c>
/// - "not all system classes currently fully support this approach", see
/// https://docs.kentico.com/documentation/developers-and-admins/api/database-table-api - and injecting
/// one makes the application fail to start under the default DI validation.
/// <c>DataClassInfoProvider.GetDataClassInfo(className)</c> is the route Kentico's own documentation
/// takes, for instance in
/// https://docs.kentico.com/guides/development/data-protection/data-collectors-find-contact-personal-data
/// and https://docs.kentico.com/api/digital-marketing/form-data. Do not replace it with
/// <c>Provider&lt;DataClassInfo&gt;.Instance</c>: that resolves the missing service back out of the
/// container.
/// </remarks>
public sealed class DataClassInfoDefinitionSource : IDataClassDefinitionSource
{
    /// <inheritdoc />
    public string? GetFormDefinition(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        return DataClassInfoProvider.GetDataClassInfo(className, throwIfNotFound: false)?.ClassFormDefinition;
    }
}
