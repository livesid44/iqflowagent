using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantAiSettings> TenantAiSettings => Set<TenantAiSettings>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<AuthSettings> AuthSettings => Set<AuthSettings>();
    public DbSet<IntakeRecord> IntakeRecords => Set<IntakeRecord>();
    public DbSet<IntakeTask> IntakeTasks => Set<IntakeTask>();
    public DbSet<TaskActionLog> TaskActionLogs => Set<TaskActionLog>();
    public DbSet<IntakeDocument> IntakeDocuments => Set<IntakeDocument>();
    public DbSet<ReportFieldStatus> ReportFieldStatuses => Set<ReportFieldStatus>();
    public DbSet<FinalReport> FinalReports => Set<FinalReport>();
    public DbSet<MasterDepartment> MasterDepartments => Set<MasterDepartment>();
    public DbSet<MasterLob> MasterLobs => Set<MasterLob>();
    public DbSet<QcCheck> QcChecks => Set<QcCheck>();
    public DbSet<RagJob> RagJobs => Set<RagJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantAiSettings>()
            .HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TenantAiSettings>()
            .HasIndex(s => s.TenantId)
            .IsUnique();

        builder.Entity<UserTenant>()
            .HasOne(ut => ut.User)
            .WithMany()
            .HasForeignKey(ut => ut.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserTenant>()
            .HasOne(ut => ut.Tenant)
            .WithMany()
            .HasForeignKey(ut => ut.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<IntakeTask>()
            .HasOne(t => t.IntakeRecord)
            .WithMany()
            .HasForeignKey(t => t.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TaskActionLog>()
            .HasOne(l => l.Task)
            .WithMany(t => t.ActionLogs)
            .HasForeignKey(l => l.IntakeTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<IntakeDocument>()
            .HasOne(d => d.IntakeRecord)
            .WithMany()
            .HasForeignKey(d => d.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<IntakeDocument>()
            .HasOne(d => d.Task)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.IntakeTaskId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<ReportFieldStatus>()
            .HasOne(r => r.IntakeRecord)
            .WithMany()
            .HasForeignKey(r => r.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<FinalReport>()
            .HasOne(r => r.IntakeRecord)
            .WithMany()
            .HasForeignKey(r => r.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QcCheck>()
            .HasOne(q => q.IntakeRecord)
            .WithMany()
            .HasForeignKey(q => q.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RagJob>()
            .HasOne(r => r.IntakeRecord)
            .WithMany()
            .HasForeignKey(r => r.IntakeRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
