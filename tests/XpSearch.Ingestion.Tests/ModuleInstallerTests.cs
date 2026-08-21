using CMS.DataEngine;
using CMS.FormEngine;

using NUnit.Framework;

using XpSearch.Ingestion.Persistence;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// The three custom module classes the ingestion API stores its data in, checked against the columns
/// spec §10.2 and §10.4 name. A pure test over the form definitions: installing them needs a database,
/// but getting a column name wrong does not.
/// </summary>
[TestFixture]
internal sealed class ModuleInstallerTests
{
    [Test]
    public void ExternalDocumentClassHasTheDocumentedColumns() =>
        AssertColumns(
            XpSearchIngestionModuleInstaller.ExternalDocumentForm(),
            [
                nameof(XpSearchExternalDocumentInfo.DocumentID),
                nameof(XpSearchExternalDocumentInfo.DocumentGuid),
                nameof(XpSearchExternalDocumentInfo.DocumentIndexName),
                nameof(XpSearchExternalDocumentInfo.DocumentSource),
                nameof(XpSearchExternalDocumentInfo.DocumentKey),
                nameof(XpSearchExternalDocumentInfo.DocumentBody),
                nameof(XpSearchExternalDocumentInfo.DocumentHash),
                nameof(XpSearchExternalDocumentInfo.DocumentCreatedAt),
                nameof(XpSearchExternalDocumentInfo.DocumentUpdatedAt),
                nameof(XpSearchExternalDocumentInfo.DocumentStatus),
            ]);

    [Test]
    public void ApiKeyClassHasTheColumnsOfTheSpecTable() =>
        AssertColumns(
            XpSearchIngestionModuleInstaller.ApiKeyForm(),
            [
                nameof(XpSearchApiKeyInfo.KeyID),
                nameof(XpSearchApiKeyInfo.KeyGuid),
                nameof(XpSearchApiKeyInfo.KeyName),
                nameof(XpSearchApiKeyInfo.KeyHash),
                nameof(XpSearchApiKeyInfo.KeyPrefix),
                nameof(XpSearchApiKeyInfo.KeyScopes),
                nameof(XpSearchApiKeyInfo.KeyEnabled),
                nameof(XpSearchApiKeyInfo.KeyExpiresAt),
                nameof(XpSearchApiKeyInfo.KeyLastUsedAt),
            ]);

    [Test]
    public void IngestionLogClassRecordsKeyPrefixIndexCountAndOutcome() =>
        AssertColumns(
            XpSearchIngestionModuleInstaller.IngestionLogForm(),
            [
                nameof(XpSearchIngestionLogInfo.LogID),
                nameof(XpSearchIngestionLogInfo.LogGuid),
                nameof(XpSearchIngestionLogInfo.LogKeyPrefix),
                nameof(XpSearchIngestionLogInfo.LogIndexName),
                nameof(XpSearchIngestionLogInfo.LogOperation),
                nameof(XpSearchIngestionLogInfo.LogDocumentCount),
                nameof(XpSearchIngestionLogInfo.LogSucceeded),
                nameof(XpSearchIngestionLogInfo.LogMessage),
                nameof(XpSearchIngestionLogInfo.LogCreatedAt),
            ]);

    [Test]
    public void TheExpiryAndLastUsedColumnsAreOptional()
    {
        var fields = XpSearchIngestionModuleInstaller.ApiKeyForm().GetFields(true, true).ToDictionary(field => field.Name, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(fields[nameof(XpSearchApiKeyInfo.KeyExpiresAt)].AllowEmpty, Is.True);
            Assert.That(fields[nameof(XpSearchApiKeyInfo.KeyLastUsedAt)].AllowEmpty, Is.True);
            Assert.That(fields[nameof(XpSearchApiKeyInfo.KeyHash)].AllowEmpty, Is.False);
            Assert.That(fields[nameof(XpSearchApiKeyInfo.KeyScopes)].DataType, Is.EqualTo(FieldDataType.LongText));
        });
    }

    private static void AssertColumns(FormInfo form, string[] expected) =>
        Assert.That(form.GetFields(true, true).Select(field => field.Name), Is.EquivalentTo(expected));
}
