using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public sealed record GroupMediaConsentWrite(bool Allowed,int Revision,Guid RequestId);
public static class GroupMediaPrivacy
{
    private const string Notice="اختياري: السماح بظهور طفلك في صور وفيديوهات المجموعة للأسر المخولة وقت الواقعة فقط، مع استمرار انضمام طفلها. ليست موافقة تسويق أو إعلان. يمكنك السحب دون تأثير على الاشتراك. السحب يحجب النسخ من التطبيق، ولا يسترجع ملفًا سبق تنزيله.";
    public static void MapGroupMediaPrivacy(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1/guardian/children/{playerId:guid}/group-media-consent").RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("",Read);g.MapPost("",Save).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/assets/media-audience",Options).RequireAuthorization(AcademyPermissions.TenantAccess).WithMetadata(new Academy.Api.Configuration.AcademyFeatureMetadata("media"));
    }
    internal static Task Lock(FoundationDbContext db,Guid academy)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-media-privacy:{academy}"},0))");
    internal static IQueryable<Academy.Infrastructure.People.GuardianPlayerLink> Links(FoundationDbContext db)=>db.GuardianPlayerLinks.Where(l=>l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive);
    internal static IQueryable<GroupMediaConsent> Current(FoundationDbContext db)=>db.GroupMediaConsents.AsNoTracking().Where(c=>c.Allowed&&Links(db).Any(l=>l.Id==c.GuardianPlayerLinkId)&&!db.GroupMediaConsents.Any(n=>n.GuardianPlayerLinkId==c.GuardianPlayerLinkId&&n.Revision>c.Revision));
    internal static IQueryable<PrivateAsset> Shareable(FoundationDbContext db)
    {
        var current=Current(db);var links=Links(db);
        return db.PrivateAssets.Where(a=>a.Purpose=="Media"&&a.MediaPolicyVersion==1&&a.IsActive&&a.MediaEventOn!=null&&
            !db.PrivateMediaConsentBases.Any(b=>b.AssetId==a.Id&&!current.Any(c=>c.Id==b.ConsentId))&&
            (a.NoRecognizableChildren||db.PrivateAssetAudiences.Any(s=>s.AssetId==a.Id&&s.IsSubject))&&
            !db.PrivateAssetAudiences.Any(s=>s.AssetId==a.Id&&s.IsSubject&&(!links.Any(l=>l.PlayerId==s.PlayerId)||links.Any(l=>l.PlayerId==s.PlayerId&&!db.PrivateMediaConsentBases.Any(b=>b.AssetId==a.Id&&b.PlayerId==s.PlayerId&&current.Any(c=>c.Id==b.ConsentId&&c.GuardianPlayerLinkId==l.Id))))));
    }
    private static async Task<DateTimeOffset> EventEnd(FoundationDbContext db,Guid academy,DateOnly day)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync());
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue),zone));
    }
    internal static IQueryable<Academy.Infrastructure.People.SportEnrollment> Roster(FoundationDbContext db,Guid group,DateOnly day,DateTimeOffset end)=>db.SportEnrollments.AsNoTracking().Where(e=>e.IsActive&&e.Player.IsActive&&e.Player.DateOfBirth<=day&&e.Branch.IsActive&&e.Sport.IsActive&&(e.GroupAssignedFrom!=null||e.CreatedAtUtc>DateTimeOffset.MinValue&&e.CreatedAtUtc<end)&&FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)==group);
    internal static IQueryable<PrivateAssetAudience> CurrentAudience(FoundationDbContext db,Guid group,DateOnly today)=>db.PrivateAssetAudiences.Where(a=>a.IsActive&&a.SportEnrollmentId!=null&&db.TrainingGroups.Any(g=>g.Id==group&&g.IsActive)&&db.SportEnrollments.Any(e=>e.Id==a.SportEnrollmentId&&e.PlayerId==a.PlayerId&&e.IsActive&&e.Player.IsActive&&e.Branch.IsActive&&e.Sport.IsActive&&FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,today)==group));
    internal static Task<bool> Assigned(FoundationDbContext db,TenantMembership t,Guid group)=>db.StaffGroupAssignments.AnyAsync(a=>a.TrainingGroupId==group&&a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.AcademyMembership.IsActive&&a.AcademyMembership.User.IsActive&&a.TrainingGroup.IsActive);
    internal static async Task<bool> Staff(TenantMembership t,FoundationDbContext db,HttpContext h,Guid group,bool write)
    {
        if(t.Role==AcademyRole.Coach)return await Assigned(db,t,group);
        return t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin && (await h.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(h.User,h,write?AcademyPermissions.PlayerMediaManage:AcademyPermissions.PlayerMediaRead)).Succeeded;
    }
    private static async Task<IResult> Read(Guid playerId,CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();var link=await Links(db).Where(l=>l.PlayerId==playerId&&l.Guardian.UserId==db.RequestUserId).Select(l=>new{l.Id,player=l.Player.ArabicName}).SingleOrDefaultAsync();if(link==null)return Results.NotFound();
        var c=await db.GroupMediaConsents.AsNoTracking().Where(c=>c.GuardianPlayerLinkId==link.Id).OrderByDescending(c=>c.Revision).FirstOrDefaultAsync();return Results.Ok(new{link.player,allowed=c?.Allowed??false,revision=c?.Revision??0,changedAtUtc=c?.CreatedAtUtc,notice=Notice});
    }
    private static async Task<IResult> Save(Guid playerId,GroupMediaConsentWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();if(b.RequestId==Guid.Empty||b.Revision<0)return Results.BadRequest(new{message="أعد فتح اختيار مشاركة الطفل."});
        await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,t.AcademyId);
        var link=await Links(db).SingleOrDefaultAsync(l=>l.PlayerId==playerId&&l.Guardian.UserId==db.RequestUserId);if(link==null)return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"GuardianPlayerLinks\" WHERE \"Id\"={link.Id} FOR SHARE");
        if(!await Links(db).AnyAsync(l=>l.Id==link.Id&&l.Guardian.UserId==db.RequestUserId))return Results.NotFound();
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{playerId,b}))));
        var old=await db.GroupMediaConsents.SingleOrDefaultAsync(c=>c.ActorUserId==db.RequestUserId&&c.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Conflict();
        var current=await db.GroupMediaConsents.Where(c=>c.GuardianPlayerLinkId==link.Id).OrderByDescending(c=>c.Revision).FirstOrDefaultAsync();if((current?.Revision??0)!=b.Revision)return Conflict();
        var now=clock.GetUtcNow();var c=new GroupMediaConsent{Id=Guid.NewGuid(),AcademyId=t.AcademyId,GuardianPlayerLinkId=link.Id,PlayerId=playerId,ActorUserId=db.RequestUserId,Revision=b.Revision+1,Allowed=b.Allowed,RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.GroupMediaConsents.Add(c);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{c.Id});
    }
    private static IResult Conflict()=>Results.Conflict(new{message="تغير اختيار المشاركة؛ أعد فتح الصفحة قبل الحفظ."});
    private static async Task<IResult> Options(Guid groupId,DateOnly? eventOn,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;if(!await Staff(t,db,h,groupId,false)||!await db.TrainingGroups.AnyAsync(g=>g.Id==groupId&&g.IsActive))return Results.NotFound();var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var day=eventOn??today;if(day>today)return Results.BadRequest(new{message="تاريخ الواقعة لا يكون في المستقبل."});
        var end=await EventEnd(db,t.AcademyId,day);var roster=await Roster(db,groupId,day,end).OrderBy(e=>e.Player.ArabicName).Take(201).Select(e=>new{e.PlayerId,name=e.Player.ArabicName}).Distinct().ToArrayAsync();if(roster.Length>200)return Results.UnprocessableEntity(new{message="قائمة الوسائط تتجاوز200 لاعب؛ قسم المجموعة للمراجعة الآمنة."});
        var current=Current(db);var ids=roster.Select(e=>e.PlayerId).ToArray();var safe=await db.Players.Where(p=>ids.Contains(p.Id)&&Links(db).Any(l=>l.PlayerId==p.Id)&&!Links(db).Any(l=>l.PlayerId==p.Id&&!current.Any(c=>c.GuardianPlayerLinkId==l.Id))).Select(p=>p.Id).ToArrayAsync();
        return Results.Ok(new{eventOn=day,asOfDate=today,players=roster.Select(e=>new{id=e.PlayerId,e.name,consented=safe.Contains(e.PlayerId)}),notice="حدد كل طفل يمكن التعرف عليه. غير الموافق يمنع النشر الجماعي؛ قص أو اطمس خارجيًا ثم راجع القائمة. القائمة جمهور الواقعة، والتنزيل مشروط باستمرار العضوية والموافقة. هذا إقرار المراجع وليس كشف وجوه آليًا."});
    }
    // Called inside the upload transaction, before writing any bytes.
    internal static async Task<string?> Prepare(PrivateAsset a,IFormCollection form,FoundationDbContext db,TenantMembership t,HttpContext h,TimeProvider clock)
    {
        if(form["privacyConfirmed"]!="true")return "أكد مراجعة خصوصية الصورة أو الفيديو قبل النشر.";
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        if(!DateOnly.TryParse(form["eventOn"],out var day)||day>today)return "حدد تاريخ الواقعة حتى اليوم الفعلي.";
        a.MediaPolicyVersion=1;a.MediaEventOn=day;
        if(a.PlayerId.HasValue)return null; // The reviewer attests only this child is identifiable; never a group audience.
        var group=a.TrainingGroupId!.Value;
        if(!await Staff(t,db,h,group,true))return "لا تملك صلاحية النشر لهذه المجموعة الآن.";
        var end=await EventEnd(db,t.AcademyId,day);var roster=await Roster(db,group,day,end).OrderBy(e=>e.Id).Take(201).Select(e=>new{e.Id,e.PlayerId}).ToArrayAsync();
        if(roster.Length==0||roster.Length>200)return "لا يوجد جمهور مؤرخ صالح، أو تجاوزت القائمة200 تسجيل؛ راجع المجموعة.";
        var members=roster.DistinctBy(e=>e.PlayerId).ToArray();
        var parts=form["subjectPlayerIds"].ToString().Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var subjects=new HashSet<Guid>();foreach(var part in parts){if(!Guid.TryParse(part,out var id)||!members.Any(e=>e.PlayerId==id))return "أحد الأطفال الظاهرين ليس ضمن جمهور الواقعة المسموح.";subjects.Add(id);}
        a.NoRecognizableChildren=form["noRecognizableChildren"]=="true";
        if(a.NoRecognizableChildren?subjects.Count!=0:subjects.Count==0)return "حدد الأطفال الظاهرين، أو أكد أن اللقطة لا تحتوي طفلًا يمكن التعرف عليه؛ لا تجمع الخيارين.";
        var consents=await Current(db).Where(c=>subjects.Contains(c.PlayerId)).ToArrayAsync();var links=await Links(db).Where(l=>subjects.Contains(l.PlayerId)).Select(l=>new{l.Id,l.PlayerId}).ToArrayAsync();
        if(subjects.Any(id=>!links.Any(l=>l.PlayerId==id))||links.Any(l=>!consents.Any(c=>c.GuardianPlayerLinkId==l.Id)))return "النشر الجماعي غير متاح: موافقة مشاركة أحد الأطفال الظاهرين غير مستوفاة. احصل على اختيار الأسرة داخل حسابها أو قص/اطمس خارجيًا قبل الرفع.";
        var now=clock.GetUtcNow();
        db.PrivateAssetAudiences.AddRange(members.Select(e=>new PrivateAssetAudience{Id=Guid.NewGuid(),AcademyId=t.AcademyId,AssetId=a.Id,PlayerId=e.PlayerId,SportEnrollmentId=e.Id,IsSubject=subjects.Contains(e.PlayerId),CreatedAtUtc=now,UpdatedAtUtc=now}));
        db.PrivateMediaConsentBases.AddRange(consents.Select(c=>new PrivateMediaConsentBasis{Id=Guid.NewGuid(),AcademyId=t.AcademyId,AssetId=a.Id,PlayerId=c.PlayerId,ConsentId=c.Id,CreatedAtUtc=now,UpdatedAtUtc=now}));
        return null;
    }
}
