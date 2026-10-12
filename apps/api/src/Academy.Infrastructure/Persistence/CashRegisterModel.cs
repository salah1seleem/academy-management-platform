using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashierAssignment> CashierAssignments => Set<CashierAssignment>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashTransfer> CashTransfers => Set<CashTransfer>();
    public DbSet<CashReceiptPosting> CashReceiptPostings => Set<CashReceiptPosting>();
    public DbSet<CashProductPosting> CashProductPostings => Set<CashProductPosting>();
    private static void ConfigureCashRegisters(ModelBuilder b)
    {
        Tenant<CashRegister>(b); Tenant<CashierAssignment>(b); Tenant<CashSession>(b); Tenant<CashMovement>(b); Tenant<CashTransfer>(b);
        Tenant<CashReceiptPosting>(b);
        Tenant<CashProductPosting>(b);
        b.Entity<CashProductPosting>(e => {
            e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.Currency).HasMaxLength(3); e.Property(x=>x.RequestHash).HasMaxLength(64);
            e.HasIndex(x=>new{x.AcademyId,x.ProductSaleId}).IsUnique();
            e.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SessionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x=>x.ProductSale).WithMany().HasForeignKey(x=>new{x.AcademyId,x.ProductSaleId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t=>t.HasCheckConstraint("CK_CashProductPosting_Amount", "\"Amount\" > 0"));
        });
        b.Entity<CashReceiptPosting>(e => {
            e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.Currency).HasMaxLength(3);
            e.Property(x=>x.RequestKey).HasMaxLength(150); e.Property(x=>x.RequestHash).HasMaxLength(64);
            e.HasIndex(x=>new{x.AcademyId,x.RequestKey}).IsUnique();
            e.HasIndex(x=>new{x.AcademyId,x.CollectionId}).IsUnique();
            e.HasIndex(x=>new{x.AcademyId,x.ReceiptId}).IsUnique();
            e.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SessionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentCollection>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CollectionId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x=>x.Receipt).WithMany().HasForeignKey(x=>new{x.AcademyId,x.ReceiptId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t=>t.HasCheckConstraint("CK_CashReceiptPosting_Amount", "\"Amount\" > 0"));
        });
        b.Entity<CashRegister>(e => {
            e.Property(x=>x.Name).HasMaxLength(100); e.Property(x=>x.Currency).HasMaxLength(3);
            e.Property(x=>x.RequestKey).HasMaxLength(150); e.Property(x=>x.RequestHash).HasMaxLength(64);
            e.HasIndex(x=>new{x.AcademyId,x.BranchId,x.Name}).IsUnique(); e.HasIndex(x=>new{x.AcademyId,x.RequestKey}).IsUnique();
            e.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.BranchId});
            e.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CashierAssignment>(e => {
            e.HasIndex(x=>new{x.AcademyId,x.RegisterId,x.MembershipId}).IsUnique();
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RegisterId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.MembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CashSession>(e => {
            e.Property(x=>x.OpeningAmount).HasPrecision(18,2); e.Property(x=>x.CountedAmount).HasPrecision(18,2); e.Property(x=>x.ExpectedAmount).HasPrecision(18,2);
            e.Property(x=>x.OpenKey).HasMaxLength(150); e.Property(x=>x.OpenHash).HasMaxLength(64); e.Property(x=>x.CloseKey).HasMaxLength(150); e.Property(x=>x.CloseHash).HasMaxLength(64); e.Property(x=>x.ClosingReason).HasMaxLength(400);
            e.HasIndex(x=>new{x.AcademyId,x.RegisterId}).IsUnique().HasFilter("\"ClosedAtUtc\" IS NULL");
            e.HasIndex(x=>new{x.AcademyId,x.OpenKey}).IsUnique(); e.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.BranchId});
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RegisterId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.OpenedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t=> { t.HasCheckConstraint("CK_CashSession_Money", "\"OpeningAmount\" >= 0 AND (\"CountedAmount\" IS NULL OR \"CountedAmount\" >= 0)"); t.HasCheckConstraint("CK_CashSession_Close", "(\"ClosedAtUtc\" IS NULL AND \"ClosedByUserId\" IS NULL AND \"CountedAmount\" IS NULL AND \"ExpectedAmount\" IS NULL AND \"CloseKey\" IS NULL AND \"CloseHash\" IS NULL) OR (\"ClosedAtUtc\" IS NOT NULL AND \"ClosedByUserId\" IS NOT NULL AND \"CountedAmount\" IS NOT NULL AND \"ExpectedAmount\" IS NOT NULL AND \"CloseKey\" IS NOT NULL AND \"CloseHash\" IS NOT NULL)"); });
        });
        b.Entity<CashTransfer>(e => {
            e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.Currency).HasMaxLength(3); e.Property(x=>x.Reason).HasMaxLength(400); e.Property(x=>x.RequestKey).HasMaxLength(150); e.Property(x=>x.RequestHash).HasMaxLength(64);
            e.HasIndex(x=>new{x.AcademyId,x.RequestKey}).IsUnique();
            e.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.FromSessionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ToSessionId,x.ToBranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t=>t.HasCheckConstraint("CK_CashTransfer_Valid", "\"Amount\" > 0 AND \"FromSessionId\" <> \"ToSessionId\""));
        });
        b.Entity<CashMovement>(e => {
            e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.Currency).HasMaxLength(3); e.Property(x=>x.Kind).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x=>new{x.AcademyId,x.TransferId,x.Kind}).IsUnique(); e.HasIndex(x=>new{x.AcademyId,x.SessionId});
            e.HasOne<CashSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SessionId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.BranchId}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashTransfer>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TransferId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t=>t.HasCheckConstraint("CK_CashMovement_Sign", "(\"Kind\" = 'TransferIn' AND \"Amount\" > 0) OR (\"Kind\" = 'TransferOut' AND \"Amount\" < 0)"));
        });
    }
}
