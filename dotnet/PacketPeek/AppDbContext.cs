using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PacketPeek;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<FlowSummary> Flows => Set<FlowSummary>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<Rule>().Property(x => x.Name).HasMaxLength(100);
        model.Entity<Rule>().Property(x => x.Metric).HasMaxLength(32);
        model.Entity<Rule>().Property(x => x.Grouping).HasMaxLength(32);
        model.Entity<Alert>().HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Alert>().HasIndex(x => x.FiredAt);
        model.Entity<FlowSummary>().HasIndex(x => x.LastUs);
    }
}
