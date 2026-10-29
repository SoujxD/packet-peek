using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace PacketPeek;

public record RuleRequest(string Name, string Metric, string Grouping, int WindowSeconds, long Threshold, bool Active);
public class RuleRequestValidator : AbstractValidator<RuleRequest>
{
    public RuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Metric).Must(x => x is "packets" or "bytes" or "distinct_dst_ips");
        RuleFor(x => x.Grouping).Must(x => x is "src_ip" or "src_ip+dst_port");
        RuleFor(x => x.WindowSeconds).InclusiveBetween(1, 86400);
        RuleFor(x => x.Threshold).GreaterThanOrEqualTo(0);
    }
}

public static class RuleEndpoints
{
    public static void MapRuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/rules").RequireAuthorization("Operator");
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Rules.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)));
        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
            await db.Rules.FindAsync([id], ct) is { } rule ? Results.Ok(rule) : Results.NotFound());
        group.MapPost("/", async (RuleRequest request, IValidator<RuleRequest> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            var rule = new Rule(); Apply(rule, request); db.Rules.Add(rule); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/rules/{rule.Id}", rule);
        });
        group.MapPut("/{id:int}", async (int id, RuleRequest request, IValidator<RuleRequest> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            var rule = await db.Rules.FindAsync([id], ct);
            if (rule is null) return Results.NotFound();
            Apply(rule, request); await db.SaveChangesAsync(ct); return Results.Ok(rule);
        });
        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var rule = await db.Rules.FindAsync([id], ct);
            if (rule is null) return Results.NotFound();
            if (await db.Alerts.AnyAsync(x => x.RuleId == id, ct))
                return Results.Conflict(new { error = "Deactivate rules with alert history instead of deleting them." });
            db.Rules.Remove(rule); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
    }

    private static void Apply(Rule rule, RuleRequest request)
    {
        rule.Name = request.Name.Trim(); rule.Metric = request.Metric; rule.Grouping = request.Grouping;
        rule.WindowSeconds = request.WindowSeconds; rule.Threshold = request.Threshold; rule.Active = request.Active;
    }
}
