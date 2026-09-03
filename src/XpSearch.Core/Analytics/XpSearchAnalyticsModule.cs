using CMS;
using CMS.Base;
using CMS.Core;
using CMS.DataEngine;

using Microsoft.Extensions.DependencyInjection;

using XpSearch.Core.Analytics;
using XpSearch.Core.ContactGroups;

[assembly: RegisterModule(typeof(XpSearchAnalyticsModule))]

namespace XpSearch.Core.Analytics;

/// <summary>
/// Installs the query log object type and the four search activity types when the application starts
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/run-code-on-application-startup),
/// and keeps the query log worker thread alive across requests.
/// </summary>
public class XpSearchAnalyticsModule : Module
{
    private IServiceProvider? services;

    /// <summary>Initializes a new instance of the <see cref="XpSearchAnalyticsModule"/> class.</summary>
    public XpSearchAnalyticsModule()
        : base("XpSearchAnalytics")
    {
    }

    /// <inheritdoc />
    protected override void OnInit(ModuleInitParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        base.OnInit(parameters);

        services = parameters.Services;

        // The class has to exist before anything reads it, and the database is only reachable once the
        // application is initialized - the same sequencing LuceneSearchModule uses.
        ApplicationEvents.Initialized.Execute += Initialize;

        RequestEvents.RunEndRequestTasks.Execute += (_, _) => ThreadWorker<XpSearchQueryLogQueueWorker>.Current.EnsureRunningThread();
    }

    private void Initialize(object? sender, EventArgs e)
    {
        // Both installers are only registered by AddXpSearch; an application that never called it has
        // no analytics to install.
        services!.GetService<XpSearchAnalyticsModuleInstaller>()?.Install();
        services!.GetService<XpSearchActivityTypeInstaller>()?.Install();
        services!.GetService<XpSearchContactGroupRuleInstaller>()?.Install();

        // AR-2: from here on, saving an index's settings row drops that index's cached options instance.
        services!.GetService<Options.XpSearchIndexSettingsInvalidator>()?.Start();
    }
}
