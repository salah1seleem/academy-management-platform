using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    private static ImportWorkbook ConnectedChange(ImportWorkbook book,string sheet,int row,int col,string value)=>new(book.Sheets.Select(s=>s.Name!=sheet?s:s with{Rows=s.Rows.Select((r,i)=>i!=row?r:r.Select((c,j)=>j==col?new ImportCell(value,true):c).ToArray()).ToArray()}).ToArray());
    private async Task<ImportWorkbook> ConnectedSample(HttpClient owner,int count=10)
    {
        var book=ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/connected-sample"));
        var source="test-connected-"+Guid.NewGuid().ToString("N");
        // Unique, isolated test identities. The shipped 10-player fixture itself is deterministic.
        var sample=ConnectedImportSchema.Template(DemoSeed.NogoomAcademyId,"EGP",new(2026,9,28),true,count,source,Random.Shared.Next(1000000,7000000));
        // Separate scenarios must not compete for the same unique structural names.
        foreach(var sheet in new[]{"الفروع","الرياضات","الفئات العمرية","مستويات التدريب","المجموعات","الباقات","العروض","البطولات","الفرق"}) {
            var field=Array.FindIndex(ConnectedImportSchema.Sheets[sheet],f=>f.Key=="Name");
            sample=new(sample.Sheets.Select(s=>s.Name==sheet?s with{Rows=s.Rows.Select((r,i)=>i==0?r:r.Select((c,j)=>j==field?c with{Value=c.Value+" "+source[^8..]}:c).ToArray()).ToArray()}:s).ToArray());
        }
        return sample;
    }
    [Fact]
    public async Task Approved_import_schema_matches_actual_downloads_and_has_no_business_side_effects()
    {
        using var owner=await Staff();
        async Task<(int Players,int Enrollments,int Collections,int Receipts)> BusinessCounts(){await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();return(await db.Players.CountAsync(),await db.SportEnrollments.CountAsync(),await db.Collections.CountAsync(),await db.Receipts.CountAsync());}
        var before=await BusinessCounts();
        var schema=await owner.GetFromJsonAsync<JsonElement>("/api/v1/people/import/schema");
        Assert.Equal(ImportXlsx.MaxFileBytes,schema.GetProperty("maxFileBytes").GetInt32());
        Assert.Equal("iAcademy.Connected.3",schema.GetProperty("connectedVersion").GetString());
        var names=schema.GetProperty("connectedSheets").EnumerateArray().Select(s=>s.GetProperty("name").GetString()).ToArray();
        Assert.Equal(AcademyWorkbookSchema.Names,names);
        Assert.Contains("المدربون",names); Assert.Contains("البطولات",names);
        Assert.Contains("اللاعبون",names);
        foreach(var removed in new[]{"أنماط التدريب","مستويات التدريب","العروض","الأرصدة الافتتاحية","علاقات أولياء الأمور"})Assert.DoesNotContain(removed,names);
        var blank=ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/connected-template"));
        Assert.Equal(names,blank.Sheets.Select(s=>s.Name));
        Assert.Single(blank.Sheet("اللاعبون").Rows);
        var sample=ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/connected-sample"));
        Assert.Equal(names,sample.Sheets.Select(s=>s.Name));
        Assert.Equal(50,sample.Sheet("اللاعبون").Rows.Count-1);
        Assert.Equal(50,sample.Sheet("اللاعبون").Rows.Skip(1).Select(r=>r[0].Value).Distinct().Count());
        Assert.True(AcademyWorkbookSchema.IsCurrent(sample));
        Assert.Equal(before,await BusinessCounts());
        using var guardian=await Guardian();
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync("/api/v1/people/import/schema")).StatusCode);
    }
    [Fact]
    public async Task Current_downloaded_fifty_player_sample_is_accepted_and_reimported_without_rebuilding_it()
    {
        // The shipped workbook is an opening-data sample. Use its actual fresh
        // academy workflow, rather than a legacy seed whose identically named
        // sports have deliberately different kinds. Do not rewrite the file.
        using var platform=factory.CreateClient();
        async Task<HttpResponseMessage> PlatformWrite(string path,object body){var csrf=await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await platform.SendAsync(request);}
        (await PlatformWrite("/api/v1/platform/auth/login",new{email=Academy.Api.Platform.PlatformIdentity.DemoEmail,password="Demo-Only-123!",code="246810"})).EnsureSuccessStatusCode();
        var email=Guid.NewGuid()+"@example.test";
        var created=await PlatformWrite("/api/v1/platform/academies",new Academy.Api.Platform.PlatformAcademyWrite(Guid.NewGuid(),"sample-"+Guid.NewGuid().ToString("N")[..12],"أكاديمية قالب افتتاحي خيالية","EGP","Africa/Cairo","مالك قالب خيالي",email,"Demo-Only-123!","multi-sport","01082"+Random.Shared.Next(100000,999999)));created.EnsureSuccessStatusCode();var academy=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await PlatformWrite($"/api/v1/platform/academies/{academy}/prepare-operation",new{})).EnsureSuccessStatusCode();
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();(await db.Academies.SingleAsync(a=>a.Id==academy)).IsActive=true;await db.SaveChangesAsync();}
        using var owner=await Staff(email);var bytes=await owner.GetByteArrayAsync("/api/v1/people/import/connected-sample");var book=ImportXlsx.Read(bytes);
        var response=await PreviewFile(owner,bytes);response.EnsureSuccessStatusCode();var p=(await response.Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.True(p.CanApprove,string.Join(';',p.Errors.Select(e=>e.Message)));Assert.Equal(50,p.PlayerCount);Assert.Equal(50,p.RowCount);
        var id=await ImportId(await ImportStage(owner,book,p));
        await using(var scope=factory.Services.CreateAsyncScope())if(!await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyImportJobs.AnyAsync(j=>j.Id==id&&j.State==Academy.Infrastructure.People.ImportJobState.Completed))await ImportPublish(id);
        var replay=await ImportStage(owner,book,p); replay.EnsureSuccessStatusCode();
        var replayBody=await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id,replayBody.GetProperty("id").GetGuid());Assert.True(replayBody.GetProperty("replayedRequest").GetBoolean());
        await File.WriteAllBytesAsync("/tmp/i-academy-one-blank.xlsx",await owner.GetByteArrayAsync("/api/v1/people/import/connected-template"));
        await File.WriteAllBytesAsync("/tmp/i-academy-one-50-fictional-players.xlsx",bytes);
    }
    [Theory]
    [InlineData(10)]
    [InlineData(250)]
    public async Task Phase1_connected_workbook_download_upload_publish_replay_and_actual_links(int count)
    {
        using var owner=await Staff();var book=await ConnectedSample(owner,count);var watch=Stopwatch.StartNew();
        // The public blank follows the current player sheet. The generated
        // workbook below still exercises retained legacy import compatibility.
        var blank=ImportXlsx.Read(await owner.GetByteArrayAsync("/api/v1/people/import/connected-template"));Assert.Equal(AcademyWorkbookSchema.Names,blank.Sheets.Select(s=>s.Name));Assert.Single(blank.Sheet("اللاعبون").Rows);
        await using var beforeScope=factory.Services.CreateAsyncScope();var beforeDb=beforeScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before=(await beforeDb.Collections.CountAsync(),await beforeDb.Receipts.CountAsync(),await beforeDb.PaymentRequests.CountAsync(),await beforeDb.PlayerAttendances.CountAsync(),await beforeDb.AcademyNotices.CountAsync(),await beforeDb.TournamentPayments.CountAsync());
        var preview=await ImportCheck(owner,book);Assert.Equal((count,count+1,count-1,2,2*count-3),(preview.PlayerCount,preview.RowCount,preview.HouseholdCount,preview.CoachAssignments,preview.TournamentParticipations));
        Assert.Contains(preview.Effects,e=>e.Sheet=="المدربون"&&e.Create==2);Assert.False(await beforeDb.Branches.AnyAsync(b=>b.Id==ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,preview.SourceSystem!,"branch","B01")));
        var key=Guid.NewGuid().ToString();var id=await ImportId(await ImportStage(owner,book,preview,key));await ImportPublish(id);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var mapping=await db.ImportedEnrollments.Where(m=>m.JobId==id).ToArrayAsync();Assert.Equal(count+1,mapping.Length);Assert.Equal(count,mapping.Select(m=>m.PlayerId).Distinct().Count());Assert.Equal(count-1,mapping.Select(m=>m.GuardianId).Distinct().Count());
        var entries=await db.TournamentRegistrations.Where(e=>e.ImportJobId==id).ToArrayAsync();Assert.Equal(2*count-3,entries.Length);Assert.Equal(count-1,entries.Select(e=>e.EnrollmentId).Distinct().Count());
        var groupIds=preview.Rows.Select(r=>r.GroupId).Distinct().ToArray();Assert.Equal(2,await db.StaffGroupAssignments.CountAsync(a=>groupIds.Contains(a.TrainingGroupId)&&a.AcademyMembership.Role==AcademyRole.Coach));
        var sessions=await db.TrainingSessions.Where(s=>groupIds.Contains(s.TrainingGroupId)).ToArrayAsync();Assert.Equal(8,sessions.Length);Assert.Equal(sessions.Length,sessions.Select(s=>(s.TrainingGroupId,s.SessionDate,s.StartTime)).Distinct().Count());
        Assert.All(await db.SubscriptionPeriods.Where(p=>p.ImportJobId==id).ToListAsync(),p=>{Assert.Equal(SubscriptionOrigin.ImportedHistorical,p.Origin);Assert.Null(p.CollectionId);Assert.Equal(6,p.RemainingSessions);});
        Assert.Equal(before,(await db.Collections.CountAsync(),await db.Receipts.CountAsync(),await db.PaymentRequests.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.AcademyNotices.CountAsync(),await db.TournamentPayments.CountAsync()));
        // Identical bytes under either request key return the same committed job, even at full capacity.
        Assert.Equal(id,await ImportId(await ImportStage(owner,book,preview,key)));Assert.Equal(id,await ImportId(await ImportStage(owner,book,preview)));
        // Re-serialized sheet order changes bytes but must replay existing rows and relations safely.
        var reordered=new ImportWorkbook(book.Sheets.Reverse().ToArray());var replay=await ImportCheck(owner,reordered);var retry=await ImportId(await ImportStage(owner,reordered,replay));await ImportPublish(retry);
        var job=await db.AcademyImportJobs.AsNoTracking().SingleAsync(j=>j.Id==retry);Assert.Equal((0,count+1),(job.PublishedRows,job.ReplayedRows));Assert.Equal(8,await db.TrainingSessions.CountAsync(s=>groupIds.Contains(s.TrainingGroupId)));Assert.Equal(2*count-3,await db.TournamentRegistrations.CountAsync(e=>entries.Select(x=>x.Id).Contains(e.Id)));
        var importedBranch=preview.Rows[0].BranchId;
        var subscriptionSummary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28&branchId={importedBranch}");
        var subscriptionCounts=subscriptionSummary.GetProperty("subscriptions");
        Assert.Equal(count+1,subscriptionCounts.GetProperty("total").GetInt32());
        var branchCounts=subscriptionCounts.GetProperty("branches").EnumerateArray().ToArray();Assert.Single(branchCounts);
        Assert.Equal(importedBranch,branchCounts[0].GetProperty("id").GetGuid());Assert.Equal(count+1,branchCounts[0].GetProperty("count").GetInt32());
        Assert.Equal(subscriptionCounts.GetProperty("total").GetInt32(),branchCounts.Sum(b=>b.GetProperty("count").GetInt32()));
        if(count==10) {
            using var mobileGuardian=factory.CreateClient();var family=mapping.Single(m=>m.LegacyEnrollmentId=="E001A");var guardianUser=await db.GuardianProfiles.Where(g=>g.Id==family.GuardianId).Select(g=>g.UserId).SingleAsync();await ImportedMobileSession(mobileGuardian,guardianUser,AcademyRole.Guardian);
            var children=await mobileGuardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/children");Assert.Equal(2,children.GetArrayLength());
            var stranger=mapping.Single(m=>m.LegacyEnrollmentId=="E003A").PlayerId;Assert.Equal(HttpStatusCode.NotFound,(await mobileGuardian.GetAsync($"/api/v1/guardian/children/{stranger}")).StatusCode);
            var memberships=await mobileGuardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/tournament-memberships");Assert.Single(memberships.EnumerateArray());Assert.Equal(mapping.Single(m=>m.LegacyEnrollmentId=="E002A").PlayerId,memberships[0].GetProperty("playerId").GetGuid());
            var team=ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,preview.SourceSystem!,"team","TM01");
            var t1=ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,preview.SourceSystem!,"tournament","T01");var t2=ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,preview.SourceSystem!,"tournament","T02");
            var matchIds=new List<Guid>();foreach(var tournament in new[]{t1,t2}) {
                var match=await Write(owner,"/api/v1/manage/matches/",new MatchWrite(tournament,team,"فريق خصم خيالي",new(2026,10,1),new(17,0),"ملعب تجريبي",null,null,null,null,true,true));Assert.True(match.IsSuccessStatusCode,await match.Content.ReadAsStringAsync());matchIds.Add((await match.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            }
            var child2=mapping.Single(m=>m.LegacyEnrollmentId=="E002A").PlayerId;
            Assert.True(await db.MatchParticipants.AnyAsync(p=>p.MatchId==matchIds[0]&&p.PlayerId==child2));Assert.False(await db.MatchParticipants.AnyAsync(p=>p.MatchId==matchIds[1]&&p.PlayerId==child2));
            var mine=await mobileGuardian.GetFromJsonAsync<JsonElement>("/api/v1/my-matches?allDates=true");Assert.Contains(matchIds[0].ToString(),mine.GetRawText());Assert.DoesNotContain(matchIds[1].ToString(),mine.GetRawText());
            var coachId=ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,source:preview.SourceSystem!,kind:"coach",code:"C01");
            using var mobileCoach=factory.CreateClient();await ImportedMobileSession(mobileCoach,await db.AcademyMemberships.Where(m=>m.Id==coachId).Select(m=>m.UserId).SingleAsync(),AcademyRole.Coach);
            var groups=await mobileCoach.GetFromJsonAsync<JsonElement>("/api/v1/coach/groups");Assert.Single(groups.EnumerateArray());Assert.Equal(preview.Rows[0].GroupId,groups[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.Forbidden,(await mobileCoach.GetAsync("/api/v1/guardian/tournament-memberships")).StatusCode);
            var actualRecords=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/import/jobs/{id}/records");Assert.Equal(11,actualRecords.GetArrayLength());
        }
        var result=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/import/jobs/{retry}");Assert.Equal(2*count-3,result.GetProperty("result").GetProperty("tournamentParticipations").GetInt32());
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/people/import/jobs/{id}/records")).StatusCode);
        var denied=await (await PreviewFile(other,ImportXlsx.Write(book))).Content.ReadFromJsonAsync<ImportPreviewResult>();Assert.False(denied!.CanApprove);
        if(count==250) {
            var pageWatch=Stopwatch.StartNew();var branch=preview.Rows[0].BranchId;
            var first=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?branchId={branch}&page=1&pageSize=25&sort=value_desc");
            var second=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?branchId={branch}&page=2&pageSize=25&sort=value_desc");
            Assert.Equal(250,first.GetProperty("total").GetInt32());Assert.Equal(25,first.GetProperty("items").GetArrayLength());Assert.Equal(25,second.GetProperty("items").GetArrayLength());
            Assert.Empty(first.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("id").GetGuid()).Intersect(second.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("id").GetGuid())));
            pageWatch.Stop();await File.WriteAllTextAsync("/tmp/academy-phase1-pagination.json",JsonSerializer.Serialize(new{rows=250,pageSize=25,distinctPages=true,twoPageElapsedMs=pageWatch.ElapsedMilliseconds}));
        }
        watch.Stop();await File.WriteAllTextAsync($"/tmp/academy-phase1-{count}-reconciliation.json",JsonSerializer.Serialize(new{players=count,enrollments=count+1,guardians=count-1,coachAssignments=2,tournamentParticipations=2*count-3,sessions=8,collectionsCreated=0,replayRows=count+1,elapsedMs=watch.ElapsedMilliseconds}));

    }
    [Theory]
    [InlineData("المجموعات",8,"C404")]
    [InlineData("المجموعات",7,"PRIVATE")]
    [InlineData("الأعضاء",25,"T404")]
    [InlineData("الأعضاء",10,"G404")]
    [InlineData("الأعضاء",26,"")]
    [InlineData("الحصص",4,"15:00")]
    public async Task Phase1_connected_invalid_reference_is_row_error_then_corrected_upload_succeeds(string sheet,int column,string bad)
    {
        using var owner=await Staff();var book=await ConnectedSample(owner);var broken=ConnectedChange(book,sheet,2,column,bad);
        var response=await PreviewFile(owner,ImportXlsx.Write(broken));response.EnsureSuccessStatusCode();var p=(await response.Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.False(p.CanApprove);Assert.Contains(p.Errors,e=>e.Sheet==sheet&&e.Row==3&&!string.IsNullOrEmpty(e.Column));
        Assert.Equal(HttpStatusCode.Conflict,(await ImportStage(owner,broken,p)).StatusCode);
        var good=await ImportCheck(owner,book);var id=await ImportId(await ImportStage(owner,book,good));await ImportPublish(id);
    }
    [Fact]
    public async Task Phase1_connected_interrupted_publication_rolls_back_structure_and_reclaims_lease()
    {
        using var owner=await Staff();var book=await ConnectedSample(owner);var p=await ImportCheck(owner,book);var id=await ImportId(await ImportStage(owner,book,p));Assert.Equal(id,await ImportWorker.Claim(ImportScopes,TimeProvider.System));
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id,cancel.Token));Assert.False(await db.ImportedEnrollments.AnyAsync(e=>e.JobId==id));var job=await db.AcademyImportJobs.SingleAsync(j=>j.Id==id);job.LeaseUntilUtc=DateTimeOffset.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
        await ImportPublish(id);await using var verify=factory.Services.CreateAsyncScope();var d=verify.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(11,await d.ImportedEnrollments.CountAsync(e=>e.JobId==id));Assert.Equal(17,await d.TournamentRegistrations.CountAsync(e=>e.ImportJobId==id));
    }
    [Fact]
    public async Task Phase1_connected_late_storage_failure_publishes_nothing_then_worker_retry_succeeds()
    {
        using var owner=await Staff();var book=await ConnectedSample(owner);var p=await ImportCheck(owner,book);var id=await ImportId(await ImportStage(owner,book,p));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION phase1_test_fail_link() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic interruption'; END $$; CREATE TRIGGER phase1_test_fail_link BEFORE INSERT ON \"TournamentRegistrations\" FOR EACH ROW EXECUTE FUNCTION phase1_test_fail_link();");
        try {
            Assert.Equal(id,await ImportWorker.Claim(ImportScopes,TimeProvider.System));
            await using(var attempt=factory.Services.CreateAsyncScope())await Assert.ThrowsAsync<DbUpdateException>(()=>attempt.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id));
            Assert.False(await db.ImportedEnrollments.AnyAsync(e=>e.JobId==id));
            Assert.False(await db.Branches.AnyAsync(b=>b.Id==ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,p.SourceSystem!,"branch","B01")));
            Assert.False(await db.AcademyMemberships.AnyAsync(m=>m.Id==ConnectedImportPlan.Id(DemoSeed.NogoomAcademyId,p.SourceSystem!,"coach","C01")));
            await ImportWorker.Fail(ImportScopes,id,"storage",DateTimeOffset.UtcNow);
        } finally {await db.Database.ExecuteSqlRawAsync("DROP TRIGGER phase1_test_fail_link ON \"TournamentRegistrations\"; DROP FUNCTION phase1_test_fail_link();");}
        await ImportPublish(id);Assert.Equal(11,await db.ImportedEnrollments.CountAsync(e=>e.JobId==id));
    }
    [Fact]
    public async Task Phase1_connected_admin_requires_structure_staff_and_match_authority_again_in_worker()
    {
        using var owner=await Staff();var book=await ConnectedSample(owner);var email="phase1.import."+Guid.NewGuid().ToString("N")+"@example.test";
        var created=await Write(owner,"/api/v1/manage/staff",new StaffWrite("إداري استيراد تجريبي",email,"010"+Random.Shared.Next(10000000,99999999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"اختبار"));created.EnsureSuccessStatusCode();var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var modules=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,true,m!="followups",true,false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",new StaffAccessWrite(true,[],modules),true)).EnsureSuccessStatusCode();using var admin=await Staff(email);var p=await ImportCheck(admin,book);var id=await ImportId(await ImportStage(admin,book,p));
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",new StaffAccessWrite(true,[],modules.Select(m=>m.Module=="staff"?m with{Add=false}:m).ToArray()),true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/people/import/connected-template")).StatusCode);
        Assert.Equal(id,await ImportWorker.Claim(ImportScopes,TimeProvider.System));await using(var attempt=factory.Services.CreateAsyncScope()){var ex=await Assert.ThrowsAsync<ImportRejected>(()=>attempt.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id));Assert.Equal("authority",ex.Code);}
        await ImportWorker.Fail(ImportScopes,id,"authority",DateTimeOffset.UtcNow);
    }
    [Theory]
    [InlineData(Academy.Infrastructure.People.TrainingRegistrationFormat.Private,"PRIVATE")]
    [InlineData(Academy.Infrastructure.People.TrainingRegistrationFormat.SemiPrivate,"SEMI")]
    public async Task Phase1_connected_private_modes_reuse_reviewed_group_resource_and_identity_models(Academy.Infrastructure.People.TrainingRegistrationFormat format,string mode)
    {
        await using var license=await FacilityLicenseScope.Begin(factory.Services);using var owner=await Staff();var f=await PlacementReady(owner,secondPaid:false);
        (await Write(owner,$"/api/v1/manage/groups/{f.Group}/training-format",new GroupTrainingFormatWrite(format,true),true)).EnsureSuccessStatusCode();
        var resource=await FacilityFixture(owner,branch:f.Branch);var day=PlacementToday.AddDays(1);
        var timetable=await Reviewed(owner,f.Group,new TimetableWrite(day,day,[day.DayOfWeek],new(10,0),new(11,0),new(resource,f.Coach,2,false)));(await SaveTimetable(owner,f.Group,timetable,Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await db.TrainingGroups.Include(g=>g.Branch).Include(g=>g.Sport).Include(g=>g.AgeCategory).SingleAsync(g=>g.Id==f.Group);var level=await db.TrainingLevels.SingleAsync(l=>l.Id==f.Level);var coach=await db.AcademyMemberships.Include(m=>m.User).SingleAsync(m=>m.Id==f.Coach);
        var book=ConnectedImportSchema.Template(DemoSeed.NogoomAcademyId,"EGP",new(2026,9,28),source:"test-private-"+Guid.NewGuid().ToString("N"));
        void Row(string sheet,params string[] values){book=new(book.Sheets.Select(s=>s.Name==sheet?s with{Rows=new[]{s.Rows[0],ConnectedImportSchema.Row(values.Concat(Enumerable.Repeat("",Math.Max(0,ConnectedImportSchema.Sheets[sheet].Length-values.Length))).ToArray())}}:s).ToArray());}
        Row("الفروع","B01",g.Branch.ArabicName,f.Branch.ToString());Row("الرياضات","S01",g.Sport.ArabicName,"سباحة",g.SportId.ToString());Row("الفئات العمرية","A01",g.AgeCategory.ArabicName,g.AgeCategory.MinimumBirthYear?.ToString()??"",g.AgeCategory.MaximumBirthYear?.ToString()??"",g.AgeCategoryId.ToString());
        Row("مستويات التدريب","L01",level.ArabicName,"S01",level.Rank.ToString(),level.Id.ToString());Row("المدربون","C01",coach.User.DisplayName,coach.User.PhoneNumber!,"B01","S01",coach.Id.ToString());Row("المجموعات","G01",g.ArabicName,"B01","S01","A01",g.Capacity!.Value.ToString(),"L01",mode,"C01",g.Id.ToString());
        Row("الباقات","P01","باقة استيراد خاصة "+Guid.NewGuid().ToString("N")[..8],"S01","B01","مدة","600","365","",mode);
        Row("الأعضاء","PX01","EX01","HX01","لاعب استيراد خاص خيالي","ولي خاص خيالي","012"+Random.Shared.Next(10000000,89999999),(g.AgeCategory.MinimumBirthYear??2017)+"-01-01","B01","S01","L01","G01","P01","2026-09-28","2027-09-27","","","agreed_net","","","600","EGP","قائم");
        var before=await db.Collections.CountAsync();var preview=await ImportCheck(owner,book);var id=await ImportId(await ImportStage(owner,book,preview));await ImportPublish(id);
        var enrollment=await db.SportEnrollments.SingleAsync(e=>db.ImportedEnrollments.Any(m=>m.JobId==id&&m.SportEnrollmentId==e.Id));Assert.Equal(format,enrollment.RegistrationFormat);Assert.Equal(f.Group,enrollment.TrainingGroupId);Assert.Equal(f.Level,enrollment.TrainingLevelId);Assert.NotNull(enrollment.LevelReviewedAtUtc);Assert.Equal(before,await db.Collections.CountAsync());
        Assert.Equal(id,await ImportId(await ImportStage(owner,book,preview)));
    }
    private async Task ImportedMobileSession(HttpClient client,Guid userId,AcademyRole role)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var phone=await db.Users.Where(u=>u.Id==userId).Select(u=>u.PhoneNumber).SingleAsync();
        var requested=await client.PostAsJsonAsync("/api/v1/mobile/auth/otp/request",new {phoneNumber=phone});Assert.True(requested.IsSuccessStatusCode,await requested.Content.ReadAsStringAsync());var challenge=(await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        var verified=await client.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify",new {challengeId=challenge,phoneNumber=phone,code="246810",deviceName="Phase1 isolated test"});Assert.True(verified.IsSuccessStatusCode,await verified.Content.ReadAsStringAsync());var credentials=(await verified.Content.ReadFromJsonAsync<Academy.Api.Mobile.MobileCredentials>())!;
        Assert.Single(credentials.Memberships);Assert.Equal(role.ToString(),credentials.Memberships[0].Role);client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",credentials.AccessToken);
    }
    private async Task<HttpResponseMessage> RegisterException(HttpClient client,RegistrationRequest body,string key)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/people/registrations"){Content=JsonContent.Create(body)};request.Headers.Add("Idempotency-Key",key);request.Headers.Add("X-CSRF-TOKEN",(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());return await client.SendAsync(request);
    }
    private RegistrationRequest ExceptionalRegistration(Guid branch,Guid group,Guid plan,int birth,RenewalDiscountType type,decimal value,decimal expected)=>new(null,"لاعب خصم خيالي",new(birth,1,1),Academy.Infrastructure.People.Gender.Male,null,"ولي خصم خيالي","015"+Random.Shared.Next(10000000,89999999),"ولي أمر",branch,Slice2DemoSeed.SwimmingId,group,plan,"Online",ExpectedAmount:expected,ExceptionalDiscount:new(type,value,"حالة إدارية تجريبية موثقة"));
    [Theory]
    [InlineData(RenewalDiscountType.FixedAmount,100,900)]
    [InlineData(RenewalDiscountType.Percentage,15,850)]
    [InlineData(RenewalDiscountType.Percentage,100,0)]
    public async Task Phase1_exceptional_registration_persists_price_audit_and_retries_once(RenewalDiscountType type,decimal value,decimal expected)
    {
        using var owner=await Staff();var f=await Fixture(false);var created=await Write(owner,"/api/v1/subscriptions/plans",Body(f.A,"باقة خصم اختبار "+Guid.NewGuid().ToString("N")[..8],1000));created.EnsureSuccessStatusCode();var plan=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body=ExceptionalRegistration(f.A,f.Group,plan,f.BirthYear,type,value,expected);var key=Guid.NewGuid().ToString();
        var quote=await Write(owner,"/api/v1/people/registration-quote",new RegistrationQuote(plan,body.GuardianPhone,null,f.A,body.ExceptionalDiscount));quote.EnsureSuccessStatusCode();Assert.Equal(expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),(await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("finalAmount").GetString());
        var r=await RegisterException(owner,body,key);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());var saved=(await r.Content.ReadFromJsonAsync<JsonElement>());var player=saved.GetProperty("playerId").GetGuid();
        var replayResponse=await RegisterException(owner,body,key);replayResponse.EnsureSuccessStatusCode();var replay=await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saved.GetProperty("periodId").GetGuid(),replay.GetProperty("periodId").GetGuid());Assert.Equal(saved.GetProperty("sportEnrollmentId").GetGuid(),replay.GetProperty("sportEnrollmentId").GetGuid());
        Assert.Equal(expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),saved.GetProperty("pricing").GetProperty("finalAmount").GetString());
        Assert.Equal(plan,saved.GetProperty("planId").GetGuid());
        var storedPeriod=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{saved.GetProperty("periodId").GetGuid()}");
        Assert.Equal(saved.GetProperty("sportEnrollmentId").GetGuid(),storedPeriod.GetProperty("sportEnrollmentId").GetGuid());Assert.Equal(plan,storedPeriod.GetProperty("subscriptionPlanId").GetGuid());
        if(expected>0){Assert.Equal(saved.GetProperty("receiptId").GetGuid(),storedPeriod.GetProperty("receiptId").GetGuid());var storedReceipt=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{saved.GetProperty("receiptId").GetGuid()}");Assert.Equal(expected,storedReceipt.GetProperty("amount").GetDecimal());}
        else{Assert.Equal(JsonValueKind.Null,saved.GetProperty("receiptId").ValueKind);Assert.Equal(JsonValueKind.Null,saved.GetProperty("paymentId").ValueKind);Assert.NotEqual(Guid.Empty,saved.GetProperty("waiverId").GetGuid());}
        Assert.Equal(HttpStatusCode.Conflict,(await RegisterException(owner,body with{ExceptionalDiscount=body.ExceptionalDiscount! with{Reason="سبب مختلف"}},key)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var renewal=await db.RenewalRequests.SingleAsync(x=>x.SportEnrollment.PlayerId==player);Assert.Equal((1000,1000-expected,expected),(renewal.OriginalAmount,renewal.DiscountAmount,renewal.FinalAmount));Assert.NotNull(renewal.DiscountAppliedByUserId);Assert.NotNull(renewal.DiscountAppliedAtUtc);Assert.Null(renewal.DiscountRuleIdSnapshot);Assert.Single(await db.RenewalDiscountAdjustments.Where(a=>a.RenewalRequestId==renewal.Id).ToListAsync());Assert.Equal(1000,(await db.SubscriptionPlans.SingleAsync(p=>p.Id==plan)).Price);Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollment.PlayerId==player).ToListAsync());
        using var periodGuardian=factory.CreateClient();await ImportedMobileSession(periodGuardian,await db.GuardianPlayerLinks.Where(l=>l.PlayerId==player).Select(l=>l.Guardian.UserId).SingleAsync(),AcademyRole.Guardian);
        var periodId=await db.SubscriptionPeriods.Where(p=>p.SportEnrollment.PlayerId==player).Select(p=>p.Id).SingleAsync();var periodView=await periodGuardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/periods/{periodId}/adjustments");
        Assert.Equal(expected,periodView.GetProperty("pricing").GetProperty("finalAmount").GetDecimal());Assert.Equal(1000,periodView.GetProperty("pricing").GetProperty("originalAmount").GetDecimal());Assert.False(periodView.GetProperty("pricing").TryGetProperty("discountReason",out _));
        if(expected>0){var receipt=await db.Receipts.SingleAsync(r=>r.PlayerId==player);Assert.Equal((1000,1000-expected,expected),(receipt.OriginalAmount,receipt.DiscountAmount,receipt.FinalAmount));
            var classified=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&planId={plan}&collectionType=Subscription&subscriptionKind=Enrollment");
            Assert.Equal(expected,classified.GetProperty("totalAmount").GetDecimal());
            Assert.Single(classified.GetProperty("items").EnumerateArray());
            Assert.Equal(0m,(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&planId={plan}&collectionType=Subscription&subscriptionKind=Renewal")).GetProperty("totalAmount").GetDecimal());
            using var guardian=factory.CreateClient();await ImportedMobileSession(guardian,await db.GuardianPlayerLinks.Where(l=>l.PlayerId==player).Select(l=>l.Guardian.UserId).SingleAsync(),AcademyRole.Guardian);
            var view=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/receipts/{receipt.Id}");Assert.Equal(expected,view.GetProperty("finalAmount").GetDecimal());Assert.Equal(1000-expected,view.GetProperty("discountAmount").GetDecimal());Assert.False(view.TryGetProperty("discountReason",out _));
            }else{Assert.Null(renewal.PaymentRequestId);Assert.False(await db.Receipts.AnyAsync(r=>r.PlayerId==player));Assert.False(await db.Collections.AnyAsync(c=>c.SportEnrollment.PlayerId==player));Assert.Equal(SubscriptionOrigin.ApprovedWaiver,(await db.SubscriptionPeriods.SingleAsync(p=>p.SportEnrollment.PlayerId==player)).Origin);}
        var later=await Write(owner,"/api/v1/subscriptions/admin-renewals",new {sportEnrollmentId=renewal.SportEnrollmentId,subscriptionPlanId=plan});Assert.True(later.IsSuccessStatusCode,await later.Content.ReadAsStringAsync());var laterPayment=(await later.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        Assert.Equal(1000,await db.PaymentRequests.Where(p=>p.Id==laterPayment).Select(p=>p.Amount).SingleAsync());Assert.False(await db.PlayerDiscountLinks.AnyAsync(l=>l.PlayerId==player));
    }
    [Theory]
    [InlineData(RenewalDiscountType.Percentage,0)]
    [InlineData(RenewalDiscountType.Percentage,101)]
    [InlineData(RenewalDiscountType.FixedAmount,1001)]
    [InlineData((RenewalDiscountType)99,10)]
    public async Task Phase1_exceptional_registration_rejects_invalid_values_without_committed_player(RenewalDiscountType type,decimal value)
    {
        using var owner=await Staff();var f=await Fixture(false);var plan=await CreatePlan(owner,f.A);var body=ExceptionalRegistration(f.A,f.Group,plan,f.BirthYear,type,value,500);var response=await RegisterException(owner,body,Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);await using var scope=factory.Services.CreateAsyncScope();Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Users.AnyAsync(u=>u.PhoneNumber==body.GuardianPhone));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Phase1_exceptional_discount_requires_explicit_admin_leaf_and_rejects_offer_stacking(bool fullWaiver)
    {
        using var owner=await Staff();var f=await Fixture(false);var plan=await CreatePlan(owner,f.A);var email="phase1."+Guid.NewGuid().ToString("N")+"@example.test";
        var c=await Write(owner,"/api/v1/manage/staff",new StaffWrite("إداري خصم تجريبي",email,"010"+Random.Shared.Next(10000000,99999999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"اختبار"));c.EnsureSuccessStatusCode();var member=(await c.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var modules=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,true,m!="followups",true,false)).ToArray();await Write(owner,$"/api/v1/manage/staff/{member}/access",new StaffAccessWrite(true,[],modules),true);using var admin=await Staff(email);
        var body=ExceptionalRegistration(f.A,f.Group,plan,f.BirthYear,fullWaiver?RenewalDiscountType.Percentage:RenewalDiscountType.FixedAmount,100,fullWaiver?0:500);Assert.Equal(HttpStatusCode.Forbidden,(await RegisterException(admin,body,Guid.NewGuid().ToString())).StatusCode);
        var leaves=StaffWorkflowAccess.Actions.Select(a=>a.Key).ToArray();(await Write(owner,$"/api/v1/manage/staff/{member}/access",new StaffAccessWrite(false,[f.A],modules,WorkflowActions:leaves),true)).EnsureSuccessStatusCode();
        var okay=await RegisterException(admin,body,Guid.NewGuid().ToString());
        if(fullWaiver){Assert.Equal(HttpStatusCode.Forbidden,okay.StatusCode);await using var waiverScope=factory.Services.CreateAsyncScope();var waiverDb=waiverScope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await waiverDb.Users.AnyAsync(u=>u.PhoneNumber==body.GuardianPhone));return;}
        Assert.True(okay.IsSuccessStatusCode,await okay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await RegisterException(admin,body with{BranchId=f.B},Guid.NewGuid().ToString())).StatusCode);
        var rule=await AddRule(owner,FixedRule(f.A,plan));var conflicted=body with{GuardianPhone="015"+Random.Shared.Next(10000000,89999999),DiscountRuleId=rule};Assert.Equal(HttpStatusCode.Conflict,(await RegisterException(owner,conflicted,Guid.NewGuid().ToString())).StatusCode);
        var replace=await RegisterException(owner,conflicted with{ExceptionalDiscount=conflicted.ExceptionalDiscount! with{ReplaceExistingDiscount=true}},Guid.NewGuid().ToString());Assert.True(replace.IsSuccessStatusCode,await replace.Content.ReadAsStringAsync());
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await RegisterException(other,body,Guid.NewGuid().ToString())).StatusCode);
        using var guardian=await Guardian();Assert.Equal(HttpStatusCode.Forbidden,(await RegisterException(guardian,body,Guid.NewGuid().ToString())).StatusCode);

    }
}

public sealed class Phase1DiscountCalculationTests
{
    [Theory]
    [InlineData(1000,RenewalDiscountType.FixedAmount,100,900)]
    [InlineData(1000,RenewalDiscountType.Percentage,15,850)]
    [InlineData(0.05,RenewalDiscountType.Percentage,15,0.04)]
    [InlineData(1000,RenewalDiscountType.Percentage,33.333,666.70)]
    [InlineData(1000,RenewalDiscountType.FixedAmount,10.125,989.87)]
    public void Phase1_one_currency_rounding_contract(decimal original,RenewalDiscountType type,decimal value,decimal final)
    {
        var current=new RenewalFinancialSnapshot(original,null,null,0,original,null,null,null);
        var price=ManualEnrollmentDiscount.Apply(current,new(type,value,"اختبار تقريب مالي"),Guid.NewGuid(),DateTimeOffset.UtcNow);
        Assert.Equal(final,price.FinalAmount);Assert.Equal(original-final,price.DiscountAmount);
        Assert.Equal(decimal.Round(value,2,MidpointRounding.AwayFromZero),price.DiscountValue);
    }
    [Fact]
    public void Phase1_reason_and_deliberate_offer_replacement_are_required()
    {
        var current=new RenewalFinancialSnapshot(1000,RenewalDiscountType.FixedAmount,100,100,900,"عرض",Guid.NewGuid(),DateTimeOffset.UtcNow);
        Assert.Throws<Academy.Api.Finance.CashRejected>(()=>ManualEnrollmentDiscount.Apply(current,new(RenewalDiscountType.FixedAmount,100,""),Guid.NewGuid(),DateTimeOffset.UtcNow));
        Assert.Throws<Academy.Api.Finance.CashRejected>(()=>ManualEnrollmentDiscount.Apply(current,new(RenewalDiscountType.FixedAmount,100,"سبب"),Guid.NewGuid(),DateTimeOffset.UtcNow));
        var price=ManualEnrollmentDiscount.Apply(current,new(RenewalDiscountType.FixedAmount,150,"استبدال العرض",true),Guid.NewGuid(),DateTimeOffset.UtcNow);
        Assert.Equal(850,price.FinalAmount);Assert.Null(price.DiscountRuleId);
    }
}
