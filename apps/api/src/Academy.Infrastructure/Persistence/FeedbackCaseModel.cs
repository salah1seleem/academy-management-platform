using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<FeedbackChange> FeedbackChanges => Set<FeedbackChange>();
    private static void ConfigureFeedbackCases(ModelBuilder b)
    {
        Tenant<FeedbackChange>(b);
        var c = b.Entity<FeedbackChange>();
        c.HasOne<GuardianFeedback>().WithMany().HasForeignKey(x => new { x.AcademyId, x.FeedbackId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.AcademyId, x.ActorMembershipId, x.RequestId }).IsUnique();
        c.Property(x => x.RequestHash).HasMaxLength(64);
        c.Property(x => x.Action).HasMaxLength(16); c.Property(x => x.FromStatus).HasMaxLength(16); c.Property(x => x.ToStatus).HasMaxLength(16);
        c.Property(x => x.InternalNote).HasMaxLength(500); c.Property(x => x.PublicResponse).HasMaxLength(500);
        c.ToTable(t => t.HasCheckConstraint("CK_FeedbackChange_Active", "\"IsActive\" AND (\"RequestId\" IS NULL) = (\"RequestHash\" IS NULL)"));
        b.Entity<GuardianFeedback>().ToTable(t => t.HasCheckConstraint("CK_GuardianFeedback_Status", "\"Status\" IN ('Open','Reviewed','InProgress','AwaitingInfo','Resolved','Closed','Reopened') AND (\"Kind\" <> 'Rating' OR \"Status\" IN ('Open','Reviewed'))"));
    }
}
