using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<MembershipBranchAccess> MembershipBranchAccesses => Set<MembershipBranchAccess>();
    public DbSet<StaffActionGrant> StaffActionGrants => Set<StaffActionGrant>();
    public DbSet<StaffAccessAudit> StaffAccessAudits => Set<StaffAccessAudit>();
    public DbSet<MembershipSportAccess> MembershipSportAccesses => Set<MembershipSportAccess>();
    private static void ConfigureStaffAccess(ModelBuilder b)
    {
        Tenant<MembershipBranchAccess>(b); Tenant<StaffActionGrant>(b); Tenant<StaffAccessAudit>(b);
        b.Entity<MembershipBranchAccess>().ToTable("MembershipBranchAccess");
        b.Entity<AcademyMembership>().Property(x => x.AllBranches).HasDefaultValue(true);
        b.Entity<AcademyMembership>().Property(x => x.AllSports).HasDefaultValue(true);
        Tenant<MembershipSportAccess>(b);
        b.Entity<MembershipSportAccess>().ToTable("MembershipSportAccess");
        b.Entity<MembershipSportAccess>().HasIndex(x => new { x.AcademyId, x.MembershipId, x.SportId }).IsUnique();
        b.Entity<MembershipSportAccess>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MembershipSportAccess>().HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MembershipBranchAccess>().HasIndex(x => new { x.AcademyId, x.MembershipId, x.BranchId }).IsUnique();
        b.Entity<MembershipBranchAccess>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MembershipBranchAccess>().HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffActionGrant>().Property(x => x.Module).HasMaxLength(40);
        b.Entity<StaffActionGrant>().HasIndex(x => new { x.AcademyId, x.MembershipId, x.Module }).IsUnique();
        b.Entity<StaffActionGrant>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffAccessAudit>().Property(x => x.Before).HasColumnType("jsonb"); b.Entity<StaffAccessAudit>().Property(x => x.After).HasColumnType("jsonb");
        b.Entity<StaffAccessAudit>().HasOne<Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffAccessAudit>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
