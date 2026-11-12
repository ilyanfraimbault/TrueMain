using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Ops.Candidates;
using TrueMain.Services.Ops.Configuration;
using TrueMain.Services.Ops.Coverage;
using TrueMain.Services.Ops.Database;
using TrueMain.Services.Ops.DataQuality;
using TrueMain.Services.Ops.Diagnostics;
using TrueMain.Services.Ops.Health;
using TrueMain.Services.Ops.Navigation;
using TrueMain.Services.Ops.Processes;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.Services.Ops;

/// <summary>Registers every read and action behind the admin portal's <c>/ops</c> routes.</summary>
public static class OpsServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainOps(this IServiceCollection services)
        => services
            .AddTrueMainOpsHealth()
            .AddTrueMainOpsNavigation()
            .AddTrueMainOpsStats()
            .AddTrueMainOpsDatabase()
            .AddTrueMainOpsProcesses()
            .AddTrueMainOpsDiagnostics()
            .AddTrueMainDataQuality()
            .AddTrueMainOpsConfiguration()
            .AddTrueMainOpsAccounts()
            .AddTrueMainPatchCoverage()
            .AddTrueMainOpsCandidates();
}
