using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerBadgeRule> PlayerBadgeRules=>Set<PlayerBadgeRule>();
    public DbSet<PlayerBadgeRuleRetirement> PlayerBadgeRuleRetirements=>Set<PlayerBadgeRuleRetirement>();
    public DbSet<PlayerBadgeGrant> PlayerBadgeGrants=>Set<PlayerBadgeGrant>();
    public DbSet<PlayerBadgeRevocation> PlayerBadgeRevocations=>Set<PlayerBadgeRevocation>();
    private static void BadgeRecord<T>(ModelBuilder b)where T:TenantEntity,IBadgeRecord
    {
        Tenant<T>(b);var e=b.Entity<T>();e.Property(x=>x.ActorName).HasMaxLength(160);e.Property(x=>x.RequestHash).HasMaxLength(64);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);e.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
    }
    private static void ConfigurePlayerBadges(ModelBuilder b)
    {
        BadgeRecord<PlayerBadgeRule>(b);BadgeRecord<PlayerBadgeRuleRetirement>(b);BadgeRecord<PlayerBadgeGrant>(b);BadgeRecord<PlayerBadgeRevocation>(b);
        var r=b.Entity<PlayerBadgeRule>();r.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportId});r.HasOne<Sport>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);r.Property(x=>x.Title).HasMaxLength(100);r.Property(x=>x.Criterion).HasMaxLength(500);r.ToTable(t=>t.HasCheckConstraint("CK_PlayerBadgeRule_Encouragement","\"IsActive\" AND \"Points\" BETWEEN 0 AND 1000 AND length(trim(\"Title\"))>0 AND length(trim(\"Criterion\"))>0"));
        var t=b.Entity<PlayerBadgeRuleRetirement>();t.HasOne<PlayerBadgeRule>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RuleId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);t.HasIndex(x=>new{x.AcademyId,x.RuleId}).IsUnique();t.Property(x=>x.Note).HasMaxLength(500);t.ToTable(x=>x.HasCheckConstraint("CK_PlayerBadgeRetirement_Active","\"IsActive\""));
        var g=b.Entity<PlayerBadgeGrant>();g.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId});g.HasOne<PlayerBadgeRule>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RuleId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);g.HasOne<SportEnrollment>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);g.HasIndex(x=>new{x.AcademyId,x.SportEnrollmentId,x.RuleId}).IsUnique();g.Property(x=>x.FamilyNote).HasMaxLength(500);g.ToTable(x=>x.HasCheckConstraint("CK_PlayerBadgeGrant_Active","\"IsActive\""));
        var v=b.Entity<PlayerBadgeRevocation>();v.HasOne<PlayerBadgeGrant>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.GrantId,x.SportEnrollmentId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportEnrollmentId,x.SportId}).OnDelete(DeleteBehavior.Restrict);v.HasIndex(x=>new{x.AcademyId,x.GrantId}).IsUnique();v.Property(x=>x.FamilyNote).HasMaxLength(500);v.ToTable(x=>x.HasCheckConstraint("CK_PlayerBadgeRevocation_Active","\"IsActive\""));
    }
}
