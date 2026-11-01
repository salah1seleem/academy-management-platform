using Academy.Api.Slice4;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed partial class ConnectedImportPlan
{
    public async Task PublishStructure(FoundationDbContext db,DateTimeOffset now, Guid jobId)
    {
        foreach(var entity in Creates){
            if(entity is ImportedHousehold family && family.JobId == Guid.Empty) family.JobId = jobId;
            if(entity is TenantEntity t){t.CreatedAtUtc=now;t.UpdatedAtUtc=now;}
            if(entity is ApplicationUser user){user.CreatedAtUtc=now;user.UpdatedAtUtc=now;}
            if(entity is AcademyMembership member){member.CreatedAtUtc=now;member.UpdatedAtUtc=now;}
            db.Add(entity);
        }
        await db.SaveChangesAsync();
    }
    public async Task PublishRelationships(FoundationDbContext db,AcademyImportJob job,TrainingSessionGenerator sessions,CancellationToken token)
    {
        var mapped=await db.ImportedEnrollments.Where(m=>m.SourceSystem==job.SourceSystem&&job.LegacyEnrollmentIds.Contains(m.LegacyEnrollmentId)).ToDictionaryAsync(m=>m.LegacyEnrollmentId,token);
        var enrollments=mapped.Values.Select(m=>m.SportEnrollmentId).ToArray();
        var existingTeams=await db.MatchTeamPlayers.Where(m=>enrollments.Contains(m.EnrollmentId)).ToListAsync(token);
        var existingEntries=await db.TournamentRegistrations.Where(m=>enrollments.Contains(m.EnrollmentId)).ToListAsync(token);
        var playerIds=mapped.Values.Select(m=>m.PlayerId).ToArray();
        var existingOffers=await db.PlayerDiscountLinks.Where(m=>playerIds.Contains(m.PlayerId)).ToListAsync(token);
        foreach(var (code,extra) in Members){var m=mapped[code];
            if(extra.Team is Guid existingTeam && existingTeams.Any(t=>t.TeamId==existingTeam&&t.EnrollmentId==m.SportEnrollmentId&&!t.IsActive))throw new ImportRejected("conflict");
            if(extra.Team is Guid team&&!existingTeams.Any(t=>t.TeamId==team&&t.EnrollmentId==m.SportEnrollmentId)){
                var entry=new MatchTeamPlayer{Id=Id(job.AcademyId,job.SourceSystem,"team-player",team+":"+code),AcademyId=job.AcademyId,TeamId=team,EnrollmentId=m.SportEnrollmentId,CreatedAtUtc=job.CreatedAtUtc,UpdatedAtUtc=job.CreatedAtUtc};
                db.MatchTeamPlayers.Add(entry);existingTeams.Add(entry);
            }
            foreach(var tournament in extra.Tournaments)if(!existingEntries.Any(t=>t.TournamentId==tournament&&t.EnrollmentId==m.SportEnrollmentId)){
                var entry=new TournamentRegistration{Id=Id(job.AcademyId,job.SourceSystem,"participation",tournament+":"+code),AcademyId=job.AcademyId,TournamentId=tournament,TeamId=extra.Team!.Value,EnrollmentId=m.SportEnrollmentId,ImportJobId=job.Id,CreatedAtUtc=job.CreatedAtUtc,UpdatedAtUtc=job.CreatedAtUtc};db.TournamentRegistrations.Add(entry);existingEntries.Add(entry);
            }
            if(extra.Offer is Guid inactiveOffer&&existingOffers.Any(o=>o.PlayerId==m.PlayerId&&o.DiscountRuleId==inactiveOffer&&!o.IsActive))throw new ImportRejected("conflict");
            if(extra.Offer is Guid offer&&!existingOffers.Any(o=>o.PlayerId==m.PlayerId&&o.DiscountRuleId==offer)){
                var link=new PlayerDiscountLink{Id=Id(job.AcademyId,job.SourceSystem,"player-offer",m.LegacyPlayerId+":"+offer),AcademyId=job.AcademyId,PlayerId=m.PlayerId,DiscountRuleId=offer,CreatedAtUtc=job.CreatedAtUtc,UpdatedAtUtc=job.CreatedAtUtc};db.PlayerDiscountLinks.Add(link);existingOffers.Add(link);
            }
        }
        await db.SaveChangesAsync(token);
        // The existing generator reserves actual resources and checks actual coach conflicts.
        // Its transaction joins the import transaction. No unrelated group's sessions are generated.
        foreach(var range in Schedules.GroupBy(s=>(s.ValidFrom,s.ValidTo))){var ids=range.Select(s=>s.TrainingGroupId).Distinct().ToArray();
            for(var from=range.Key.ValidFrom!.Value;from<=range.Key.ValidTo!.Value;from=from.AddDays(31)){
                var to=from.AddDays(30);if(to>range.Key.ValidTo.Value)to=range.Key.ValidTo.Value;
                await sessions.GenerateAsync(job.AcademyId,job.ActorUserId,from,to,token,ids,job.PlatformInitiated ? job.Id : null);
            }
        }
    }
}
