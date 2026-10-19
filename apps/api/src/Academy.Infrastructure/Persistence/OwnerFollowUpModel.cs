using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<OwnerFollowUp> OwnerFollowUps => Set<OwnerFollowUp>();
    public DbSet<OwnerFollowUpChange> OwnerFollowUpChanges => Set<OwnerFollowUpChange>();
    private static void ConfigureOwnerFollowUps(ModelBuilder b)
    {
        Tenant<OwnerFollowUp>(b); Tenant<OwnerFollowUpChange>(b);
        var f = b.Entity<OwnerFollowUp>();
        f.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        f.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SourceBranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        f.HasOne<Sport>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SourceSportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        f.Property(x=>x.SourceKind).HasMaxLength(20).HasDefaultValue("Group");
        f.Property(x=>x.SourceNameSnapshot).HasMaxLength(200);f.Property(x=>x.SourceBranchSnapshot).HasMaxLength(200);f.Property(x=>x.SourceSportSnapshot).HasMaxLength(200);
        f.HasIndex(x=>new{x.AcademyId,x.SourceKind,x.SourceId}).IsUnique().HasFilter("\"SourceKind\" <> 'Group' AND \"State\" NOT IN ('Closed','Rejected')");
        f.ToTable(t=>t.HasCheckConstraint("CK_OwnerFollowUp_Source","(\"SourceKind\"='Group' AND \"TrainingGroupId\" IS NOT NULL AND \"SourceId\" IS NULL AND \"ManualPriority\" IS NULL) OR (\"SourceKind\" IN ('Player','Employee','Guardian','Request','Receivable') AND \"TrainingGroupId\" IS NULL AND \"SourceId\" IS NOT NULL AND \"Condition\"='RecordFollowUp' AND length(trim(\"SourceNameSnapshot\"))>0 AND \"SourceNameSnapshot\" IS NOT NULL AND \"ManualPriority\" IS NOT NULL AND \"ManualPriority\" IN (1,2))"));
        f.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CreatedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        f.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AssignedToMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        f.HasIndex(x => new { x.AcademyId, x.CreatedByMembershipId, x.RequestId }).IsUnique();
        f.HasIndex(x => new { x.AcademyId, x.TrainingGroupId, x.Condition }).IsUnique().HasFilter("\"State\" NOT IN ('Closed','Rejected')");
        f.Property(x => x.Condition).HasMaxLength(30); f.Property(x => x.State).HasMaxLength(16);
        f.Property(x => x.Note).HasMaxLength(500); f.Property(x => x.ResolutionReason).HasMaxLength(500);
        f.Property(x => x.RequestHash).HasMaxLength(64); f.Property(x => x.EvidenceJson).HasColumnType("jsonb");
        f.ToTable(t => t.HasCheckConstraint("CK_OwnerFollowUp_State", "\"IsActive\" AND \"Condition\" IN ('RecordFollowUp','GroupCoachMissing','GroupLowOccupancy','GroupSessionCoverage','GroupEvaluationDrafts','GroupRecordedAbsences','GroupOverdueFeedback') AND \"State\" IN ('New','Assigned','InProgress','Snoozed','Closed','Rejected') AND (\"State\" NOT IN ('Assigned','InProgress') OR \"AssignedToMembershipId\" IS NOT NULL) AND (\"State\" <> 'Snoozed' OR \"DueOn\" IS NOT NULL) AND (\"State\" NOT IN ('Closed','Rejected') OR length(trim(\"ResolutionReason\")) > 0 AND \"ResolutionReason\" IS NOT NULL)"));
        var c = b.Entity<OwnerFollowUpChange>();
        c.HasOne<OwnerFollowUp>().WithMany().HasForeignKey(x => new { x.AcademyId, x.FollowUpId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.AcademyId, x.ActorMembershipId, x.RequestId }).IsUnique();
        c.Property(x => x.RequestHash).HasMaxLength(64); c.Property(x => x.BeforeJson).HasColumnType("jsonb"); c.Property(x => x.AfterJson).HasColumnType("jsonb");
    }
}
