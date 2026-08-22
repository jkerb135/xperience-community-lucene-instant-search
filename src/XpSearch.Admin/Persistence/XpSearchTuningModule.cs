using CMS;
using CMS.Base;
using CMS.Core;
using CMS.DataEngine;

using Microsoft.Extensions.DependencyInjection;

using XpSearch.Admin.Persistence;

[assembly: RegisterModule(typeof(XpSearchTuningModule))]

namespace XpSearch.Admin.Persistence;

/// <summary>
/// Installs the relevance tuning object types when the application starts
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/run-code-on-application-startup).
/// </summary>
public class XpSearchTuningModule : Module
{
    private IServiceProvider? services;

    /// <summary>Initializes a new instance of the <see cref="XpSearchTuningModule"/> class.</summary>
    public XpSearchTuningModule()
        : base("XpSearchTuning")
    {
    }

    /// <inheritdoc />
    protected override void OnInit(ModuleInitParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        base.OnInit(parameters);

        services = parameters.Services;

        // The classes have to exist before the admin pages read them, and the database is only
        // reachable once the application is initialized - the sequencing LuceneSearchModule uses.
        ApplicationEvents.Initialized.Execute += (_, _) =>
            services!.GetRequiredService<XpSearchTuningModuleInstaller>().Install();
    }
}
