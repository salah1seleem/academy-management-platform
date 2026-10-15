using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PayrollStatement> PayrollStatements=>Set<PayrollStatement>();
    public DbSet<PayrollDisbursement> PayrollDisbursements=>Set<PayrollDisbursement>();
    public DbSet<PayrollAdvance> PayrollAdvances=>Set<PayrollAdvance>();
    private static void ConfigurePayrollStatements(ModelBuilder b)
    {
        Tenant<PayrollStatement>(b);Tenant<PayrollDisbursement>(b);Tenant<PayrollAdvance>(b);
        var a=b.Entity<PayrollAdvance>();
        a.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.EmployeeId,x.BranchId,x.Currency});
        a.HasOne<Employee>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.EmployeeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        a.HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestKey}).IsUnique();
        a.Property(x=>x.Amount).HasPrecision(18,2);a.Property(x=>x.Currency).HasMaxLength(3);a.Property(x=>x.Reference).HasMaxLength(120);a.Property(x=>x.RequestKey).HasMaxLength(100);a.Property(x=>x.RequestHash).HasMaxLength(64);
        a.ToTable(t=>t.HasCheckConstraint("CK_PayrollAdvance_Amount","\"Amount\">0"));
        var s=b.Entity<PayrollStatement>();var p=b.Entity<PayrollDisbursement>();
        s.HasOne<Employee>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.EmployeeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.BranchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ApprovedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        s.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.EmployeeId,x.BranchId,x.Currency});
        s.HasIndex(x=>new{x.AcademyId,x.EmployeeId,x.Month}).IsUnique();s.HasIndex(x=>new{x.AcademyId,x.ApprovedByMembershipId,x.RequestKey}).IsUnique();
        s.Property(x=>x.EmployeeName).HasMaxLength(160);s.Property(x=>x.Currency).HasMaxLength(3);s.Property(x=>x.Note).HasMaxLength(500);s.Property(x=>x.SourceSnapshot).HasColumnType("text");s.Property(x=>x.SourceHash).HasMaxLength(64);s.Property(x=>x.RequestKey).HasMaxLength(100);s.Property(x=>x.RequestHash).HasMaxLength(64);
        foreach(var name in new[]{nameof(PayrollStatement.MonthlySalary),nameof(PayrollStatement.BaseDue),nameof(PayrollStatement.Deductions),nameof(PayrollStatement.Additions),nameof(PayrollStatement.NetDue),nameof(PayrollStatement.PrivateCompensation)})s.Property<decimal>(name).HasPrecision(18,2);
        s.ToTable(t=>t.HasCheckConstraint("CK_PayrollStatement_Amounts","EXTRACT(DAY FROM \"Month\")=1 AND \"ContractDays\" BETWEEN 1 AND 31 AND \"MonthlySalary\">=0 AND \"BaseDue\">=0 AND \"Deductions\">=0 AND \"Additions\">=0 AND \"NetDue\">0 AND \"NetDue\"=\"BaseDue\"-\"Deductions\"+\"Additions\""));
        p.HasOne<PayrollStatement>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.StatementId,x.EmployeeId,x.BranchId,x.Currency}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.EmployeeId,x.BranchId,x.Currency}).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<PayrollAdvance>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.AdvanceId,x.EmployeeId,x.BranchId,x.Currency}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.EmployeeId,x.BranchId,x.Currency}).OnDelete(DeleteBehavior.Restrict);
        p.Property(x=>x.AdvanceAppliedBefore).HasPrecision(18,2);p.Property(x=>x.AdvanceRemainingAfter).HasPrecision(18,2);
        p.ToTable(t=>t.HasCheckConstraint("CK_PayrollDisbursement_Advance","(\"AdvanceId\" IS NULL AND \"AdvanceAppliedBefore\" IS NULL AND \"AdvanceRemainingAfter\" IS NULL) OR (\"AdvanceId\" IS NOT NULL AND \"AdvanceAppliedBefore\" IS NOT NULL AND \"AdvanceRemainingAfter\" IS NOT NULL AND \"AdvanceAppliedBefore\">=0 AND \"AdvanceRemainingAfter\">=0)"));
        p.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        p.HasIndex(x=>new{x.AcademyId,x.StatementId,x.Sequence}).IsUnique();p.HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestKey}).IsUnique();p.Property(x=>x.Currency).HasMaxLength(3);p.Property(x=>x.Reference).HasMaxLength(120);p.Property(x=>x.RequestKey).HasMaxLength(100);p.Property(x=>x.RequestHash).HasMaxLength(64);
        foreach(var name in new[]{nameof(PayrollDisbursement.Amount),nameof(PayrollDisbursement.PaidBefore),nameof(PayrollDisbursement.RemainingAfter)})p.Property<decimal>(name).HasPrecision(18,2);
        p.ToTable(t=>t.HasCheckConstraint("CK_PayrollDisbursement_Amounts","\"Amount\">0 AND \"PaidBefore\">=0 AND \"RemainingAfter\">=0 AND \"Sequence\">0"));
    }
}
