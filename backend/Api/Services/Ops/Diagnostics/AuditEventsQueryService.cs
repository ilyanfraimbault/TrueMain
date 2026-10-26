using Data.Logging.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Diagnostics;

public interface IAuditEventsQueryService
{
    Task<AuditEventsReadModel> GetAsync(AuditEventFilter filter, CancellationToken ct);
}

/// <summary>
/// The operator-action trail for <c>GET /ops/audit-events</c> (#2288): maps the Data-layer
/// <see cref="IAuditEventQuery"/> page onto the API contract and words each action from
/// <see cref="AuditActions"/>.
/// </summary>
public sealed class AuditEventsQueryService(IAuditEventQuery query) : IAuditEventsQueryService
{
    private static readonly IReadOnlyList<AuditActionReadModel> ActionCatalog =
        AuditActions.Labels.Select(pair => new AuditActionReadModel(pair.Key, pair.Value)).ToList();

    public async Task<AuditEventsReadModel> GetAsync(AuditEventFilter filter, CancellationToken ct)
    {
        var page = await query.GetAsync(filter, ct);
        return new AuditEventsReadModel
        {
            Entries = page.Entries
                .Select(row => new AuditEventReadModel
                {
                    Id = row.Id,
                    TimestampUtc = row.TimestampUtc,
                    Actor = row.Actor,
                    Action = row.Action,
                    Label = AuditActions.Label(row.Action),
                    TargetType = row.TargetType,
                    TargetId = row.TargetId,
                    Metadata = row.Metadata,
                })
                .ToList(),
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
            Actions = ActionCatalog,
            Actors = AuditActors.All,
            TargetTypes = AuditTargets.All,
        };
    }
}
