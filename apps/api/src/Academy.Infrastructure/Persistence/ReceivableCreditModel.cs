using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<ReceivableCreditApplication> ReceivableCreditApplications => Set<ReceivableCreditApplication>();
    private static void ConfigureReceivableCredit(ModelBuilder b)
    {
        Tenant<ReceivableCreditApplication>(b);
        b.Entity<ReceivableCreditApplication>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.SourceBalanceBefore).HasPrecision(18, 2);
            e.Property(x => x.TargetBalanceBefore).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Reason).HasMaxLength(400);
            e.Property(x => x.RequestKey).HasMaxLength(80); e.Property(x => x.RequestHash).HasMaxLength(64); e.Property(x => x.QuoteHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.SourceDocumentId }); e.HasIndex(x => new { x.AcademyId, x.TargetDocumentId });
            e.HasOne<ReceivableDocument>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SourceDocumentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ReceivableDocument>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TargetDocumentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ActorMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ReceivableCredit_Conserved", "\"SourceDocumentId\" <> \"TargetDocumentId\" AND \"Amount\" > 0 AND \"SourceBalanceBefore\" < 0 AND \"TargetBalanceBefore\" > 0 AND \"Amount\" <= -\"SourceBalanceBefore\" AND \"Amount\" <= \"TargetBalanceBefore\""));
        });
    }
}
