using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<PlayerCard> PlayerCards => Set<PlayerCard>();
    private static void ConfigurePlayerCards(ModelBuilder b)
    {
        Tenant<PlayerCard>(b);
        b.Entity<PlayerCard>(e => {
            e.Property(x => x.TokenHash).HasMaxLength(64); e.Property(x => x.ProtectedToken).HasMaxLength(1200); e.Property(x => x.RequestKey).HasMaxLength(100);
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.AcademyId, x.PlayerId }).IsUnique().HasFilter("\"IsActive\" = TRUE");
            e.HasIndex(x => new { x.AcademyId, x.IssuedByUserId, x.RequestKey }).IsUnique();
            e.HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict); e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_PlayerCard_State", "length(\"TokenHash\")=64 AND \"ExpiresAtUtc\">\"CreatedAtUtc\" AND ((\"IsActive\" AND \"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL))"));
        });
    }
}
