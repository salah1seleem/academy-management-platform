using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();
    private static void ConfigureContractHistory(ModelBuilder b)
    {
        Tenant<ContractVersion>(b); var s = b.Entity<ContractVersion>();
        s.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EmployeeId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ApprovedByMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<ContractVersion>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PreviousVersionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasIndex(x => new { x.AcademyId, x.EmployeeId, x.Revision }).IsUnique();
        s.HasIndex(x => new { x.AcademyId, x.EmployeeId, x.EffectiveFrom });
        s.HasIndex(x => new { x.AcademyId, x.ApprovedByMembershipId, x.RequestKey }).IsUnique();
        s.Property(x => x.Kind).HasMaxLength(20); s.Property(x => x.Note).HasMaxLength(500); s.Property(x => x.RequestKey).HasMaxLength(100); s.Property(x => x.RequestHash).HasMaxLength(64);
        s.ToTable(t => t.HasCheckConstraint("CK_ContractVersion_Terms", "\"Revision\">0 AND EXTRACT(DAY FROM \"EffectiveFrom\")=1 AND (\"ContractEnd\" IS NULL OR \"ContractEnd\">=\"ContractStart\") AND \"Kind\" IN ('Initial','LegacyBaseline','Change') AND ((\"Revision\"=1 AND \"PreviousVersionId\" IS NULL AND \"Kind\"<>'Change') OR (\"Revision\">1 AND \"PreviousVersionId\" IS NOT NULL AND \"Kind\"='Change'))"));
    }
}
