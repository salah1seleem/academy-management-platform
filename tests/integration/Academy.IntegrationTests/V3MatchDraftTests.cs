using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    private static string DraftRoot(Guid id)=>$"/api/v1/manage/matches/{id}";
    private static Task<JsonElement> DraftPreview(HttpClient c,Guid id)=>c.GetFromJsonAsync<JsonElement>(DraftRoot(id)+"/copy-preview");
    private static Task<HttpResponseMessage> DraftCopy(HttpClient c,Guid id,MatchCopyWrite body,string? key=null)=>Send(c,DraftRoot(id)+"/copy",body,key:key??Guid.NewGuid().ToString());
    [Fact]
    public async Task V3_match_copy_add_permission_and_branch_sport_scope_are_enforced_independently_of_read()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var b=new MatchWrite(f.Tournament,f.Team,"صلاحيات النسخ",new(2026,10,21),new(17,0),"ملعب",null,null,null,[],false,true);var created=await Send(owner,"/api/v1/manage/matches/",b);created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var tag=Guid.NewGuid().ToString("N");var email=$"match.reader.{tag}@example.test";var phone="010"+string.Concat(tag.Where(char.IsDigit).Take(8)).PadRight(8,'7');var added=await Send(owner,"/api/v1/manage/staff",new StaffWrite("إداري مباريات اصطناعي",email,phone,"Demo-Only-123!",AcademyRole.AcademyAdmin,"مراجعة تشغيل"));added.EnsureSuccessStatusCode();var member=(await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var team=await db.MatchTeams.AsNoTracking().SingleAsync(x=>x.Id==f.Team);var access=new StaffAccessWrite(false,[team.BranchId],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="matches",false,false,false)).ToArray(),AllSports:true,Sports:[]);var path=$"/api/v1/manage/staff/{member}/access";(await Send(owner,path,access,true)).EnsureSuccessStatusCode();using var admin=await Staff(email);Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/matches/options")).GetProperty("canCopy").GetBoolean());var q=await DraftPreview(admin,id);var copy=new MatchCopyWrite(new(2026,10,22),new(18,0),q.GetProperty("quoteHash").GetString()!);Assert.Equal(HttpStatusCode.Forbidden,(await DraftCopy(admin,id,copy)).StatusCode);
        access=access with{Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="matches",m=="matches",false,false)).ToArray()};(await Send(owner,path,access,true)).EnsureSuccessStatusCode();Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/matches/options")).GetProperty("canCopy").GetBoolean());(await DraftCopy(admin,id,copy)).EnsureSuccessStatusCode();
        var otherBranch=await db.Branches.Where(x=>x.AcademyId==team.AcademyId&&x.Id!=team.BranchId&&x.IsActive).Select(x=>x.Id).FirstAsync();(await Send(owner,path,access with{Branches=[otherBranch]},true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(DraftRoot(id)+"/copy-preview")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await DraftCopy(admin,id,copy)).StatusCode);
        var otherSport=await db.Sports.Where(x=>x.AcademyId==team.AcademyId&&x.Id!=team.SportId&&x.IsActive).Select(x=>x.Id).FirstAsync();(await Send(owner,path,access with{AllSports=false,Sports=[otherSport]},true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(DraftRoot(id)+"/copy-preview")).StatusCode);
    }
    [Fact]
    public async Task V3_match_copy_is_once_only_independent_draft_without_results_audience_notices_or_financial_records()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);using var coach=await Mobile(AcademyRole.Coach);var f=await Fixtures(owner);
        var clock=factory.Services.GetRequiredService<ISubscriptionClock>();var original=new MatchWrite(f.Tournament,f.Team,"منافس سابق اصطناعي",clock.Today.AddDays(-1),new(17,0),"ملعب عرض آمن","تجمع الفريق",3,1,[],true,true);
        var saved=await Send(owner,"/api/v1/manage/matches/",original);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var before=await db.AcademyMatches.AsNoTracking().SingleAsync(x=>x.Id==id);var notices=await db.AcademyNotices.CountAsync();var payments=await db.TournamentPayments.CountAsync();var collections=await db.Collections.CountAsync();var participants=await db.MatchParticipants.CountAsync(x=>x.MatchId==id);
        var preview=await DraftPreview(owner,id);Assert.Single(preview.GetProperty("roster").EnumerateArray());var body=new MatchCopyWrite(clock.Today.AddDays(2),new(18,30),preview.GetProperty("quoteHash").GetString()!);var key=Guid.NewGuid().ToString();var copies=await Task.WhenAll(DraftCopy(owner,id,body,key),DraftCopy(owner,id,body,key));Assert.Single(copies,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(copies,r=>r.StatusCode==HttpStatusCode.OK);
        var copy=(await copies[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.NotEqual(id,copy);var row=await db.AcademyMatches.AsNoTracking().SingleAsync(x=>x.Id==copy);Assert.Equal(id,row.CopiedFromMatchId);Assert.Equal(body.MatchDate,row.MatchDate);Assert.Equal(body.StartTime,row.StartTime);Assert.Null(row.OurScore);Assert.Null(row.OpponentScore);Assert.Null(row.PublishedAtUtc);Assert.Null(row.PublishedFingerprint);Assert.True(row.IsActive);Assert.False(await db.MatchParticipants.AnyAsync(x=>x.MatchId==copy));
        Assert.Equal(notices,await db.AcademyNotices.CountAsync());Assert.Equal(payments,await db.TournamentPayments.CountAsync());Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(participants,await db.MatchParticipants.CountAsync(x=>x.MatchId==id));var source=await db.AcademyMatches.AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal(before.PublishedFingerprint,source.PublishedFingerprint);Assert.Equal(3,source.OurScore);Assert.Equal(before.UpdatedAtUtc,source.UpdatedAtUtc);
        foreach(var c in new[]{guardian,coach})Assert.DoesNotContain(copy.ToString(),await c.GetStringAsync("/api/v1/my-matches"));
        Assert.Equal(HttpStatusCode.Conflict,(await DraftCopy(owner,id,body with{StartTime=new(19,0)},key)).StatusCode);
        var publish=original with{MatchDate=body.MatchDate,StartTime=body.StartTime,OurScore=null,OpponentScore=null};Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,DraftRoot(copy),publish,true)).StatusCode);
        var roster=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/teams/{f.Team}/roster");publish=publish with{RosterQuoteHash=roster.GetProperty("quoteHash").GetString()};(await Send(owner,DraftRoot(copy),publish,true)).EnsureSuccessStatusCode();Assert.Contains(copy.ToString(),await guardian.GetStringAsync("/api/v1/my-matches"));var publishedNotices=await db.AcademyNotices.CountAsync();Assert.True(publishedNotices>notices);(await Send(owner,DraftRoot(copy),publish,true)).EnsureSuccessStatusCode();Assert.Equal(publishedNotices,await db.AcademyNotices.CountAsync());Assert.Single(await db.MatchParticipants.Where(x=>x.MatchId==copy).ToArrayAsync());
    }
    [Fact]
    public async Task V3_match_copy_requires_current_preview_explicit_date_time_csrf_and_authorized_tenant_role()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var sourceBody=new MatchWrite(f.Tournament,f.Team,"نسخ آمن",new(2026,10,20),new(17,0),"ملعب",null,null,null,[],false,true);var created=await Send(owner,"/api/v1/manage/matches/",sourceBody);created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var quote=await DraftPreview(owner,id);var body=new MatchCopyWrite(new(2026,10,22),new(19,0),quote.GetProperty("quoteHash").GetString()!);
        (await Send(owner,DraftRoot(id),sourceBody with{Venue="ملعب آخر"},true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await DraftCopy(owner,id,body)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(DraftRoot(id)+"/copy",body)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await Send(owner,DraftRoot(id)+"/copy",new{body.QuoteHash},key:Guid.NewGuid().ToString())).StatusCode);
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(DraftRoot(id)+"/copy-preview")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await DraftCopy(other,id,body)).StatusCode);
        foreach(var role in new[]{AcademyRole.Coach,AcademyRole.Guardian}){using var mobile=await Mobile(role);Assert.Equal(HttpStatusCode.Forbidden,(await mobile.GetAsync(DraftRoot(id)+"/copy-preview")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await DraftCopy(mobile,id,body)).StatusCode);}
        var fresh=await DraftPreview(owner,id);(await DraftCopy(owner,id,body with{QuoteHash=fresh.GetProperty("quoteHash").GetString()!})).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task V3_match_copy_roster_change_invalidates_preview_and_first_publish_but_preserves_published_source_roster()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var body=new MatchWrite(f.Tournament,f.Team,"منافس اختبار قائمة",new(2026,10,20),new(17,0),"ملعب",null,null,null,[],true,true);var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var quote=await DraftPreview(owner,id);var copyBody=new MatchCopyWrite(new(2026,10,25),new(19,0),quote.GetProperty("quoteHash").GetString()!);
        var copy=await DraftCopy(owner,id,copyBody);copy.EnsureSuccessStatusCode();var copyId=(await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var oldRoster=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/teams/{f.Team}/roster");
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var current=await db.MatchTeamPlayers.SingleAsync(x=>x.TeamId==f.Team);current.IsActive=false;await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await DraftCopy(owner,id,copyBody)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,DraftRoot(copyId),body with{MatchDate=copyBody.MatchDate,StartTime=copyBody.StartTime,RosterQuoteHash=oldRoster.GetProperty("quoteHash").GetString()},true)).StatusCode);
        Assert.Single(await db.MatchParticipants.Where(x=>x.MatchId==id).ToArrayAsync());Assert.False(await db.MatchParticipants.AnyAsync(x=>x.MatchId==copyId));var empty=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/teams/{f.Team}/roster");Assert.Empty(empty.GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,DraftRoot(copyId),body with{MatchDate=copyBody.MatchDate,StartTime=copyBody.StartTime,RosterQuoteHash=empty.GetProperty("quoteHash").GetString()},true)).StatusCode);
    }
}
