using FamilyJobsBoard.Domain.Administration;
using FamilyJobsBoard.Domain.GoodBehaviours;
using FamilyJobsBoard.Domain.Households;
using FamilyJobsBoard.Domain.Identity;
using FamilyJobsBoard.Domain.Jobs;
using FamilyJobsBoard.Domain.PointAdjustments;
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

    public DbSet<HouseholdBootstrap> HouseholdBootstraps => Set<HouseholdBootstrap>();

    public DbSet<MemberCredential> MemberCredentials => Set<MemberCredential>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<PinSetupToken> PinSetupTokens => Set<PinSetupToken>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<RecurringJobSeries> RecurringJobSeries => Set<RecurringJobSeries>();

    public DbSet<JobReviewDecision> JobReviewDecisions => Set<JobReviewDecision>();

    public DbSet<PointsLedgerEntry> PointsLedgerEntries => Set<PointsLedgerEntry>();

    public DbSet<GoodBehaviourType> GoodBehaviourTypes => Set<GoodBehaviourType>();

    public DbSet<GoodBehaviour> GoodBehaviours => Set<GoodBehaviour>();

    public DbSet<PointAdjustment> PointAdjustments => Set<PointAdjustment>();

    public DbSet<HouseholdDataReset> HouseholdDataResets => Set<HouseholdDataReset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
