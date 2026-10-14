using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayrollReason> PayrollReasons => Set<PayrollReason>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<PayrollAdditionRequest> PayrollAdditionRequests => Set<PayrollAdditionRequest>();
    public DbSet<PayrollSettlement> PayrollSettlements => Set<PayrollSettlement>();
    public DbSet<CoachCompensationAgreement> CoachCompensationAgreements => Set<CoachCompensationAgreement>();
    public DbSet<CoachCompensationApproval> CoachCompensationApprovals => Set<CoachCompensationApproval>();

    private static void ConfigurePayroll(ModelBuilder b)
    {
        Tenant<Employee>(b); Tenant<PayrollReason>(b); Tenant<PayrollAdjustment>(b); Tenant<PayrollAdditionRequest>(b); Tenant<PayrollSettlement>(b);
        Tenant<CoachCompensationAgreement>(b); Tenant<CoachCompensationApproval>(b);
        var agreement = b.Entity<CoachCompensationAgreement>();
        agreement.HasAlternateKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId });
        agreement.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        agreement.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        agreement.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CreatedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        agreement.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        agreement.HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        agreement.HasIndex(x => new { x.AcademyId, x.CreatedByMembershipId, x.IdempotencyKey }).IsUnique();
        agreement.Property(x => x.VariableMethod).HasMaxLength(20); agreement.Property(x => x.Eligibility).HasMaxLength(30); agreement.Property(x => x.BaseCoverage).HasMaxLength(30);
        agreement.Property(x => x.Note).HasMaxLength(500); agreement.Property(x => x.IdempotencyKey).HasMaxLength(100); agreement.Property(x => x.Currency).HasMaxLength(3);
        agreement.Property(x => x.BaseSalarySnapshot).HasPrecision(18, 2); agreement.Property(x => x.VariableValue).HasPrecision(18, 2);
        agreement.ToTable(t => t.HasCheckConstraint("CK_CoachAgreement_Terms", "\"VariableMethod\" IN ('Percent','PerSession') AND \"Eligibility\" IN ('DeliveredCollected','Delivered') AND \"BaseCoverage\" IN ('GroupOnly','IncludesPrivate') AND \"VariableValue\" >= 0 AND \"VariableValue\" <= CASE WHEN \"VariableMethod\"='Percent' THEN 100 ELSE 100000000 END AND (\"BaseCoverage\" <> 'IncludesPrivate' OR \"VariableValue\"=0) AND (\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\")"));
        var approval = b.Entity<CoachCompensationApproval>();
        approval.HasOne<CoachCompensationAgreement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AgreementId, x.EmployeeId, x.BranchId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.EmployeeId, x.BranchId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        approval.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ApprovedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        approval.HasIndex(x => new { x.AcademyId, x.AgreementId }).IsUnique();
        approval.HasIndex(x => new { x.AcademyId, x.EmployeeId, x.BranchId, x.SportId, x.Revision }).IsUnique();
        approval.ToTable(t => t.HasCheckConstraint("CK_CoachApproval_Revision", "\"Revision\" > 0"));
        b.Entity<Employee>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Employee>().HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Employee>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PhotoAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Employee>().HasIndex(x => new { x.AcademyId, x.MembershipId }).IsUnique();
        b.Entity<Employee>().HasIndex(x => new { x.AcademyId, x.Phone }).IsUnique();
        b.Entity<Employee>().Property(x => x.Name).HasMaxLength(160); b.Entity<Employee>().Property(x => x.Phone).HasMaxLength(32); b.Entity<Employee>().Property(x => x.JobTitle).HasMaxLength(100); b.Entity<Employee>().Property(x => x.MonthlySalary).HasPrecision(18, 2); b.Entity<Employee>().Property(x => x.Currency).HasMaxLength(3);
        b.Entity<PayrollReason>().Property(x => x.Name).HasMaxLength(100); b.Entity<PayrollReason>().HasIndex(x => new { x.AcademyId, x.Name }).IsUnique();
        b.Entity<PayrollAdditionRequest>().HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollAdditionRequest>().HasOne<PayrollReason>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ReasonId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollAdditionRequest>().HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
        b.Entity<PayrollAdditionRequest>().Property(x => x.Amount).HasPrecision(18, 2); b.Entity<PayrollAdditionRequest>().Property(x => x.IdempotencyKey).HasMaxLength(100); b.Entity<PayrollAdditionRequest>().Property(x => x.ReasonName).HasMaxLength(100); b.Entity<PayrollAdditionRequest>().Property(x => x.ReviewNote).HasMaxLength(500);
        b.Entity<PayrollAdjustment>().HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollAdjustment>().HasOne<PayrollAdditionRequest>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AdditionRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollAdjustment>().HasIndex(x => new { x.AcademyId, x.AdditionRequestId }).IsUnique();
        b.Entity<PayrollAdjustment>().HasIndex(x => new { x.AcademyId, x.PerformedByUserId, x.IdempotencyKey }).IsUnique();
        b.Entity<PayrollAdjustment>().Property(x => x.Amount).HasPrecision(18, 2); b.Entity<PayrollAdjustment>().Property(x => x.IdempotencyKey).HasMaxLength(100); b.Entity<PayrollAdjustment>().Property(x => x.Reason).HasMaxLength(500);
        b.Entity<PayrollSettlement>().HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollSettlement>().HasIndex(x => new { x.AcademyId, x.EmployeeId, x.Month }).IsUnique();
        b.Entity<PayrollSettlement>().HasIndex(x => new { x.AcademyId, x.PaidByUserId, x.IdempotencyKey }).IsUnique();
        foreach (var name in new[] { nameof(PayrollSettlement.MonthlySalary), nameof(PayrollSettlement.BaseDue), nameof(PayrollSettlement.Deductions), nameof(PayrollSettlement.Additions), nameof(PayrollSettlement.NetDue) }) b.Entity<PayrollSettlement>().Property<decimal>(name).HasPrecision(18, 2);
        b.Entity<PayrollSettlement>().Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.Entity<PayrollAdjustment>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PayrollSettlement>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PaidByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
