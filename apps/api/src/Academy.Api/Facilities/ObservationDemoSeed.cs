using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
// Explicit LegacyRegression opt-in. The real academy day is an input, not a reset clock.
public static class ObservationDemoSeed
{
    public static Guid DailyId(DateOnly day, string kind) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"academity-demo-observation:{day:yyyy-MM-dd}:{kind}"))[..16]);
    public static async Task Seed(FoundationDbContext db, CollectionService collections, TimeProvider clock)
    {
        var academy = DemoSeed.NogoomAcademyId; var now = clock.GetUtcNow(); var local=TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")); var day=DateOnly.FromDateTime(local.DateTime); var sessionId=DailyId(day,"session"); var bookingId=DailyId(day,"booking");
        await using var tx=await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+academy},0))");
        var source=await db.TrainingGroups.SingleOrDefaultAsync(g=>g.Id==GroupPlacementDemoSeed.Id(1) && g.AcademyId==academy); var resource=await db.FacilityResources.SingleOrDefaultAsync(r=>r.Id==Guid.Parse("58000000-0000-0000-0000-000000000010") && r.AcademyId==academy && r.IsActive && r.ApprovedAtUtc!=null);
        if(source is null || resource is null || resource.Capacity<6 || !resource.OperatingDays.Contains((int)day.DayOfWeek) || !await db.Academies.AnyAsync(a=>a.Id==academy && a.Slug=="nogoom-demo" && a.IsActive) || await db.TrainingSessions.AnyAsync(s=>s.Id==sessionId)) return;
        if (!await SwimmingPolicyGate.AllowsSyntheticPoolSeed(db, source.SportId, source.BranchId, "Training")) return;
        var config=await db.AcademyConfigurationVersions.Where(c=>c.AcademyId==academy && c.State==ConfigurationState.Active).Select(c=>c.Payload).SingleAsync(); if(!AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(e=>e.AcademyId==academy && e.Feature=="facilities" && e.Enabled && e.IsActive)) return;
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync(); var coach=await db.AcademyMemberships.Where(m=>m.AcademyId==academy && m.User.Email==DemoSeed.CoachEmail && m.IsActive).Select(m=>m.Id).SingleAsync();
        var start=new TimeOnly(10,0); var end=new TimeOnly(11,0);
        if(start<resource.OpensAt || end>resource.ClosesAt || await db.ResourceClosures.AnyAsync(c=>c.ResourceId==resource.Id && c.IsActive && c.FromDate<=day && c.ToDate>=day && c.StartTime<end && start<c.EndTime)) return;
        var existing=await db.TrainingGroups.SingleOrDefaultAsync(g=>g.Id==GroupPlacementDemoSeed.Id(160) && g.AcademyId==academy);
        if(existing is not null && (!existing.IsActive || existing.Capacity<6 || existing.BranchId!=source.BranchId || existing.SportId!=source.SportId || !await db.StaffGroupAssignments.AnyAsync(a=>a.TrainingGroupId==existing.Id && a.AcademyMembershipId==coach && a.IsActive) || await db.SportEnrollments.CountAsync(e=>e.AcademyId==academy && e.IsActive && e.Player.IsActive && FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)==existing.Id)!=6)) return;
        if(await db.ResourceBookings.AnyAsync(b=>b.ResourceId==resource.Id && b.Date==day && b.State==ResourceBookingState.Reserved && b.StartTime<end && start<b.EndTime) || await db.TrainingSessions.AnyAsync(s=>s.AcademyId==academy && s.SessionDate==day && s.Status!=TrainingSessionStatus.Cancelled && s.StartTime<end && start<s.EndTime && db.StaffGroupAssignments.Any(a=>a.TrainingGroupId==s.TrainingGroupId && a.AcademyMembershipId==coach && a.IsActive))) return;
        if(!await db.TrainingGroups.AnyAsync(g=>g.Id==GroupPlacementDemoSeed.Id(160))) {
            var guardian=await db.GuardianProfiles.Where(g=>g.AcademyId==academy && g.User.PhoneNumber==DemoSeed.GuardianPhone && g.IsActive).Select(g=>g.Id).SingleAsync();
            db.TrainingGroups.Add(new(){Id=GroupPlacementDemoSeed.Id(160),AcademyId=academy,BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,TrainingLevelId=source.TrainingLevelId,Capacity=8,ArabicName="العد الفعلي — سيناريو6/5/7 تجريبي",CreatedAtUtc=now,UpdatedAtUtc=now});
            db.StaffGroupAssignments.Add(new(){Id=GroupPlacementDemoSeed.Id(162),AcademyId=academy,TrainingGroupId=GroupPlacementDemoSeed.Id(160),AcademyMembershipId=coach,CreatedAtUtc=now,UpdatedAtUtc=now});
            foreach(var n in new[]{180,190,200,210,220,230}) {
                db.Players.Add(new(){Id=GroupPlacementDemoSeed.Id(n),AcademyId=academy,PlayerCode="DEMO-OBS-"+n,ArabicName="لاعب عد فعلي اصطناعي "+n,DateOfBirth=new(2017,1,1),CreatedAtUtc=now,UpdatedAtUtc=now});
                db.SportEnrollments.Add(new(){Id=GroupPlacementDemoSeed.Id(n+1),AcademyId=academy,PlayerId=GroupPlacementDemoSeed.Id(n),BranchId=source.BranchId,SportId=source.SportId,TrainingGroupId=GroupPlacementDemoSeed.Id(160),GroupAssignedFrom=new(2026,9,28),TrainingLevelId=source.TrainingLevelId,LevelReviewedByUserId=actor,LevelReviewedAtUtc=now,CreatedAtUtc=now,UpdatedAtUtc=now});
                db.GuardianPlayerLinks.Add(new(){Id=GroupPlacementDemoSeed.Id(n+2),AcademyId=academy,PlayerId=GroupPlacementDemoSeed.Id(n),GuardianId=guardian,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
            }
            await db.SaveChangesAsync(); foreach(var n in new[]{180,190,200,210,220,230}) await collections.CreateCashAsync(academy,actor,GroupPlacementDemoSeed.Id(n+1),GroupPlacementDemoSeed.Id(5),"demo-observation-player-"+n,"Cash");
        }
        db.TrainingSessions.Add(new(){Id=sessionId,AcademyId=academy,TrainingGroupId=GroupPlacementDemoSeed.Id(160),BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,SessionDate=day,StartTime=start,EndTime=end,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.ResourceBookings.Add(new(){Id=bookingId,AcademyId=academy,ResourceId=resource.Id,BranchId=source.BranchId,TrainingGroupId=GroupPlacementDemoSeed.Id(160),TrainingSessionId=sessionId,CoachMembershipId=coach,Date=day,StartTime=start,EndTime=end,ReservedSeats=6,ApprovedCapacitySnapshot=resource.Capacity,Exclusive=false,ServiceKind="Training",State=ResourceBookingState.Reserved,ActorUserId=actor,RequestKey="demo-observation-booking-"+day,RequestHash="demo-observation",CreatedAtUtc=now,UpdatedAtUtc=now});
        // Duration-only subscriptions: saved synthetic presence has no session movement.
        foreach(var n in new[]{180,190,200,210,220}) db.PlayerAttendances.Add(new(){Id=DailyId(day,"attendance-"+n),AcademyId=academy,TrainingSessionId=sessionId,TrainingGroupId=GroupPlacementDemoSeed.Id(160),SportEnrollmentId=GroupPlacementDemoSeed.Id(n+1),Status=AttendanceStatus.Present,RecordedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();
        db.ResourceObservations.Add(new(){Id=DailyId(day,"observation"),AcademyId=academy,BranchId=source.BranchId,ResourceId=resource.Id,ResourceBookingId=bookingId,TrainingSessionId=sessionId,BookingDate=day,Sequence=1,Count=7,ExpectedSnapshot=6,RecordedPresentSnapshot=5,RecordedTotalSnapshot=5,ExpectedBasis="Roster",Source="DemoSeed",ActorUserId=actor,RequestKey="demo-observation-"+day,RequestHash="demo-observation",OutsideBookedHours=TimeOnly.FromDateTime(local.DateTime)<start || TimeOnly.FromDateTime(local.DateTime)>=end,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
