using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class GuardianContactEndpoints
{
    public static void MapGuardianContacts(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/people").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/players/{id:guid}/guardian-contacts", Player);
        api.MapGet("/guardians/{id:guid}/contact", Guardian);
    }
    private static Task<AuthorizationResult> Staff(ClaimsPrincipal actor,TenantMembership t,IAuthorizationService auth) =>
        auth.AuthorizeAsync(actor,null,t.Role==AcademyRole.Coach?AcademyPermissions.CoachGroupsRead:AcademyPermissions.PeopleManage);
    private static async Task<IQueryable<SportEnrollment>> Visible(FoundationDbContext db,TenantMembership t,TimeProvider time)
    {
        var query=db.SportEnrollments.Where(e=>e.AcademyId==t.AcademyId&&e.IsActive&&e.Player.IsActive);
        if(t.Role!=AcademyRole.Coach)return query;
        var day=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        return query.Where(e=>db.StaffGroupAssignments.Any(a=>a.AcademyId==t.AcademyId&&a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.AcademyMembership.IsActive&&a.AcademyMembership.User.IsActive&&a.TrainingGroupId==FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)));
    }
    public static string? ChatUrl(string? phone,string academy,string? player=null)
    {
        var normalized=EgyptPhoneNormalizer.Normalize(phone);if(normalized is null)return null;
        var draft="مرحبًا، نتواصل معكم من أكاديمية "+academy+(string.IsNullOrWhiteSpace(player)?".":" بخصوص "+player+".");
        return "https://wa.me/"+normalized[1..]+"?text="+Uri.EscapeDataString(draft);
    }
    private static async Task<IResult> Player(Guid id,CurrentTenant tenant,ClaimsPrincipal actor,IAuthorizationService auth,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role==AcademyRole.Guardian||!(await Staff(actor,t,auth)).Succeeded)return Results.Forbid();
        var visible=await Visible(db,t,time);
        var player=await db.Players.Where(p=>p.AcademyId==t.AcademyId&&p.Id==id&&p.IsActive&&(t.Role!=AcademyRole.Coach||visible.Any(e=>e.PlayerId==p.Id))).Select(p=>p.ArabicName).SingleOrDefaultAsync();
        if(player is null)return Results.NotFound();
        response.Headers.CacheControl="private, no-store";
        var contacts=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==t.AcademyId&&l.PlayerId==id&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive&&db.AcademyMemberships.Any(m=>m.AcademyId==t.AcademyId&&m.UserId==l.Guardian.UserId&&m.Role==AcademyRole.Guardian&&m.IsActive))
            .OrderBy(l=>l.Guardian.DisplayName).Select(l=>new {id=l.GuardianId,name=l.Guardian.DisplayName,relationship=l.RelationshipType,phone=l.Guardian.ContactPhone}).ToListAsync();
        return Results.Ok(new {player,contacts=contacts.Select(c=>new {c.id,c.name,relationship=c.relationship=="ولي أمر — اعتماد استيراد"?"ولي الأمر":c.relationship,phone=EgyptPhoneNormalizer.Normalize(c.phone),url=ChatUrl(c.phone,t.AcademyName,player)}),message=contacts.Count==0?"لا يوجد ولي أمر مرتبط متاح للتواصل. راجع علاقات ولي الأمر في ملف اللاعب.":null});
    }
    private static async Task<IResult> Guardian(Guid id,CurrentTenant tenant,ClaimsPrincipal actor,IAuthorizationService auth,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role==AcademyRole.Guardian||!(await Staff(actor,t,auth)).Succeeded)return Results.Forbid();
        var visible=await Visible(db,t,time);
        var guardian=await db.GuardianProfiles.Where(g=>g.AcademyId==t.AcademyId&&g.Id==id&&g.IsActive&&g.User.IsActive&&db.AcademyMemberships.Any(m=>m.AcademyId==t.AcademyId&&m.UserId==g.UserId&&m.Role==AcademyRole.Guardian&&m.IsActive)&&db.GuardianPlayerLinks.Any(l=>l.AcademyId==t.AcademyId&&l.GuardianId==g.Id&&l.IsActive&&visible.Any(e=>e.PlayerId==l.PlayerId)))
            .Select(g=>new {g.Id,g.DisplayName,g.ContactPhone}).SingleOrDefaultAsync();
        if(guardian is null)return Results.NotFound();response.Headers.CacheControl="private, no-store";
        return Results.Ok(new {contacts=new[]{new {id=guardian.Id,name=guardian.DisplayName,relationship="ولي الأمر",phone=EgyptPhoneNormalizer.Normalize(guardian.ContactPhone),url=ChatUrl(guardian.ContactPhone,t.AcademyName)}}});
    }
}
