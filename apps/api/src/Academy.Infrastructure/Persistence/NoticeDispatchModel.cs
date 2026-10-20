using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<NoticeTemplate> NoticeTemplates => Set<NoticeTemplate>();
    public DbSet<NoticeDispatch> NoticeDispatches => Set<NoticeDispatch>();
    public DbSet<PageHelpVideo> PageHelpVideos => Set<PageHelpVideo>();
    private static void ConfigureNoticeDispatch(ModelBuilder b)
    {
        Tenant<NoticeTemplate>(b);Tenant<NoticeDispatch>(b);
        Tenant<PageHelpVideo>(b);
        b.Entity<PageHelpVideo>().HasIndex(x=>new{x.AcademyId,x.Role,x.Screen}).IsUnique();
        b.Entity<PageHelpVideo>().Property(x=>x.Role).HasMaxLength(24);
        b.Entity<PageHelpVideo>().Property(x=>x.Screen).HasMaxLength(32);
        b.Entity<PageHelpVideo>().Property(x=>x.Title).HasMaxLength(120);
        b.Entity<PageHelpVideo>().Property(x=>x.Url).HasMaxLength(2000);
        b.Entity<NoticeTemplate>().HasIndex(x=>new{x.AcademyId,x.Key}).IsUnique();
        b.Entity<NoticeTemplate>().Property(x=>x.Key).HasMaxLength(40);
        b.Entity<NoticeTemplate>().Property(x=>x.Message).HasMaxLength(1000);
        b.Entity<NoticeDispatch>().HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestId}).IsUnique();
        b.Entity<NoticeDispatch>().Property(x=>x.RequestHash).HasMaxLength(64);
        b.Entity<NoticeDispatch>().Property(x=>x.Kind).HasMaxLength(40);
        b.Entity<NoticeDispatch>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
