using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.Points;
using Microsoft.EntityFrameworkCore;

namespace FamilyJobsBoard.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<DailyJobSeries> DailyJobSeries => Set<DailyJobSeries>();

    public DbSet<JobReviewDecision> JobReviewDecisions => Set<JobReviewDecision>();

    public DbSet<PointsLedgerEntry> PointsLedgerEntries => Set<PointsLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
