using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<AuthSettings> AuthSettings => Set<AuthSettings>();
    public DbSet<IntakeRecord> IntakeRecords => Set<IntakeRecord>();
    public DbSet<IntakeTask> IntakeTasks => Set<IntakeTask>();
    public DbSet<TaskActionLog> TaskActionLogs => Set<TaskActionLog>();
    public DbSet<IntakeDocument> IntakeDocuments => Set<IntakeDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
    }
}
