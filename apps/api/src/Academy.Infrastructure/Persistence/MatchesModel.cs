using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<AcademyTournament> AcademyTournaments => Set<AcademyTournament>();
    public DbSet<MatchTeam> MatchTeams => Set<MatchTeam>();
    public DbSet<MatchTeamPlayer> MatchTeamPlayers => Set<MatchTeamPlayer>();
    public DbSet<AcademyMatch> AcademyMatches => Set<AcademyMatch>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<TournamentPayment> TournamentPayments => Set<TournamentPayment>();
    private static void ConfigureMatches(ModelBuilder b)
    {
        Tenant<AcademyTournament>(b); Tenant<MatchTeam>(b); Tenant<MatchTeamPlayer>(b); Tenant<AcademyMatch>(b); Tenant<MatchParticipant>(b); Tenant<TournamentPayment>(b);
        b.Entity<AcademyTournament>().Property(x => x.Name).HasMaxLength(120); b.Entity<AcademyTournament>().Property(x => x.Fee).HasPrecision(18, 2);
        b.Entity<AcademyTournament>().HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeam>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<MatchTeam>().HasOne<Sport>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeam>().HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeam>().HasOne<AgeCategory>().WithMany().HasForeignKey(x => new { x.AcademyId, x.AgeCategoryId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeam>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CoachMembershipId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeamPlayer>().HasIndex(x => new { x.AcademyId, x.TeamId, x.EnrollmentId }).IsUnique();
        b.Entity<MatchTeamPlayer>().HasOne<MatchTeam>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TeamId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchTeamPlayer>().HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyMatch>().HasOne<AcademyTournament>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TournamentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyMatch>().HasOne<MatchTeam>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TeamId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyMatch>().HasOne<AcademyMatch>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CopiedFromMatchId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyMatch>().HasOne<AcademyMembership>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CopiedByMembershipId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AcademyMatch>().Property(x=>x.CopyRequestKey).HasMaxLength(100);b.Entity<AcademyMatch>().Property(x=>x.CopyRequestHash).HasMaxLength(64);
        b.Entity<AcademyMatch>().HasIndex(x=>new{x.AcademyId,x.CopiedByMembershipId,x.CopyRequestKey}).IsUnique().HasFilter("\"CopiedFromMatchId\" IS NOT NULL");
        b.Entity<AcademyMatch>().ToTable(t=>t.HasCheckConstraint("CK_Match_Copy","(\"CopiedFromMatchId\" IS NULL AND \"CopiedByMembershipId\" IS NULL AND \"CopyRequestKey\" IS NULL AND \"CopyRequestHash\" IS NULL) OR (\"CopiedFromMatchId\" IS NOT NULL AND \"CopiedFromMatchId\"<>\"Id\" AND \"CopiedByMembershipId\" IS NOT NULL AND \"CopyRequestKey\" IS NOT NULL AND length(\"CopyRequestKey\") BETWEEN 8 AND 100 AND \"CopyRequestHash\" IS NOT NULL AND length(\"CopyRequestHash\")=64)"));
        b.Entity<MatchParticipant>().HasIndex(x => new { x.AcademyId, x.MatchId, x.PlayerId }).IsUnique();
        b.Entity<MatchParticipant>().HasOne<AcademyMatch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.MatchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatchParticipant>().HasOne<Player>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlayerId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TournamentPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<TournamentPayment>().HasIndex(x => new { x.AcademyId, x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
        b.Entity<TournamentPayment>().HasIndex(x => new { x.AcademyId, x.EnrollmentId, x.TournamentId }).IsUnique();
        b.Entity<TournamentPayment>().HasOne<AcademyTournament>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TournamentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TournamentPayment>().HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.EnrollmentId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TournamentPayment>().HasOne<PrivateAsset>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ProofAssetId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
