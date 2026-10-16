using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<StaffPasskey> StaffPasskeys => Set<StaffPasskey>();
    public DbSet<StaffPasskeyChallenge> StaffPasskeyChallenges => Set<StaffPasskeyChallenge>();
    private static void ConfigureStaffPasskeys(ModelBuilder b)
    {
        Tenant<StaffPasskey>(b);Tenant<StaffPasskeyChallenge>(b);
        var key=b.Entity<StaffPasskey>();var challenge=b.Entity<StaffPasskeyChallenge>();
        key.Property(x=>x.Name).HasMaxLength(80);key.Property(x=>x.CredentialHash).HasMaxLength(64);key.Property(x=>x.RpId).HasMaxLength(253);
        key.HasIndex(x=>new{x.AcademyId,x.CredentialHash}).IsUnique();
        key.HasIndex(x=>new{x.AcademyId,x.RegistrationChallengeId}).IsUnique();
        key.HasOne<StaffPasskeyChallenge>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RegistrationChallengeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        key.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.MembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        key.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        challenge.Property(x=>x.Purpose).HasMaxLength(16);challenge.Property(x=>x.Name).HasMaxLength(80);challenge.Property(x=>x.RpId).HasMaxLength(253);
        challenge.Property(x=>x.OptionsJson).HasColumnType("jsonb");challenge.Property(x=>x.OriginsJson).HasColumnType("jsonb");
        challenge.HasIndex(x=>new{x.AcademyId,x.MembershipId,x.ExpiresAtUtc});
        challenge.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.MembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        challenge.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        key.ToTable(t=>t.HasCheckConstraint("CK_StaffPasskey_State","\"SignCount\">=0 AND length(\"CredentialHash\")=64 AND octet_length(\"CredentialId\") BETWEEN 1 AND 1024 AND octet_length(\"PublicKey\") BETWEEN 1 AND 4096 AND octet_length(\"UserHandle\")=16 AND ((\"IsActive\" AND \"RevokedAtUtc\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAtUtc\" IS NOT NULL))"));
        challenge.ToTable(t=>t.HasCheckConstraint("CK_StaffPasskeyChallenge_State","\"IsActive\" AND \"Purpose\" IN ('Register','Verify') AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND (\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\">=\"CreatedAtUtc\") AND (\"VerifiedAtUtc\" IS NULL OR (\"ConsumedAtUtc\" IS NOT NULL AND \"VerifiedAtUtc\">=\"ConsumedAtUtc\"))"));
    }
}
