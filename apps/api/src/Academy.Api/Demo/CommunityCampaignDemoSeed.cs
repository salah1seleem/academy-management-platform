using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class CommunityCampaignDemoSeed
{
    public static async Task Seed(FoundationDbContext db,IWebHostEnvironment env,DemoOptions options,TimeProvider clock,IConfiguration config)
    {
        var a=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(x=>x.Id==a&&x.Slug==FootballDemoSeed.Slug&&x.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("المحتوى الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"config:{a}"},0))");
        if(await db.CommunityCampaigns.AnyAsync(c=>c.Id==FootballDemoSeed.Id("community-demo/approved")))return; // Never re-enable an operator's later opt-out or overwrite revisions.
        var owner=await db.AcademyMemberships.SingleAsync(m=>m.Id==FootballDemoSeed.Id("member/owner")&&m.AcademyId==a&&m.Role==AcademyRole.AcademyOwner&&m.IsActive&&m.User.IsActive&&m.AllBranches&&m.AllSports);var now=clock.GetUtcNow();
        var entitlement=await db.AcademyFeatureEntitlements.SingleAsync(e=>e.AcademyId==a&&e.Feature=="community_ads");entitlement.Enabled=true;entitlement.PlanCode="Premium";await db.SaveChangesAsync();
        var current=await db.AcademyConfigurationVersions.SingleAsync(v=>v.AcademyId==a&&v.State==ConfigurationState.Active);var settings=AcademyRuntimeSettings.Read(current.Payload);var updated=settings with{Features=new(settings.Features),TenantMarketingOptIn=true};updated.Features["community_ads"]=true;
        var service=new AcademyConfigurationService(db,clock);var draft=await service.CreateDraft(a,owner.UserId,updated);var errors=await service.Validate(draft);if(errors.Length!=0)throw new InvalidOperationException("إعداد بيانات الإعلان الاصطناعية غير صالح.");
        await service.Transition(draft,ConfigurationState.Validated,owner.UserId);await service.Transition(draft,ConfigurationState.Staged,owner.UserId);await service.Transition(current,ConfigurationState.Superseded,owner.UserId);draft.EffectiveAtUtc=now;await service.Transition(draft,ConfigurationState.Active,owner.UserId);
        // Reuse the academy's existing non-child brand asset; never download a tracker or reuse children's photos.
        var image=FootballDemoSeed.Id("community-demo/design");var source=Path.Combine(env.ContentRootPath,"Assets","professional-academy.png");var bytes=await File.ReadAllBytesAsync(source);var root=Path.GetFullPath(config["Media:PrivateRoot"]??Path.Combine(env.ContentRootPath,"local-uploads"));Directory.CreateDirectory(root);var storage="synthetic-community-"+image.ToString("N")+".png";var path=Path.Combine(root,storage);
        if(File.Exists(path)){if(!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(bytes)))throw new InvalidOperationException("ملف محجوز غير مطابق؛ لم يُكتب فوقه.");}else{await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);await output.WriteAsync(bytes);}
        db.PrivateAssets.Add(new(){Id=image,AcademyId=a,UploadedByUserId=owner.UserId,Purpose="Campaign",StorageName=storage,ContentType="image/png",Length=bytes.Length,Caption="تصميم اصطناعي من هوية أكاديمية العرض، بلا أطفال",CreatedAtUtc=now,UpdatedAtUtc=now});
        string[] names=["approved","pending","expired","paused","rejected","changed"];
        for(var i=0;i<names.Length;i++)
        {
            var key=names[i];var campaign=FootballDemoSeed.Id("community-demo/"+key);var changed=key=="changed";
            db.CommunityCampaigns.Add(new(){Id=campaign,AcademyId=a,InternalName=$"محتوى برعاية — مثال {key switch{"approved"=>"معتمد لمدينتي","pending"=>"بانتظار المراجعة","expired"=>"منتهي","paused"=>"موقوف","rejected"=>"مرفوض لمدينة نصر",_=>"تصميم معدل يحتاج اعتمادًا"}}",Advertiser="مركز البرمجة التجريبي — جهة خيالية",CurrentRevision=changed?2:1,CreatedAtUtc=now,UpdatedAtUtc=now});
            for(var revision=1;revision<=(changed?2:1);revision++)
            {
                var id=FootballDemoSeed.Id($"community-demo/{key}/revision/{revision}");var state=key=="pending"?"PendingApproval":key=="paused"?"Paused":key=="rejected"?"Rejected":changed&&revision==2?"Draft":"Approved";var decision=state=="PendingApproval"||state=="Draft"?"Pending":state=="Rejected"?"Rejected":"Approved";var branch=FootballDemoSeed.Id(key=="rejected"?"branch/0":"branch/1");var request=FootballDemoSeed.Id($"community-demo/{key}/request/{revision}");var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-community/{key}/{revision}")));
                db.CommunityCampaignRevisions.Add(new(){Id=id,AcademyId=a,CampaignId=campaign,Revision=revision,Advertiser="مركز البرمجة التجريبي — جهة خيالية",Title=changed&&revision==2?"تصميم جديد لم يُعتمد بعد":"ورشة برمجة في مدينتي — عرض تجريبي",Body="محتوى عربي اصطناعي للتجربة فقط. ورشة تعريفية للأسرة داخل الأكاديمية؛ لا دفع أو حجز أو تواصل حقيقي، ولا مشاركة أسماء أو هواتف الأطفال.",ButtonText="تفاصيل الورشة",ImageAssetId=image,ImageAlt="شعار أكاديمية العرض ضمن تصميم تجريبي بلا صور أطفال",StartsAtUtc=key=="expired"?now.AddDays(-14):now.AddDays(-1),EndsAtUtc=key=="expired"?now.AddDays(-7):now.AddDays(7),Priority=changed?9:5,State=state,CreatedByMembershipId=owner.Id,RequestId=request,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});
                db.CommunityCampaignTargets.Add(new(){Id=FootballDemoSeed.Id($"community-demo/{key}/target/{revision}"),AcademyId=a,RevisionId=id,BranchId=branch,Decision=decision,CreatedAtUtc=now,UpdatedAtUtc=now});
                db.CommunityCampaignReviews.Add(new(){Id=FootballDemoSeed.Id($"community-demo/{key}/review/{revision}"),AcademyId=a,RevisionId=id,BranchId=decision=="Approved"||decision=="Rejected"?branch:null,ActorMembershipId=owner.Id,Decision=state=="Draft"?"Draft":decision=="Approved"?"Approve":decision=="Rejected"?"Reject":"Submit",Note="حالة اصطناعية معلّمة للعرض فقط، ليست موافقة تجارية أو تحصيلًا حقيقيًا.",RequestId=request,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});
            }
        }
        // Synthetic adults in this reserved dataset only. Production/user preferences default off and are never seeded.
        var users=Enumerable.Range(1,29).Select(i=>FootballDemoSeed.Id($"user/guardian{i}")).ToArray();foreach(var user in users)if(!await db.GuardianSponsorPreferences.AnyAsync(p=>p.AcademyId==a&&p.UserId==user))db.GuardianSponsorPreferences.Add(new(){Id=FootballDemoSeed.Id("community-demo/preference/"+user),AcademyId=a,UserId=user,AdultDeclaredAtUtc=now,InAppOptIn=true,CreatedAtUtc=now,UpdatedAtUtc=now});
        // No measured impression/click is seeded: only actual UI exposure or a deliberate test event can create one.
        var match=FootballDemoSeed.Id("community-demo/urgent-match");var team=FootballDemoSeed.Id("community-demo/medinaty-team");var date=(await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(a,db,clock)).AddDays(2);
        db.MatchTeams.Add(new(){Id=team,AcademyId=a,Name="منتخب مدينتي — عرض المحتوى برعاية",SportId=FootballDemoSeed.Id("sport"),BranchId=FootballDemoSeed.Id("branch/1"),AgeCategoryId=FootballDemoSeed.Id("age/2"),CoachMembershipId=FootballDemoSeed.Id("member/coach3"),CreatedAtUtc=now,UpdatedAtUtc=now});
        db.MatchTeamPlayers.Add(new(){Id=FootballDemoSeed.Id("community-demo/team-player"),AcademyId=a,TeamId=team,EnrollmentId=FootballDemoSeed.Id("enrollment/12"),CreatedAtUtc=now,UpdatedAtUtc=now});
        db.AcademyMatches.Add(new(){Id=match,AcademyId=a,TeamId=team,TournamentId=FootballDemoSeed.Id("matches/tournament"),Opponent="فريق عرض افتراضي",MatchDate=date,StartTime=new TimeOnly(18,0),Venue="ملعب مدينتي التجريبي",Notes="موعد اصطناعي لاختبار أولوية إشعار الخدمة، بلا رسوم أو حضور ضمني.",PublishedAtUtc=now,PublishedFingerprint="community-demo-urgent-fixture",CreatedAtUtc=now,UpdatedAtUtc=now});
        db.MatchCoachAssignments.Add(new(){Id=FootballDemoSeed.Id("community-demo/match-coach"),AcademyId=a,MatchId=match,CoachMembershipId=FootballDemoSeed.Id("member/coach3"),AssignedByMembershipId=owner.Id,Sequence=1,Source="DemoFixture",CreatedAtUtc=now,UpdatedAtUtc=now});
        db.MatchParticipants.Add(new(){Id=FootballDemoSeed.Id("community-demo/match-participant"),AcademyId=a,MatchId=match,PlayerId=FootballDemoSeed.Id("player/12"),CreatedAtUtc=now,UpdatedAtUtc=now});
        db.AcademyNotices.Add(new(){Id=FootballDemoSeed.Id("community-demo/urgent-notice"),AcademyId=a,UserId=FootballDemoSeed.Id("user/guardian12"),MatchId=match,UrgentUntilUtc=now.AddDays(7),Message=$"تنبيه موعد اصطناعي: مباراة ابنك في مدينتي يوم {date:yyyy-MM-dd} الساعة 18:00. بيانات عرض فقط؛ راجع المواعيد قبل المحتوى برعاية.",CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
