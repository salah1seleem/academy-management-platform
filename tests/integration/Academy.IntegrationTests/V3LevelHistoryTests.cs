using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Level_review_history_preserves_old_levels_replays_snapshots_and_private_notes_without_moving_or_promoting_automatically()
    {
        using var owner=await Staff();using var family=await Mobile(AcademyRole.Guardian);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var link=await db.GuardianPlayerLinks.FirstAsync(l=>l.IsActive&&l.Guardian.User.PhoneNumber==DemoSeed.GuardianPhone);var source=await db.SportEnrollments.AsNoTracking().FirstAsync(e=>e.PlayerId==link.PlayerId&&e.IsActive);var e=await FreshSkillChild(db,source,link.GuardianId);
        var a=await SkillDefinition(owner,e.SportId);var b=await SkillDefinition(owner,e.SportId);var url=$"/api/v1/people/enrollments/{e.Id}/placement-level";
        var financial=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var periods=await db.SubscriptionPeriods.CountAsync();
        var before=await owner.GetFromJsonAsync<JsonElement>(url);Assert.Empty(before.GetProperty("history").GetProperty("items").EnumerateArray());
        var first=new PlacementLevelWrite(a.Level,Guid.NewGuid(),before.GetProperty("version").GetUInt32(),"ملاحظة داخلية لا تظهر للأسرة");(await Send(owner,url,first,true)).EnsureSuccessStatusCode();(await Send(owner,url,first,true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,first with{LevelId=b.Level},true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,first with{LevelId=b.Level,RequestId=Guid.NewGuid()},true)).StatusCode);
        var current=await owner.GetFromJsonAsync<JsonElement>(url);Assert.Single(current.GetProperty("history").GetProperty("items").EnumerateArray());Assert.Equal(first.Note,current.GetProperty("history").GetProperty("items")[0].GetProperty("note").GetString());
        var second=new PlacementLevelWrite(b.Level,Guid.NewGuid(),current.GetProperty("version").GetUInt32());(await Send(owner,url,second,true)).EnsureSuccessStatusCode();
        var history=(await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("history").GetProperty("items");Assert.Equal(2,history.GetArrayLength());Assert.Equal(2,history[0].GetProperty("revision").GetInt32());var firstName=history[1].GetProperty("toLevelName").GetString();Assert.Equal(firstName,history[0].GetProperty("fromLevelName").GetString());
        var familyData=await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{e.PlayerId}/skills");var journey=familyData.GetProperty("items").EnumerateArray().Single(j=>j.GetProperty("enrollment").GetProperty("id").GetGuid()==e.Id);var publicRows=journey.GetProperty("levelHistory").GetProperty("items");Assert.Equal(2,publicRows.GetArrayLength());Assert.All(publicRows.EnumerateArray(),r=>Assert.Equal(JsonValueKind.Null,r.GetProperty("note").ValueKind));Assert.False(journey.GetProperty("canAssign").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,url,new PlacementLevelWrite(a.Level,Guid.NewGuid()),true)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await family.GetAsync(url)).StatusCode);
        var fromDb=await db.PlayerLevelReviews.AsNoTracking().SingleAsync(r=>r.SportEnrollmentId==e.Id&&r.Revision==1);var imm=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlayerLevelReviews\" WHERE \"Id\"={fromDb.Id}"));Assert.Equal("23514",imm.SqlState);
        // Renaming a live level never rewrites the saved label.
        var level=await db.TrainingLevels.SingleAsync(l=>l.Id==a.Level);level.ArabicName="اسم أحدث للمستوى "+Guid.NewGuid().ToString("N")[..6];await db.SaveChangesAsync();Assert.Equal(firstName,(await db.PlayerLevelReviews.AsNoTracking().SingleAsync(r=>r.Id==fromDb.Id)).ToLevelName);
        var after=await db.SportEnrollments.AsNoTracking().SingleAsync(x=>x.Id==e.Id);Assert.Equal(b.Level,after.TrainingLevelId);Assert.Equal(e.TrainingGroupId,after.TrainingGroupId);Assert.Equal(e.Status,after.Status);
        var third=new PlacementLevelWrite(b.Level,Guid.NewGuid(),after.Version);(await Send(owner,url,third,true)).EnsureSuccessStatusCode();var reviewed=(await owner.GetFromJsonAsync<JsonElement>(url));Assert.Equal("Reviewed",reviewed.GetProperty("history").GetProperty("items")[0].GetProperty("action").GetString());
        (await Send(owner,url,new PlacementLevelWrite(null,Guid.NewGuid(),reviewed.GetProperty("version").GetUInt32()),true)).EnsureSuccessStatusCode();(await Send(owner,url,new PlacementLevelWrite(null),true)).EnsureSuccessStatusCode();Assert.Equal(4,await db.PlayerLevelReviews.CountAsync(r=>r.SportEnrollmentId==e.Id));
        var originalReplay=await Send(owner,url,first,true);originalReplay.EnsureSuccessStatusCode();Assert.Equal(a.Level,(await originalReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trainingLevelId").GetGuid());Assert.Null((await db.SportEnrollments.AsNoTracking().SingleAsync(x=>x.Id==e.Id)).TrainingLevelId);Assert.Equal(4,await db.PlayerLevelReviews.CountAsync(r=>r.SportEnrollmentId==e.Id));
        // Deliberately forge only this isolated synthetic fixture. A false source
        // snapshot must roll back both its history and the enrollment update.
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var now=DateTimeOffset.UtcNow;
        var bad=new PlayerLevelReview{Id=Guid.NewGuid(),AcademyId=e.AcademyId,SportEnrollmentId=e.Id,SportId=e.SportId,Revision=5,ToLevelId=b.Level,ToLevelName="اسم مزيف غير مطابق للمصدر",ActorUserId=actor,ActorName="مالك اصطناعي",Action="Changed",RequestId=Guid.NewGuid(),RequestHash="isolated-rejected-fixture",CreatedAtUtc=now,UpdatedAtUtc=now};
        await using(var tx=await db.Database.BeginTransactionAsync()){
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SportEnrollments\" SET \"TrainingLevelId\"={b.Level},\"LevelReviewedByUserId\"={actor},\"LevelReviewedAtUtc\"={now},\"UpdatedAtUtc\"={now} WHERE \"Id\"={e.Id}");
            db.PlayerLevelReviews.Add(bad);await db.SaveChangesAsync();var rejected=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync());Assert.Equal("23514",rejected.SqlState);
        }
        db.Entry(bad).State=EntityState.Detached;Assert.Null((await db.SportEnrollments.AsNoTracking().SingleAsync(x=>x.Id==e.Id)).TrainingLevelId);Assert.Equal(4,await db.PlayerLevelReviews.CountAsync(r=>r.SportEnrollmentId==e.Id));
        Assert.Equal(periods,await db.SubscriptionPeriods.CountAsync());Assert.Equal(financial,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
}
