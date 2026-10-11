using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class BrandingMatchesTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private async Task<HttpClient> Staff(string email = DemoSeed.OwnerEmail) { var c = factory.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await Send(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).StatusCode); return c; }
    private static async Task<HttpResponseMessage> Send(HttpClient c, string path, object body, bool put = false, string? key = null) { var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); if(key != null) r.Headers.Add("Idempotency-Key",key); return await c.SendAsync(r); }
    private async Task<HttpClient> Mobile(AcademyRole role) {
        var c = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var m = await db.AcademyMemberships.Include(x=>x.User).FirstAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Role==role&&x.IsActive&&(role!=AcademyRole.Guardian||x.User.PhoneNumber==DemoSeed.GuardianPhone));
        var token=MobileAuthenticationHandler.NewToken(); var now=DateTimeOffset.UtcNow;
        db.MobileSessions.Add(new Academy.Infrastructure.Identity.MobileSession { Id=Guid.NewGuid(),UserId=m.UserId,MembershipId=m.Id,AccessTokenHash=MobileAuthenticationHandler.Hash(token),RefreshTokenHash=MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()),SecurityStamp=m.User.SecurityStamp??"",DeviceName="Branding match test",CreatedAtUtc=now,LastUsedAtUtc=now,AccessExpiresAtUtc=now.AddMinutes(10),ExpiresAtUtc=now.AddHours(1) });await db.SaveChangesAsync();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return c;
    }
    [Fact] public async Task Branding_requires_separate_platform_session_not_owner_or_admin_role() {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail); using var guardian = await Mobile(AcademyRole.Guardian);
        var body = new { displayName = "هوية اختبار مستقلة", primaryColor = "#D71935", secondaryColor = "#171923" };
        foreach (var c in new[] { owner, admin, guardian }) {
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/platform/academies")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(c, $"/api/v1/platform/academies/{DemoSeed.FutureAcademyId}/branding", body, true)).StatusCode);
        }
        using var platform = factory.CreateClient();
        async Task<HttpResponseMessage> PlatformWrite(string path, object data, bool put = false) {
            var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
            var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(data) };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await platform.SendAsync(request);
        }
        (await PlatformWrite("/api/v1/platform/auth/login", new { email = Academy.Api.Platform.PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await PlatformWrite($"/api/v1/platform/academies/{DemoSeed.FutureAcademyId}/branding", body, true)).StatusCode);
        var ours = await owner.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience");
        Assert.NotEqual(body.displayName, ours.GetProperty("displayName").GetString());
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(body.displayName, (await other.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience")).GetProperty("displayName").GetString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.True(await db.BrandingAudits.AnyAsync(x => x.AcademyId == DemoSeed.FutureAcademyId && x.ActorUserId == Academy.Api.Platform.PlatformIdentity.DemoUserId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PlatformWrite($"/api/v1/platform/academies/{DemoSeed.FutureAcademyId}/branding", new { body.displayName, primaryColor = "url(javascript:alert(1))", body.secondaryColor }, true)).StatusCode);
    }
    [Fact] public async Task Disabled_coach_attendance_is_enforced_server_side_but_evaluation_and_sessions_remain() {
        using var owner=await Staff(); using var admin=await Staff(DemoSeed.AdminEmail);using var coach=await Mobile(AcademyRole.Coach);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/academy/experience",new{coachAttendanceEnabled=false},true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(owner,"/api/v1/manage/academy/experience",new{coachAttendanceEnabled=false},true)).StatusCode);
        try { Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,$"/api/v1/attendance/sessions/{Guid.NewGuid()}/players",new{items=Array.Empty<object>()},true)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await coach.GetAsync("/api/v1/evaluations/options")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await coach.GetAsync("/api/v1/attendance/sessions")).StatusCode); }
        finally { await Send(owner,"/api/v1/manage/academy/experience",new{coachAttendanceEnabled=true},true); }
    }
    [Fact] public async Task Transfer_settings_reach_guardian_and_cannot_be_changed_by_ungranted_admin() {
        using var owner=await Staff();using var admin=await Staff(DemoSeed.AdminEmail);using var guardian=await Mobile(AcademyRole.Guardian);var body=new{instaPayAddress="synthetic-test-only@instapay",walletNumber="00000000000",recipientName="حساب اختبار غير صالح للتحويل",isActive=true};
        Assert.Equal(HttpStatusCode.NoContent,(await Send(owner,"/api/v1/payment-settings",body,true)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/payment-settings",body,true)).StatusCode);var g=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/payment-settings");Assert.True(g.GetProperty("configured").GetBoolean());Assert.Equal(body.instaPayAddress,g.GetProperty("instaPayAddress").GetString());using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.NotEqual(body.instaPayAddress,(await other.GetFromJsonAsync<JsonElement>("/api/v1/payment-settings")).GetProperty("instaPayAddress").GetString());
    }
    private async Task<(Guid Tournament, Guid Team, Guid Enrollment, Guid Player)> Fixtures(HttpClient owner) {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var e=await db.SportEnrollments.Include(x=>x.TrainingGroup).FirstAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.PlayerId==Slice2DemoSeed.OmarPlayerId&&x.SportId==Slice2DemoSeed.FootballId);var coach=await db.AcademyMemberships.Where(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Role==AcademyRole.Coach&&x.IsActive).Select(x=>x.Id).FirstAsync();
        var tr=await Send(owner,"/api/v1/manage/matches/tournaments",new{name="بطولة اختبار "+Guid.NewGuid(),sportId=e.SportId,fee=250,currency="EGP",isActive=true});Assert.Equal(HttpStatusCode.OK,tr.StatusCode);var tid=(await tr.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var team=await Send(owner,"/api/v1/manage/matches/teams",new{name="منتخب اختبار "+Guid.NewGuid(),sportId=e.SportId,branchId=e.BranchId,ageCategoryId=e.TrainingGroup.AgeCategoryId,coachMembershipId=coach,enrollmentIds=new[]{e.Id},isActive=true});Assert.Equal(HttpStatusCode.OK,team.StatusCode);return(tid,(await team.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),e.Id,e.PlayerId);
    }
    [Fact] public async Task Published_match_is_child_scoped_notifies_once_and_keeps_training_group() {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var before=await db.SportEnrollments.Where(x=>x.Id==f.Enrollment).Select(x=>x.TrainingGroupId).SingleAsync();
        var body=new MatchWrite(f.Tournament,f.Team,"منافس اختبار",new DateOnly(2026,10,1),new TimeOnly(17,0),"ملعب اختبار",null,null,null,[],true,true);var r=await Send(owner,"/api/v1/manage/matches/",body);Assert.Equal(HttpStatusCode.OK,r.StatusCode);var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var notices=await db.AcademyNotices.CountAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId);Assert.True(notices>0);Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/matches/{id}",body,true)).StatusCode);Assert.Equal(notices,await db.AcademyNotices.CountAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId));Assert.Equal(before,await db.SportEnrollments.Where(x=>x.Id==f.Enrollment).Select(x=>x.TrainingGroupId).SingleAsync());var mine=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/my-matches");Assert.Contains(mine.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);var foreign=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={Slice2DemoSeed.AcademyBPlayerId}");Assert.Equal(0,foreign.GetArrayLength());using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await Send(other,$"/api/v1/manage/matches/{id}",body,true)).StatusCode);
    }
    [Fact] public async Task Team_rejects_foreign_enrollment_and_wrong_age_and_draft_never_leaks() {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var team=await db.MatchTeams.SingleAsync(x=>x.Id==f.Team);var foreign=await db.SportEnrollments.Where(x=>x.AcademyId==DemoSeed.FutureAcademyId).Select(x=>x.Id).FirstAsync();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/matches/teams/{f.Team}",new TeamWrite(team.Name,team.SportId,team.BranchId,team.AgeCategoryId,team.CoachMembershipId,[foreign],true),true)).StatusCode);var r=await Send(owner,"/api/v1/manage/matches/",new MatchWrite(f.Tournament,f.Team,"مسودة سرية",new DateOnly(2026,10,3),new TimeOnly(17,0),"ملعب",null,null,null,[],false,true));Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.DoesNotContain("مسودة سرية",await guardian.GetStringAsync("/api/v1/my-matches"));
    }
    [Fact] public async Task Tournament_payment_is_idempotent_and_confirmation_does_not_extend_training() {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var actor=await db.Users.Where(x=>x.PhoneNumber==DemoSeed.GuardianPhone).Select(x=>x.Id).SingleAsync();var periods=await db.SubscriptionPeriods.CountAsync(x=>x.SportEnrollmentId==f.Enrollment);var key=Guid.NewGuid().ToString();var body=new TournamentPaymentWrite(f.Tournament,f.Enrollment);var first=await Send(guardian,"/api/v1/guardian/tournament-payments/",body,key:key);Assert.Equal(HttpStatusCode.Created,first.StatusCode);var id=(await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.OK,(await Send(guardian,"/api/v1/guardian/tournament-payments/",body,key:key)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Send(guardian,"/api/v1/guardian/tournament-payments/",body with{TournamentId=Guid.NewGuid()},key:key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,$"/api/v1/manage/tournament-payments/{id}/review",new{accept=true})).StatusCode);var asset=new PrivateAsset{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UploadedByUserId=actor,StorageName="synthetic-test-only",ContentType="image/png",Length=20,Purpose="Proof"};db.PrivateAssets.Add(asset);await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.OK,(await Send(guardian,$"/api/v1/guardian/tournament-payments/{id}/proof",new{assetId=asset.Id})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/tournament-payments/{id}/review",new{accept=true})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/tournament-payments/{id}/review",new{accept=true})).StatusCode);Assert.Equal(periods,await db.SubscriptionPeriods.CountAsync(x=>x.SportEnrollmentId==f.Enrollment));Assert.Equal(1,await db.TournamentPayments.CountAsync(x=>x.Id==id&&x.Status=="Confirmed"&&x.ReceiptNumber!=null));var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28&collectionType=Tournament");Assert.Contains(report.GetProperty("items").EnumerateArray(),x=>x.GetProperty("collectionId").GetGuid()==id);Assert.Contains("رسم بطولة",await owner.GetStringAsync("/api/v1/reports/financial/export?from=2026-09-01&to=2026-09-28&collectionType=Tournament"));using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/tournament-payments/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/assets/{asset.Id}")).StatusCode);
    }
    [Fact] public async Task Guardian_cannot_create_matches_and_foreign_payment_reference_is_not_disclosed() {
        using var guardian=await Mobile(AcademyRole.Guardian);Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync("/api/v1/manage/matches/options")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/tournament-payments/{Guid.NewGuid()}")).StatusCode);
    }
    [Fact]
    public async Task Uploaded_logo_is_private_tenant_scoped_and_rejects_active_content()
    {
        using var owner = await Staff();
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var platform = factory.CreateClient();
        var loginToken = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/auth/login") { Content = JsonContent.Create(new { email = Academy.Api.Platform.PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" }) };
        login.Headers.Add("X-CSRF-TOKEN", loginToken.GetProperty("token").GetString());
        (await platform.SendAsync(login)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var actor = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        db.PlatformBrandOperators.Add(new PlatformBrandOperator { UserId = actor });
        await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Upload(byte[] bytes, string type)
        {
            var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(type);
            form.Add(file, "file", "synthetic-logo.png");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/academies/{DemoSeed.FutureAcademyId}/logo") { Content = form };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            return await platform.SendAsync(request);
        }
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), "image/svg+xml")).StatusCode);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aapcAAAAASUVORK5CYII=");
            Assert.Equal(HttpStatusCode.NoContent, (await Upload(png, "image/png")).StatusCode);
            var logo = await other.GetAsync("/api/v1/academy/branding/logo");
            Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
            Assert.Equal(png, await logo.Content.ReadAsByteArrayAsync());
            Assert.True(logo.Headers.CacheControl!.Private);
            Assert.True(logo.Headers.CacheControl.NoStore);
            Assert.Equal("nosniff", logo.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/v1/academy/branding/logo")).StatusCode);
        }
        finally { await db.PlatformBrandOperators.Where(x => x.UserId == actor).ExecuteDeleteAsync(); }
    }
    [Fact]
    public async Task Default_daily_nutrition_uses_demo_reference_date_and_oversized_fee_is_validation_not_server_error()
    {
        using var guardian = await Mobile(AcademyRole.Guardian);
        // LegacyRegression does not identify sports by the Football profile marker.
        // Set that explicit fixture precondition temporarily, never in the review DB.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sport = await db.Sports.SingleAsync(x => x.Id == Slice2DemoSeed.FootballId);
        var previousMarker = sport.EnglishName;
        sport.EnglishName = "Football";
        await db.SaveChangesAsync();
        try
        {
            var nutrition = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{Slice2DemoSeed.OmarPlayerId}/nutrition/recommendations");
            Assert.Equal("2026-09-28", nutrition.GetProperty("date").GetString());
        }
        finally { sport.EnglishName = previousMarker; await db.SaveChangesAsync(); }
        using var owner = await Staff();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, "/api/v1/manage/matches/tournaments", new TournamentWrite("حد قاعدة البيانات", Slice2DemoSeed.FootballId, 10000000000000000m, "EGP", true))).StatusCode);
    }
    private sealed class Factory(string connection):WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",connection);b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:FixedOtpEnabled","true");b.UseSetting("Demo:FixedOtp","246810");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");} }
}
