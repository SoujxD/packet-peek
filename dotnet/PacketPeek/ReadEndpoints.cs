using Microsoft.EntityFrameworkCore;

namespace PacketPeek;

public static class ReadEndpoints
{
    public static void MapReadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Operator");
        group.MapGet("/flows", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Flows.AsNoTracking().OrderByDescending(x => x.LastUs).ThenByDescending(x => x.Id).Take(500).ToListAsync(ct)));
        group.MapGet("/alerts", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Alerts.AsNoTracking().OrderByDescending(x => x.Id).Take(500)
                .Select(x => new { x.Id, x.RuleId, RuleName = x.Rule.Name, x.FiredAt, x.GroupKey, x.Value }).ToListAsync(ct)));
        group.MapGet("/alerts/{id:long}", async (long id, AppDbContext db, CancellationToken ct) =>
        {
            var alert = await db.Alerts.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.RuleId, RuleName = x.Rule.Name, x.FiredAt, x.GroupKey, x.Value,
                    x.Rule.Metric, x.Rule.Grouping, x.Rule.WindowSeconds, x.Rule.Threshold }).SingleOrDefaultAsync(ct);
            return alert is null ? Results.NotFound() : Results.Ok(alert);
        });
    }
}
