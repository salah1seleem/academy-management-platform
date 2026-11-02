using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Day_pass_demo_seed_is_opt_in_idempotent_and_keeps_existing_decisions()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await FacilityDemoSeed.Seed(db);
        await DayPassDemoSeed.Seed(db, factory.VisitClock);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(factory.VisitClock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        var id = DayPassDemoSeed.BookingId(today.AddDays(1));
        var before = (await db.SubscriptionPlans.CountAsync(p => p.Id == DayPassDemoSeed.PlanId), await db.ResourceBookings.CountAsync(b => b.Id == id));
        await DayPassDemoSeed.Seed(db, factory.VisitClock);
        Assert.Equal(before, (await db.SubscriptionPlans.CountAsync(p => p.Id == DayPassDemoSeed.PlanId), await db.ResourceBookings.CountAsync(b => b.Id == id)));
        Assert.Equal(1, before.Item1);
    }

    [Fact]
    public async Task Day_pass_requires_linked_child_proof_staff_review_and_identity_before_one_admission()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services);
        var f = await Fixture();
        using var owner = await Staff();
        using var guardian = await Guardian();
        var resource = await FacilityFixture(owner, branch: f.A);
        var booked = await BookFacility(owner, FacilityBody(resource, 2), Guid.NewGuid().ToString());
        booked.EnsureSuccessStatusCode();
        var booking = (await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var planResponse = await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "زيارة يومية اصطناعية " + Guid.NewGuid().ToString("N")[..8], 125, type: SubscriptionPlanType.Duration) with { ServiceKind = AcademyServiceKind.DayPass, DurationDays = 1 });
        planResponse.EnsureSuccessStatusCode();
        var plan = (await planResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var player = await PlayerFor(f.Enrollment);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var transfer = await db.AcademyTransferSettings.SingleOrDefaultAsync(s => s.AcademyId == DemoSeed.NogoomAcademyId);
        if (transfer is null) { transfer = new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId }; db.AcademyTransferSettings.Add(transfer); }
        transfer.IsActive = true; transfer.InstaPayAddress = "demo-only@example.test";
        await db.SaveChangesAsync();

        var choice = new DayPassChoice(player, booking, plan);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(owner, "/api/v1/guardian/day-pass/quote", choice)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(guardian, "/api/v1/guardian/day-pass/quote", choice with { PlayerId = Guid.NewGuid() })).StatusCode);
        var quoteResponse = await Write(guardian, "/api/v1/guardian/day-pass/quote", choice);
        quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("125.00", quote.GetProperty("amount").GetString());
        var order = new DayPassOrder(player, booking, plan, quote.GetProperty("quoteHash").GetString()!);
        var key = Guid.NewGuid().ToString();
        var ordered = await VisitCommand(guardian, "/api/v1/guardian/day-pass/orders", order, key);
        ordered.EnsureSuccessStatusCode();
        var ticket = (await ordered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var replay = await VisitCommand(guardian, "/api/v1/guardian/day-pass/orders", order, key);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await VisitCommand(guardian, "/api/v1/guardian/day-pass/orders", order with { PlayerId = Guid.NewGuid() }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await VisitCommand(guardian, "/api/v1/guardian/day-pass/orders", order, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await VisitCommand(owner, $"/api/v1/facilities/bookings/{booking}/entry", new EntryWrite(null, IdentityConfirmed: true, DayPassTicketId: ticket))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/facilities/day-pass/review")).StatusCode);

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]);
        file.Headers.ContentType = new("image/png"); upload.Add(file, "file", "day-pass-proof.png"); upload.Add(new StringContent("Proof"), "purpose");
        var csrf = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assets/upload") { Content = upload };
        uploadRequest.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var uploaded = await guardian.SendAsync(uploadRequest); uploaded.EnsureSuccessStatusCode();
        var asset = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(guardian, $"/api/v1/guardian/day-pass/orders/{ticket}/proof", new DayPassProof(asset))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/facilities/bookings/{booking}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
        (await Write(owner, $"/api/v1/facilities/day-pass/{ticket}/review", new DayPassReview(true, null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/facilities/day-pass/{ticket}/review", new DayPassReview(false, "لا"))).StatusCode);
        Assert.Equal(DayPassTicketStatus.Confirmed, (await db.DayPassTickets.AsNoTracking().SingleAsync(x => x.Id == ticket)).Status);
        Assert.False(await db.SubscriptionPeriods.AnyAsync(x => x.SubscriptionPlanId == plan));
        var financial = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true&collectionType=DayPass");
        Assert.Contains(financial.GetProperty("items").EnumerateArray(), x => x.GetProperty("collectionId").GetGuid() == ticket && x.GetProperty("collectionType").GetString() == "DayPass");
        var export = await owner.GetAsync("/api/v1/reports/financial/export?allDates=true&collectionType=DayPass");
        export.EnsureSuccessStatusCode();
        Assert.Contains("زيارة يومية", await export.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/reports/financial?allDates=true&collectionType=DayPass")).StatusCode);

        factory.VisitClock.Now = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(FacilityDay.ToDateTime(new(17, 5)), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")));
        (await Write(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" })).EnsureSuccessStatusCode();
        var entry = await VisitCommand(owner, $"/api/v1/facilities/bookings/{booking}/entry", new EntryWrite(null, IdentityConfirmed: true, DayPassTicketId: ticket));
        entry.EnsureSuccessStatusCode();
        Assert.Equal("PaidDayPass", (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("basis").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await VisitCommand(owner, $"/api/v1/facilities/bookings/{booking}/entry", new EntryWrite(null, IdentityConfirmed: true, DayPassTicketId: ticket))).StatusCode);
        Assert.Single(await db.AccessEntitlements.Where(x => x.DayPassTicketId == ticket).ToArrayAsync());
        Assert.False(await db.PlayerAttendances.AnyAsync(a => a.TrainingSession.TrainingGroupId == f.Group && a.TrainingSession.SessionDate == FacilityDay));
    }
}
