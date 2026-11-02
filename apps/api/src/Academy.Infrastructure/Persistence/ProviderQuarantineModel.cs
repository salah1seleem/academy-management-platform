using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<ProviderEventQuarantine> ProviderEventQuarantines => Set<ProviderEventQuarantine>();
    private static void ConfigureProviderQuarantine(ModelBuilder b)
    {
        ConfigureTenantEntity<ProviderEventQuarantine>(b, "ProviderEventQuarantines");
        b.Entity<ProviderEventQuarantine>(e => {
            e.Property(x => x.Provider).HasMaxLength(60);
            e.Property(x => x.ProviderEventId).HasMaxLength(100);
            e.Property(x => x.EnvelopeHash).HasMaxLength(64);
            e.Property(x => x.ReasonCode).HasMaxLength(60);
            e.Property(x => x.ReceivedStatus).HasMaxLength(50);
            e.Property(x => x.ReceivedReference).HasMaxLength(100);
            e.Property(x => x.ExpectedReference).HasMaxLength(100);
            e.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
            e.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
            e.Property(x => x.ReceivedCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ExpectedCurrency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => new { x.AcademyId, x.Provider, x.ProviderEventId, x.EnvelopeHash }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.CreatedAtUtc });
            e.HasOne(x => x.PaymentRequest).WithMany().HasForeignKey(x => new { x.AcademyId, x.PaymentRequestId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
