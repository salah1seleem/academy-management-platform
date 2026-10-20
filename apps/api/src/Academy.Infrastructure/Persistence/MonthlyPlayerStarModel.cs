using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<MonthlyPlayerStar> MonthlyPlayerStars=>Set<MonthlyPlayerStar>();
    private static void ConfigureMonthlyPlayerStars(ModelBuilder b)
    {
        Tenant<MonthlyPlayerStar>(b);
        var e=b.Entity<MonthlyPlayerStar>();
        e.HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CoachMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<MatchTeam>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TeamId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Player>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.PlayerId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.AcademyId,x.CoachMembershipId,x.RequestId}).IsUnique();
        e.HasIndex(x=>new{x.AcademyId,x.CoachMembershipId,x.TeamId,x.Month,x.Slot}).IsUnique();
        e.ToTable(t=>t.HasCheckConstraint("CK_MonthlyPlayerStar_Budget","\"IsActive\" AND \"Slot\" BETWEEN 1 AND 3 AND EXTRACT(DAY FROM \"Month\")=1"));
    }
}
