using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<ReceivableDocument> ReceivableDocuments => Set<ReceivableDocument>();
    public DbSet<ReceivableMovement> ReceivableMovements => Set<ReceivableMovement>();
    private static void ConfigureReceivables(ModelBuilder b)
    {
        Tenant<ReceivableDocument>(b); Tenant<ReceivableMovement>(b);
        b.Entity<ReceivableDocument>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.PlanPriceSnapshot).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Description).HasMaxLength(400); e.Property(x => x.PlanNameSnapshot).HasMaxLength(160);
            e.Property(x => x.PlayerNameSnapshot).HasMaxLength(180);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(24); e.Property(x => x.SourceKey).HasMaxLength(150); e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.SourceKey }).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.BranchId, x.DueDate });
            // The posted branch is historical: moving an enrollment later must not rewrite or block its ledger.
            e.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ReceivableDocument_Positive", "\"Amount\" > 0 OR (\"Amount\" < 0 AND \"Source\" = 'OpeningBalance' AND \"ImportJobId\" IS NOT NULL)"));
        });
        b.Entity<ReceivableMovement>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Reason).HasMaxLength(400);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24); e.Property(x => x.SourceKey).HasMaxLength(150); e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasIndex(x => new { x.AcademyId, x.SourceKey }).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.DocumentId });
            e.HasOne<ReceivableDocument>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DocumentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentCollection>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CollectionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_ReceivableMovement_Positive", "\"Amount\" > 0"); t.HasCheckConstraint("CK_ReceivableMovement_Collection", "(\"Kind\" = 'PaymentAllocation' AND \"CollectionId\" IS NOT NULL) OR (\"Kind\" = 'CreditNote' AND \"CollectionId\" IS NULL)"); });
        });
    }
}
