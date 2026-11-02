using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<MobilePushDevice> MobilePushDevices=>Set<MobilePushDevice>();
    public DbSet<MobilePushDelivery> MobilePushDeliveries=>Set<MobilePushDelivery>();
    private static void ConfigureMobilePush(ModelBuilder b)
    {
        var d=b.Entity<MobilePushDevice>();d.HasKey(x=>x.Id);d.HasIndex(x=>x.TokenHash).IsUnique();
        d.Property(x=>x.Provider).HasMaxLength(8);d.Property(x=>x.TokenHash).HasMaxLength(64);d.Property(x=>x.ProtectedToken).HasMaxLength(4096);
        d.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        d.HasOne<MobileSession>().WithMany().HasForeignKey(x=>new{x.MobileSessionId,x.UserId}).HasPrincipalKey(x=>new{x.Id,x.UserId}).OnDelete(DeleteBehavior.Restrict);
        Tenant<MobilePushDelivery>(b);var q=b.Entity<MobilePushDelivery>();q.HasIndex(x=>new{x.AcademyId,x.NoticeId,x.DeviceId}).IsUnique();q.Property(x=>x.State).HasMaxLength(16);
        q.HasOne<AcademyNotice>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.NoticeId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        q.HasOne<MobilePushDevice>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
