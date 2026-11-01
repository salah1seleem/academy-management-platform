using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<AudienceCommunication> AudienceCommunications => Set<AudienceCommunication>();
    public DbSet<AudienceAnnouncement> AudienceAnnouncements => Set<AudienceAnnouncement>();
    public DbSet<AudienceLinkAudit> AudienceLinkAudits => Set<AudienceLinkAudit>();
    private static void ConfigureAudienceCommunication(ModelBuilder b)
    {
        Tenant<AudienceCommunication>(b);Tenant<AudienceAnnouncement>(b);Tenant<AudienceLinkAudit>(b);
        b.Entity<AudienceCommunication>().Property(x=>x.InviteUrl).HasMaxLength(256);
        b.Entity<AudienceCommunication>().HasIndex(x=>new{x.AcademyId,x.TrainingGroupId}).IsUnique().HasFilter("\"TrainingGroupId\" IS NOT NULL");
        b.Entity<AudienceCommunication>().HasIndex(x=>new{x.AcademyId,x.TeamId}).IsUnique().HasFilter("\"TeamId\" IS NOT NULL");
        b.Entity<AudienceAnnouncement>().Property(x=>x.Message).HasMaxLength(1000);
        b.Entity<AudienceAnnouncement>().HasIndex(x=>new{x.AcademyId,x.ActorMembershipId,x.RequestId}).IsUnique();
        b.Entity<AudienceCommunication>().ToTable(t=>t.HasCheckConstraint("CK_AudienceCommunication_Target","(\"TrainingGroupId\" IS NULL) <> (\"TeamId\" IS NULL)"));
        b.Entity<AudienceAnnouncement>().ToTable(t=>t.HasCheckConstraint("CK_AudienceAnnouncement_Target","(\"TrainingGroupId\" IS NULL) <> (\"TeamId\" IS NULL)"));
        b.Entity<AudienceCommunication>().HasOne<TrainingGroup>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingGroupId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceCommunication>().HasOne<MatchTeam>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TeamId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceAnnouncement>().HasOne<TrainingGroup>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingGroupId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceAnnouncement>().HasOne<MatchTeam>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TeamId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceAnnouncement>().HasOne<AcademyMatch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.MatchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceCommunication>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.UpdatedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceAnnouncement>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceLinkAudit>().HasOne<AudienceCommunication>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CommunicationId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AudienceLinkAudit>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ActorMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
