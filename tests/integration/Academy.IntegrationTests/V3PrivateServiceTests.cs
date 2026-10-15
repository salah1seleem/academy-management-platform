using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private async Task<(HttpClient Owner, Guid Coach, string Email, Guid Employee, Guid Document, Guid Enrollment, Guid Plan, Guid[] Sessions, Guid Actor, Guid Branch, Guid Sport, DateOnly Today)> PrivateFixture(bool isolatedBranch = false)
    {
        var f = await AgreementFixture();
        if (isolatedBranch)
        {
            // Summary verification must not depend on unrelated tests' historical collection fixtures.
            await using var isolatedScope = factory.Services.CreateAsyncScope(); var isolatedDb = isolatedScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            f.Branch = Guid.NewGuid(); isolatedDb.Branches.Add(new() { Id=f.Branch,AcademyId=DemoSeed.NogoomAcademyId,ArabicName="فرع مطابقة خاص اصطناعي "+f.Branch });
            (await isolatedDb.Employees.SingleAsync(e=>e.Id==f.Employee)).BranchId=f.Branch; await isolatedDb.SaveChangesAsync();
        }
        var agreement = await AgreementId(f.Owner, f.Coach, new(f.Branch, f.Sport, f.Today, null, "Percent", 40, "DeliveredCollected", "GroupOnly"));
        (await Write(f.Owner, $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements/{agreement}/approve", new CoachAgreementApprovalWrite(null), false)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var a = DemoSeed.NogoomAcademyId; var now = DateTimeOffset.UtcNow;
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); var age = await db.AgeCategories.Where(c => c.AcademyId == a && c.IsActive).Select(c => c.Id).FirstAsync();
        var group = Guid.NewGuid(); var player = Guid.NewGuid(); var enrollment = Guid.NewGuid(); var plan = Guid.NewGuid(); var doc = Guid.NewGuid();
        db.TrainingGroups.Add(new() { Id = group, AcademyId = a, ArabicName = "خاص اصطناعي " + group, BranchId = f.Branch, SportId = f.Sport, AgeCategoryId = age, RegistrationFormat = TrainingRegistrationFormat.Private, Capacity = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = a, TrainingGroupId = group, AcademyMembershipId = f.Coach });
        db.Players.Add(new() { Id = player, AcademyId = a, ArabicName = "طفل مصادر خاص اصطناعي", PlayerCode = "PR-" + player.ToString("N")[..12], DateOfBirth = new(2016,1,1) });
        db.SportEnrollments.Add(new() { Id = enrollment, AcademyId = a, PlayerId = player, BranchId = f.Branch, SportId = f.Sport, TrainingGroupId = group, GroupAssignedFrom = f.Today, RegistrationFormat = TrainingRegistrationFormat.Private });
        db.SubscriptionPlans.Add(new() { Id = plan, AcademyId = a, SportId = f.Sport, ArabicName = "باقة8 خاصة " + plan, PlanType = SubscriptionPlanType.Sessions, TrainingFormat = TrainingFormat.Private, SessionCount = 8, Price = 2000, Currency = "EGP", Revision = 1 });
        db.ReceivableDocuments.Add(new() { Id = doc, AcademyId = a, SportEnrollmentId = enrollment, PlayerId = player, PlayerNameSnapshot = "طفل مصادر خاص اصطناعي", BranchId = f.Branch, PlanId = plan, PlanNameSnapshot = "باقة 8 خاصة", PlanPriceSnapshot = 2000, Description = "مطالبة الخدمة الصناعية", Amount = 2000, Currency = "EGP", DueDate = f.Today, Source = ReceivableSource.ManualCharge, SourceKey = doc.ToString(), ContentHash = "PRIVATE-TEST", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        var sessions = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < sessions.Length; i++) db.TrainingSessions.Add(new() { Id = sessions[i], AcademyId = a, TrainingGroupId = group, BranchId = f.Branch, SportId = f.Sport, AgeCategoryId = age, SessionDate = i == 3 ? f.Today.AddDays(1) : f.Today, StartTime = new(0,0,i * 2), EndTime = new(0,0,i * 2 + 1), Source = TrainingSessionSource.Manual, CreatedByUserId = actor });
        await db.SaveChangesAsync(); return (f.Owner, f.Coach, f.Email, f.Employee, doc, enrollment, plan, sessions, actor, f.Branch, f.Sport, f.Today);
    }
    private async Task<Guid> PrivatePayment(HttpClient owner, Guid document, Guid enrollment, Guid plan, Guid actor, decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var a = DemoSeed.NogoomAcademyId; var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var key = id.ToString(); var e = await db.SportEnrollments.SingleAsync(e => e.Id == enrollment);
        var r = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = a, SportEnrollmentId = enrollment, SportId = e.SportId, SubscriptionPlanId = plan, RequestedByUserId = actor, RequestedAtUtc = now, OriginalAmount = amount, FinalAmount = amount, AmountExpected = amount, Currency = "EGP", Status = RenewalRequestStatus.Paid, IdempotencyKey = key };
        var p = new PaymentRequest { Id = Guid.NewGuid(), AcademyId = a, RenewalRequestId = r.Id, Amount = amount, Currency = "EGP", Status = PaymentRequestStatus.Confirmed, Provider = "InternalTest", ProviderEnvironment = "Demo", ProviderReference = key, CheckoutReference = key, ConfirmedAtUtc = now };
        db.RenewalRequests.Add(r); db.PaymentRequests.Add(p); db.Collections.Add(new() { Id = id, AcademyId = a, SportEnrollmentId = enrollment, RenewalRequestId = r.Id, PaymentRequestId = p.Id, Amount = amount, Currency = "EGP", PaymentMethod = "InternalTest", Provider = "InternalTest", ProviderReference = key, ConfirmedAtUtc = now, ConfirmedBy = "synthetic-test" }); await db.SaveChangesAsync();
        db.Receipts.Add(new() { Id=Guid.NewGuid(),AcademyId=a,CollectionId=id,ReceiptNumber="PR-"+id.ToString("N")[..12],PlayerId=e.PlayerId,SportEnrollmentId=enrollment,SubscriptionPlanId=plan,PlayerNameSnapshot="طفل مصادر خاص اصطناعي",SportNameSnapshot="رياضة اصطناعية",PlanNameSnapshot="باقة8 خاصة اصطناعية",OriginalAmount=amount,FinalAmount=amount,Amount=amount,Currency="EGP",PaidAtUtc=now,PaymentMethod="InternalTest",ProviderReference=key,CreatedAtUtc=now,UpdatedAtUtc=now }); await db.SaveChangesAsync();
        var allocated = await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{document}/movements", new { kind = "PaymentAllocation", amount, collectionId = id, reason = "تخصيص اصطناعي" });
        Assert.True(allocated.IsSuccessStatusCode, await allocated.Content.ReadAsStringAsync()); return id;
    }
    private static async Task<HttpResponseMessage> PrivateFinancialWrite(HttpClient c, string path, object body)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var r = new HttpRequestMessage(HttpMethod.Post,path) { Content = JsonContent.Create(body) };
        r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); r.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString()); return await c.SendAsync(r);
    }
    private static string PrivateRoot(Guid coach) => $"/api/v1/manage/payroll/coaches/{coach}/private-services";
    private static async Task<JsonElement[]> PrivateRows(HttpClient owner, Guid coach) => (await owner.GetFromJsonAsync<JsonElement>(PrivateRoot(coach))).GetProperty("items").EnumerateArray().ToArray();

    [Fact]
    public async Task V3_private_delivery_partial_collection_later_rate_and_credit_are_source_linked_without_posting_salary()
    {
        var f = await PrivateFixture(); using var owner = f.Owner; var root = PrivateRoot(f.Coach);
        var bound = await Write(owner, root + "/packages", new PrivatePackageWrite(f.Document, 8), false); Assert.Equal(HttpStatusCode.Created, bound.StatusCode); var package = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Write(owner, root + "/packages", new PrivatePackageWrite(f.Document, 8), false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, root + "/packages", new PrivatePackageWrite(f.Document, 7), false)).StatusCode);
        Assert.Empty(await PrivateRows(owner, f.Coach)); // binding and collection alone do not prove delivery
        await PrivatePayment(owner, f.Document, f.Enrollment, f.Plan, f.Actor, 1000);
        for (var i = 0; i < 2; i++) (await Write(owner, $"{root}/{package}/deliveries", new PrivateDeliveryWrite(f.Sessions[i], i+1), false)).EnsureSuccessStatusCode();
        var replay = await Task.WhenAll(Write(owner, $"{root}/{package}/deliveries", new PrivateDeliveryWrite(f.Sessions[0],1), false), Write(owner, $"{root}/{package}/deliveries", new PrivateDeliveryWrite(f.Sessions[0],1), false)); Assert.All(replay, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var half = await PrivateRows(owner, f.Coach); Assert.Equal(2, half.Length); Assert.Equal(500, half.Sum(r => r.GetProperty("serviceNet").GetDecimal())); Assert.Equal(250, half.Sum(r => r.GetProperty("eligible").GetDecimal())); Assert.Equal(100, half.Sum(r => r.GetProperty("earned").GetDecimal())); Assert.Equal(100, half.Sum(r => r.GetProperty("contingent").GetDecimal()));
        var old = half[0].GetProperty("agreementId").GetGuid(); var next = await AgreementId(owner, f.Coach, new(f.Branch, f.Sport, f.Today, null, "Percent", 30, "DeliveredCollected", "GroupOnly"));
        (await Write(owner, $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements/{next}/approve", new CoachAgreementApprovalWrite(old), false)).EnsureSuccessStatusCode();
        await PrivatePayment(owner, f.Document, f.Enrollment, f.Plan, f.Actor, 1000);
        var full = await PrivateRows(owner, f.Coach); Assert.All(full, r => Assert.Equal(old, r.GetProperty("agreementId").GetGuid())); Assert.Equal(200, full.Sum(r => r.GetProperty("earned").GetDecimal())); Assert.Equal(100, full.Sum(r => r.GetProperty("earned").GetDecimal()) - half.Sum(r => r.GetProperty("earned").GetDecimal())); Assert.Equal(0, full.Sum(r => r.GetProperty("contingent").GetDecimal()));
        (await Write(owner, $"{root}/{package}/deliveries", new PrivateDeliveryWrite(f.Sessions[2],3), false)).EnsureSuccessStatusCode(); Assert.Contains(await PrivateRows(owner, f.Coach), r => r.GetProperty("agreementId").GetGuid() == next && r.GetProperty("earned").GetDecimal() == 75);
        // Credit reduces service value. This remains an assessment, not a hidden negative payroll adjustment.
        (await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{f.Document}/movements", new { kind = "CreditNote", amount = 400m, collectionId = (Guid?)null, reason = "تخفيض اصطناعي" })).EnsureSuccessStatusCode();
        Assert.Equal(220, (await PrivateRows(owner, f.Coach)).Sum(r => r.GetProperty("earned").GetDecimal()));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.PayrollAdjustments.AnyAsync(a => a.EmployeeId == f.Employee)); Assert.False(await db.PayrollSettlements.AnyAsync(a => a.EmployeeId == f.Employee)); Assert.Equal(3000, await db.Employees.Where(e => e.Id == f.Employee).Select(e => e.MonthlySalary).SingleAsync());
        var unit = await db.PrivateServiceDeliveries.FirstAsync(d => d.PackageId == package); unit.Ordinal = 7; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateServiceDeliveries\" WHERE \"PackageId\"={package}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"TrainingSessions\" SET \"StartTime\"='02:00',\"EndTime\"='03:00' WHERE \"Id\"={f.Sessions[0]}"))).SqlState);
    }

    [Fact]
    public async Task V3_private_requires_owner_actual_assignment_past_service_and_distinct_ordinal_and_protects_mobile_privacy()
    {
        var f = await PrivateFixture(); using var owner = f.Owner; var root = PrivateRoot(f.Coach); var adminFixture = await CreateAdmin(); using var admin = adminFixture.Admin; using var grantOwner = adminFixture.Owner;
        (await Write(grantOwner, $"/api/v1/manage/staff/{adminFixture.Member}/access", Access(f.Branch,true,true) with { SensitivePermissions = [AcademyPermissions.CoachAgreementManage, AcademyPermissions.PayrollPay] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, root + "/packages", new PrivatePackageWrite(f.Document,8),false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(root + "/packages", new PrivatePackageWrite(f.Document,8))).StatusCode);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(root)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, root + "/packages", new PrivatePackageWrite(f.Document,8),false)).StatusCode);
        var bound = await Write(owner, root + "/packages", new PrivatePackageWrite(f.Document,8),false); bound.EnsureSuccessStatusCode(); var package = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[3],1),false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[0],9),false)).StatusCode);
        (await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[0],1),false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,$"/api/v1/attendance/sessions/{f.Sessions[0]}/status",new { status="Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[1],1),false)).StatusCode);
        var other = await AgreementFixture(); using var otherOwner = other.Owner;
        Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,$"{PrivateRoot(other.Coach)}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[1],2),false)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group=await db.TrainingSessions.Where(s=>s.Id==f.Sessions[1]).Select(s=>s.TrainingGroupId).SingleAsync();
            db.PlayerAttendances.Add(new() { Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,TrainingSessionId=f.Sessions[1],TrainingGroupId=group,SportEnrollmentId=f.Enrollment,Status=AttendanceStatus.Absent,RecordedByUserId=f.Actor }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(f.Sessions[1],2),false)).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(root)).StatusCode);
        var own = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/private-services"); var row = Assert.Single(own.GetProperty("items").EnumerateArray());
        Assert.False(row.TryGetProperty("player",out _)); Assert.False(row.TryGetProperty("documentId",out _)); Assert.False(row.TryGetProperty("serviceNet",out _)); Assert.Equal(0,row.GetProperty("earned").GetDecimal()); Assert.Equal(100,row.GetProperty("contingent").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/staff/payroll/private-services")).StatusCode);
        // A later truthful attendance correction is allowed, but cannot silently
        // leave a conflicting financial assessment looking ready for posting.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group=await db.TrainingSessions.Where(s=>s.Id==f.Sessions[0]).Select(s=>s.TrainingGroupId).SingleAsync();
            db.PlayerAttendances.Add(new() { Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,TrainingSessionId=f.Sessions[0],TrainingGroupId=group,SportEnrollmentId=f.Enrollment,Status=AttendanceStatus.Absent,RecordedByUserId=f.Actor }); await db.SaveChangesAsync();
        }
        var disputed=Assert.Single(await PrivateRows(owner,f.Coach)); Assert.True(disputed.GetProperty("requiresReview").GetBoolean()); Assert.Equal(JsonValueKind.Null,disputed.GetProperty("earned").ValueKind); Assert.Equal(JsonValueKind.Null,disputed.GetProperty("contingent").ValueKind);
        var disputedOwn=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/private-services"); Assert.True(disputedOwn.GetProperty("items")[0].GetProperty("requiresReview").GetBoolean());
    }

    [Fact]
    public async Task V3_private_demo_seed_is_repeatable_isolated_and_does_not_reset_payroll_or_production()
    {
        await using var demo = factory.WithWebHostBuilder(b => b.UseSetting("Demo:SeedProfile","Football")); using var c = demo.CreateClient(); await using var scope = demo.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        await PrivateServiceDemoSeed.Seed(db,env,options); await PrivateServiceDemoSeed.Seed(db,env,options);
        Assert.Single(await db.PrivateServicePackages.Where(p=>p.Id==PrivateServiceDemoSeed.Id("package")).ToArrayAsync()); Assert.Equal(2,await db.PrivateServiceDeliveries.CountAsync(d=>d.PackageId==PrivateServiceDemoSeed.Id("package")));
        (await Write(c,"/api/v1/auth/login",new { email="owner.football@example.test", password="Demo-Only-123!" },false)).EnsureSuccessStatusCode();
        var assessed = await PrivateRows(c,FootballDemoSeed.Id("member/coach1")); var fixture = assessed.Where(a=>a.GetProperty("packageId").GetGuid()==PrivateServiceDemoSeed.Id("package")).ToArray(); Assert.Equal(100,fixture.Sum(x=>x.GetProperty("earned").GetDecimal())); Assert.Equal(100,fixture.Sum(x=>x.GetProperty("contingent").GetDecimal()));
        var old = env.EnvironmentName; try { env.EnvironmentName="Production"; await Assert.ThrowsAsync<InvalidOperationException>(()=>PrivateServiceDemoSeed.Seed(db,env,options)); } finally { env.EnvironmentName=old; }
        Assert.False(await db.PrivateServicePackages.AnyAsync(p=>p.Id==PrivateServiceDemoSeed.Id("package")&&p.AcademyId==DemoSeed.FutureAcademyId));
    }
}
