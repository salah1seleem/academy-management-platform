using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<ExternalAgreement> ExternalAgreements=>Set<ExternalAgreement>();
    public DbSet<ExternalAgreementBooking> ExternalAgreementBookings=>Set<ExternalAgreementBooking>();
    public DbSet<ExternalAgreementReceipt> ExternalAgreementReceipts=>Set<ExternalAgreementReceipt>();
    public DbSet<CashExternalReceiptPosting> CashExternalReceiptPostings=>Set<CashExternalReceiptPosting>();
    private static void ConfigureExternalAgreements(ModelBuilder b)
    {
        Tenant<ExternalAgreement>(b);Tenant<ExternalAgreementBooking>(b);Tenant<ExternalAgreementReceipt>(b);Tenant<CashExternalReceiptPosting>(b);
        var a=b.Entity<ExternalAgreement>();
        a.Property(x=>x.CounterpartyName).HasMaxLength(180);a.Property(x=>x.Title).HasMaxLength(180);a.Property(x=>x.Note).HasMaxLength(500);a.Property(x=>x.CancellationReason).HasMaxLength(500);
        a.Property(x=>x.Amount).HasPrecision(18,2);a.Property(x=>x.Currency).HasMaxLength(3);a.Property(x=>x.RequestHash).HasMaxLength(64);a.Property(x=>x.Kind).HasConversion<string>().HasMaxLength(24);
        a.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Sport>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Employee>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CounterpartyEmployeeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        a.HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestId}).IsUnique();
        a.ToTable(t=>t.HasCheckConstraint("CK_ExternalAgreement_Shape","\"IsActive\" AND \"Amount\">0 AND \"Amount\"<=999999999 AND \"FromDate\"<=\"ToDate\" AND \"Kind\" IN ('ExternalProgram','PoolRental') AND length(trim(\"CounterpartyName\"))>0 AND length(trim(\"Title\"))>0 AND (\"CancelledAtUtc\" IS NULL)=(\"CancelledByUserId\" IS NULL)"));
        var link=b.Entity<ExternalAgreementBooking>();
        link.HasOne<ExternalAgreement>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AgreementId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        link.HasOne<ResourceBooking>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ResourceBookingId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        link.HasIndex(x=>new{x.AcademyId,x.ResourceBookingId}).IsUnique();
        var r=b.Entity<ExternalAgreementReceipt>();
        r.HasOne(x=>x.Agreement).WithMany().HasForeignKey(x=>new{x.AcademyId,x.AgreementId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.Property(x=>x.Amount).HasPrecision(18,2);r.Property(x=>x.Currency).HasMaxLength(3);r.Property(x=>x.PaymentMethod).HasMaxLength(12);r.Property(x=>x.PaymentReference).HasMaxLength(100);r.Property(x=>x.ReceiptNumber).HasMaxLength(45);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestId}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.ReceiptNumber}).IsUnique();
        r.ToTable(t=>t.HasCheckConstraint("CK_ExternalReceipt_Shape","\"IsActive\" AND \"Amount\">0 AND \"Amount\"<=999999999 AND \"PaymentMethod\" IN ('Cash','Online') AND (NOT \"CashPostingRequired\" OR \"PaymentMethod\"='Cash')"));
        var p=b.Entity<CashExternalReceiptPosting>();
        p.HasOne(x=>x.Receipt).WithMany().HasForeignKey(x=>new{x.AcademyId,x.ReceiptId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SessionId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        p.HasIndex(x=>new{x.AcademyId,x.ReceiptId}).IsUnique();p.Property(x=>x.Amount).HasPrecision(18,2);p.Property(x=>x.Currency).HasMaxLength(3);
        p.ToTable(t=>t.HasCheckConstraint("CK_CashExternalReceipt_Positive","\"IsActive\" AND \"Amount\">0"));
    }
}
