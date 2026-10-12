using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<SettlementComparison> SettlementComparisons => Set<SettlementComparison>();
    private static void ConfigureSettlementComparisons(ModelBuilder b)
    {
        ConfigureTenantEntity<SettlementComparison>(b, "SettlementComparisons");
        b.Entity<SettlementComparison>(e => {
            foreach (var name in new[] { nameof(SettlementComparison.SourceKind), nameof(SettlementComparison.Result) }) e.Property<string>(name).HasMaxLength(30);
            foreach (var name in new[] { nameof(SettlementComparison.AccountTag), nameof(SettlementComparison.Provider) }) e.Property<string>(name).HasMaxLength(60);
            foreach (var name in new[] { nameof(SettlementComparison.StatementReference), nameof(SettlementComparison.LineReference), nameof(SettlementComparison.PaymentReference) }) e.Property<string>(name).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(300); e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.ExpectedCurrency).HasMaxLength(3).IsFixedLength();
            foreach (var name in new[] { nameof(SettlementComparison.GrossAmount), nameof(SettlementComparison.FeeAmount), nameof(SettlementComparison.NetAmount), nameof(SettlementComparison.ExpectedAmount) }) e.Property(name).HasPrecision(18, 2);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentCollection>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CollectionId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.SourceKind, x.AccountTag, x.StatementReference, x.LineReference }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.SourceKind, x.AccountTag, x.CollectionId }).IsUnique().HasFilter("\"Result\" = 'Matched' AND \"CollectionId\" IS NOT NULL");
            e.HasIndex(x => new { x.AcademyId, x.SettlementDate });
        });
    }
}
