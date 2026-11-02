using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Importing;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class PlatformSetupImportTests
{
    [Fact]
    public async Task Platform_private_import_uses_its_purpose_bound_calendar_authority_and_metadata_replay_preserves_capacity()
    {
        using var platform=await Platform();var phone="01096"+Random.Shared.Next(100000,999999);var academy=await Create(platform,phone);var path=$"/api/v1/platform/academies/{academy}";
        var licenses=await platform.GetFromJsonAsync<JsonElement>(path+"/entitlements");
        var features=licenses.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>x.GetProperty("feature").GetString() is "training" or "facilities");
        (await Send(platform,path+"/entitlements",new{planCode="Trial",features},true)).EnsureSuccessStatusCode();
        (await Send(platform,path+"/configuration",new{features},true)).EnsureSuccessStatusCode();
        var branch=Guid.NewGuid();var sport=Guid.NewGuid();var age=Guid.NewGuid();var level=Guid.NewGuid();var group=Guid.NewGuid();var coach=Guid.NewGuid();var user=Guid.NewGuid();var facility=Guid.NewGuid();var space=Guid.NewGuid();var resource=Guid.NewGuid();var session=Guid.NewGuid();
        var day=new DateOnly(2026,10,26);var now=DateTimeOffset.UtcNow;var start=new TimeOnly(17,0);var end=new TimeOnly(18,0);var coachPhone="01596"+Random.Shared.Next(100000,999999);
        await using(var scope=factory.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var owner=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.UserId).SingleAsync();
            db.AddRange(new Branch{Id=branch,AcademyId=academy,ArabicName="فرع اختبار خاص"},new Sport{Id=sport,AcademyId=academy,ArabicName="كرة اختبار خاص",Kind=SportKind.Football},new AgeCategory{Id=age,AcademyId=academy,ArabicName="فئة اختبار خاص",MinimumBirthYear=2014,MaximumBirthYear=2018},new TrainingLevel{Id=level,AcademyId=academy,SportId=sport,ArabicName="مستوى خاص",Rank=1});
            db.Add(new ApplicationUser{Id=user,UserName=coachPhone,NormalizedUserName=coachPhone,PhoneNumber="+2"+coachPhone,DisplayName="مدرب خاص خيالي",SecurityStamp=Guid.NewGuid().ToString()});
            db.Add(new AcademyMembership{Id=coach,AcademyId=academy,UserId=user,Role=AcademyRole.Coach,AllBranches=true,AllSports=true});
            db.Add(new TrainingGroup{Id=group,AcademyId=academy,BranchId=branch,SportId=sport,AgeCategoryId=age,TrainingLevelId=level,ArabicName="مجموعة فردية خيالية",Capacity=1,RegistrationFormat=TrainingRegistrationFormat.Private,FormatReviewedByUserId=owner,FormatReviewedAtUtc=now});
            db.Add(new StaffGroupAssignment{Id=Guid.NewGuid(),AcademyId=academy,AcademyMembershipId=coach,TrainingGroupId=group});
            db.AddRange(new Facility{Id=facility,AcademyId=academy,BranchId=branch,Name="مرفق خيالي"},new FacilitySpace{Id=space,AcademyId=academy,BranchId=branch,SportId=sport,FacilityId=facility,Kind=FacilitySpaceKind.Pitch,Name="ملعب خيالي"},new FacilityResource{Id=resource,AcademyId=academy,BranchId=branch,FacilitySpaceId=space,Name="مورد خيالي واحد",Capacity=1,SharedBookingsAllowed=false,OperatingDays=[0,1,2,3,4,5,6],OpensAt=new(8,0),ClosesAt=new(22,0),ServiceKinds=["Training"],ApprovedAtUtc=now,ApprovedByUserId=owner,ApprovalEvidence="اختبار آلي لبيانات خيالية"});
            db.Add(new GroupResourceSchedule{Id=Guid.NewGuid(),AcademyId=academy,TrainingGroupId=group,BranchId=branch,ResourceId=resource,CoachMembershipId=coach,ReservedSeats=1,Exclusive=true,ValidFrom=day,ValidTo=day.AddDays(30)});
            db.Add(new TrainingSession{Id=session,AcademyId=academy,TrainingGroupId=group,BranchId=branch,SportId=sport,AgeCategoryId=age,SessionDate=DateOnly.FromDateTime(now.UtcDateTime).AddDays(1),StartTime=start,EndTime=end,Status=TrainingSessionStatus.Scheduled,Source=TrainingSessionSource.Manual,CreatedByUserId=owner});
            db.Add(new ResourceBooking{Id=Guid.NewGuid(),AcademyId=academy,ResourceId=resource,BranchId=branch,TrainingGroupId=group,TrainingSessionId=session,CoachMembershipId=coach,Date=DateOnly.FromDateTime(now.UtcDateTime).AddDays(1),StartTime=start,EndTime=end,ServiceKind="Training",ReservedSeats=1,ApprovedCapacitySnapshot=1,Exclusive=true,State=ResourceBookingState.Reserved,RequestKey="private-fixture",RequestHash="synthetic",ActorUserId=owner});
            await db.SaveChangesAsync();
        }
        var book=FriendlyImportSchema.Template(academy,"EGP",day,"private-platform-test");
        void Add(string sheet,params string[] row){book=new(book.Sheets.Select(s=>s.Name==sheet?s with{Rows=s.Rows.Append(ConnectedImportSchema.Row(row)).ToArray()}:s).ToArray());}
        Add("الفروع","BR","فرع اختبار خاص",branch.ToString());Add("الرياضات","SP","كرة اختبار خاص","كرة قدم",sport.ToString());Add("الفئات العمرية","AG","فئة اختبار خاص","2014","2018",age.ToString());Add("مستويات التدريب","LV","مستوى خاص","SP","1",level.ToString());Add("المدربون","CO","مدرب خاص خيالي",coachPhone,"BR","SP",coach.ToString());
        Add("المجموعات","GR","مجموعة فردية خيالية","BR","SP","AG","1","LV","PRIVATE","CO",group.ToString());Add("الباقات","PK","أربع حصص خاصة خيالية","SP","BR","حصص","800","","4","PRIVATE","","");Add("أولياء الأمور","GU","ولي خاص خيالي","01296"+Random.Shared.Next(100000,999999),"");Add("اللاعبون","PL","لاعب خاص خيالي","2015-03-15","ذكر","GU","2026-10-26");Add("علاقات أولياء الأمور","REL","PL","GU","أب");Add("التسجيلات","EN","PL","BR","SP","GR","PK","LV","PER","2026-10-26","2026-11-25","4","4","800","EGP");
        async Task<Guid> Publish(byte[] bytes){var preview=await Preview(platform,academy,bytes);Assert.True(preview.CanApprove,JsonSerializer.Serialize(preview.Errors));var staged=await Stage(platform,academy,bytes,preview);staged.EnsureSuccessStatusCode();var id=(await staged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var job=await db.AcademyImportJobs.SingleAsync(j=>j.Id==id);job.State=ImportJobState.Running;await db.SaveChangesAsync();await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id);return id;}
        var first=await Publish(ImportXlsx.Write(book));Add("ابدأ هنا","تعديل عرض فقط","المصدر والأكواد والبيانات لم تتغير");var second=await Publish(ImportXlsx.Write(book));Assert.NotEqual(first,second);
        await using(var scope=factory.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(1,await db.SportEnrollments.CountAsync(e=>e.AcademyId==academy));Assert.Equal(1,await db.ResourceBookings.CountAsync(b=>b.AcademyId==academy));Assert.False(await db.Collections.AnyAsync(c=>c.AcademyId==academy));var job=await db.AcademyImportJobs.SingleAsync(j=>j.Id==second);Assert.Equal(ImportJobState.Completed,job.State);Assert.Equal(0,job.PublishedRows);Assert.Equal(1,job.ReplayedRows);
        }
    }
}
