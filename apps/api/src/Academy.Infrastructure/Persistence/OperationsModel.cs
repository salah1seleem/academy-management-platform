using Academy.Infrastructure.Content;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<PrivateAsset> PrivateAssets => Set<PrivateAsset>();
    public DbSet<AcademyExperience> AcademyExperiences => Set<AcademyExperience>();
    public DbSet<PlatformBrandOperator> PlatformBrandOperators => Set<PlatformBrandOperator>();
    public DbSet<BrandingAudit> BrandingAudits => Set<BrandingAudit>();
    public DbSet<AcademyPolicyAudit> AcademyPolicyAudits => Set<AcademyPolicyAudit>();
    public DbSet<PrivateAssetAudience> PrivateAssetAudiences => Set<PrivateAssetAudience>();
    public DbSet<AcademyTransferSettings> AcademyTransferSettings => Set<AcademyTransferSettings>();
    public DbSet<PaymentProof> PaymentProofs => Set<PaymentProof>();
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<PlayerDiscountLink> PlayerDiscountLinks => Set<PlayerDiscountLink>();
    public DbSet<CollectionRefund> CollectionRefunds => Set<CollectionRefund>();
    public DbSet<ProductSale> ProductSales => Set<ProductSale>();
    public DbSet<AcademyNotice> AcademyNotices => Set<AcademyNotice>();
    public DbSet<AcademyNews> AcademyNews => Set<AcademyNews>();
    public DbSet<StaffGrant> StaffGrants => Set<StaffGrant>();
    public DbSet<StaffWorkSchedule> StaffWorkSchedules => Set<StaffWorkSchedule>();
    public DbSet<StaffDailyAttendance> StaffDailyAttendances => Set<StaffDailyAttendance>();

    private static void ConfigureOperations(ModelBuilder b)
    {
        ConfigureVersionedSettings(b);
        ConfigurePlayerCards(b);
        ConfigureObservations(b);
        ConfigureVisits(b);
        ConfigureMatches(b);
        ConfigurePayroll(b);
        Tenant<AcademyExperience>(b);
        Tenant<AcademyPolicyAudit>(b);
        b.Entity<AcademyPolicyAudit>().Property(x => x.Policy).HasMaxLength(80);
        b.Entity<AcademyPolicyAudit>().HasOne<Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyExperience>().HasIndex(x => x.AcademyId).IsUnique();
        b.Entity<AcademyExperience>().Property(x => x.DisplayName).HasMaxLength(120);
        b.Entity<AcademyExperience>().Property(x => x.PrimaryColor).HasMaxLength(7);
        b.Entity<AcademyExperience>().Property(x => x.SecondaryColor).HasMaxLength(7);
        b.Entity<PlatformBrandOperator>().HasKey(x => x.UserId);
        b.Entity<PlatformBrandOperator>().HasOne<Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BrandingAudit>().HasKey(x => x.Id);
        b.Entity<BrandingAudit>().HasOne<Tenancy.Academy>().WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BrandingAudit>().HasOne<Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        Tenant<PrivateAsset>(b); Tenant<PrivateAssetAudience>(b); Tenant<AcademyTransferSettings>(b); Tenant<PaymentProof>(b);
        Tenant<DiscountRule>(b); Tenant<PlayerDiscountLink>(b); Tenant<CollectionRefund>(b);
        Tenant<ProductSale>(b); Tenant<AcademyNotice>(b); Tenant<AcademyNews>(b);
        Tenant<StaffGrant>(b); Tenant<StaffWorkSchedule>(b); Tenant<StaffDailyAttendance>(b);
        b.Entity<PrivateAsset>().Property(x => x.StorageName).HasMaxLength(100);
        b.Entity<RenewalRequest>().Property(x => x.RegistrationPayloadHash).HasMaxLength(64);
        b.Entity<PrivateAssetAudience>().HasIndex(x => new { x.AcademyId, x.AssetId, x.PlayerId }).IsUnique();
        b.Entity<PrivateAssetAudience>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PrivateAssetAudience>().HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PrivateAsset>().HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PrivateAsset>().HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyTransferSettings>().HasIndex(x => x.AcademyId).IsUnique();
        b.Entity<PaymentProof>().HasOne<PaymentRequest>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PaymentRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PaymentProof>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PaymentProof>().HasIndex(x => new { x.AcademyId, x.PaymentRequestId }).IsUnique();
        b.Entity<DiscountRule>().HasIndex(x => new { x.AcademyId, x.ArabicName }).IsUnique();
        b.Entity<DiscountRule>().Property(x => x.Percentage).HasPrecision(5, 2);
        b.Entity<PlayerDiscountLink>().HasIndex(x => new { x.AcademyId, x.PlayerId, x.DiscountRuleId }).IsUnique();
        b.Entity<PlayerDiscountLink>().HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PlayerDiscountLink>().HasOne<DiscountRule>().WithMany().HasForeignKey(x => new { x.AcademyId, x.DiscountRuleId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CollectionRefund>().HasIndex(x => new { x.AcademyId, x.PerformedByUserId, x.IdempotencyKey }).IsUnique();
        b.Entity<CollectionRefund>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<CollectionRefund>().HasOne<PaymentCollection>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CollectionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductSale>().HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
        b.Entity<ProductSale>().HasOne<SportCatalogItem>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ProductId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductSale>().Property(x => x.UnitPrice).HasPrecision(18, 2); b.Entity<ProductSale>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<ProductSale>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ProofAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductSale>().HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductSale>().HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductSale>().HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyNews>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ImageAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NewEnrollmentRequest>().Property(x => x.OriginalAmount).HasPrecision(18, 2); b.Entity<NewEnrollmentRequest>().Property(x => x.FinalAmount).HasPrecision(18, 2);
        b.Entity<NewEnrollmentRequest>().HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SubscriptionPlanId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NewEnrollmentRequest>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ProofAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<NewEnrollmentRequest>().HasOne<PaymentRequest>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PaymentRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffGrant>().HasIndex(x => new { x.AcademyId, x.MembershipId, x.Permission }).IsUnique();
        b.Entity<StaffGrant>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffWorkSchedule>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffWorkSchedule>().Property(x => x.LateDeduction).HasPrecision(18, 2);
        b.Entity<StaffDailyAttendance>().HasIndex(x => new { x.AcademyId, x.MembershipId, x.WorkDate }).IsUnique();
        b.Entity<StaffDailyAttendance>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StaffDailyAttendance>().Property(x => x.SuggestedDeduction).HasPrecision(18, 2); b.Entity<StaffDailyAttendance>().Property(x => x.ApprovedDeduction).HasPrecision(18, 2);
    }
    private static void Tenant<T>(ModelBuilder b) where T : TenantEntity
    {
        var e = b.Entity<T>(); e.ToTable(typeof(T).Name + "s"); e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.AcademyId, x.Id }); e.Property(x => x.Version).IsRowVersion();
        e.HasOne<Tenancy.Academy>().WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
    }
}
