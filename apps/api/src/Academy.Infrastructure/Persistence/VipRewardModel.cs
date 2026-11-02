using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<VipReward> VipRewards=>Set<VipReward>();
    public DbSet<VipRewardUse> VipRewardUses=>Set<VipRewardUse>();
    private void GuardVipRewards()
    {
        foreach(var e in ChangeTracker.Entries<VipReward>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.OriginalValues.GetValue<DateTimeOffset?>(nameof(VipReward.CancelledAtUtc))!=null||e.Entity.CancelledAtUtc==null||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(VipReward.CancelledAtUtc) or nameof(VipReward.CancelledByMembershipId) or nameof(VipReward.UpdatedAtUtc))))throw new InvalidOperationException("شروط المكافأة وإصدارها محفوظان؛ ألغِ غير المحجوزة دون تعديل التاريخ.");
        foreach(var e in ChangeTracker.Entries<VipRewardUse>().Where(e=>e.State is EntityState.Modified or EntityState.Deleted))
            if(e.State==EntityState.Deleted||e.OriginalValues.GetValue<DateTimeOffset?>(nameof(VipRewardUse.UsedAtUtc))!=null||e.OriginalValues.GetValue<DateTimeOffset?>(nameof(VipRewardUse.ReleasedAtUtc))!=null||e.Properties.Any(p=>p.IsModified&&p.Metadata.Name is not(nameof(VipRewardUse.UsedAtUtc) or nameof(VipRewardUse.ReleasedAtUtc) or nameof(VipRewardUse.UpdatedAtUtc))))throw new InvalidOperationException("حجز المكافأة وأثر استخدامها محفوظان دون إعادة كتابة.");
    }
    private static void ConfigureVipRewards(ModelBuilder b)
    {
        Tenant<VipReward>(b);Tenant<VipRewardUse>(b);
        var r=b.Entity<VipReward>();r.Property(x=>x.Percentage).HasPrecision(5,2);r.Property(x=>x.Currency).HasMaxLength(3);r.Property(x=>x.Message).HasMaxLength(500);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.HasOne<GuardianProfile>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.GuardianId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<DiscountRule>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.DiscountRuleId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<AcademyNotice>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.NoticeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.IssuedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CancelledByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.DiscountRuleId}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.IssuedByMembershipId,x.RequestId,x.GuardianId}).IsUnique();
        r.ToTable(t=>t.HasCheckConstraint("CK_VipReward_Terms","\"IsActive\" AND \"Percentage\">0 AND \"Percentage\"<100 AND \"ExpiresOn\">=\"IssuedOn\" AND (\"CancelledAtUtc\" IS NULL)=(\"CancelledByMembershipId\" IS NULL)"));
        var u=b.Entity<VipRewardUse>();u.Property(x=>x.SourceKind).HasMaxLength(16);u.HasOne<VipReward>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.RewardId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        u.HasIndex(x=>new{x.AcademyId,x.RewardId}).IsUnique().HasFilter("\"ReleasedAtUtc\" IS NULL");
        u.ToTable(t=>t.HasCheckConstraint("CK_VipRewardUse_Terms","\"IsActive\" AND \"SourceKind\" IN ('Renewal','Family','Enrollment') AND NOT (\"UsedAtUtc\" IS NOT NULL AND \"ReleasedAtUtc\" IS NOT NULL)"));
        b.Entity<RenewalRequest>().HasOne<VipRewardUse>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.VipRewardUseId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FamilyRenewalRequest>().HasOne<VipRewardUse>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.VipRewardUseId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
