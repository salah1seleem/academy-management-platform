using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class ExternalRenewalTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;
    private Guid? legacyEnrollment;
    private readonly Dictionary<Guid, RenewalRequestStatus> suspended = [];

    private InternalGatewayRouteScope route = null!;
    public async Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); route = await InternalGatewayRouteScope.Begin(factory.Services); }
    public async Task DisposeAsync()
    {
        if (legacyEnrollment is Guid enrollment)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            foreach (var row in await OpenLegacyPayments(db, enrollment))
            {
                row.Renewal.Status = RenewalRequestStatus.Cancelled;
                row.Payment.Status = PaymentRequestStatus.Cancelled;
            }
            foreach (var (id, status) in suspended)
                (await db.RenewalRequests.SingleAsync(row => row.Id == id)).Status = status;
            await db.SaveChangesAsync();
        }
        await route.DisposeAsync();
        await factory.DisposeAsync();
    }

    private async Task PrepareLegacyPayment()
    {
        if (legacyEnrollment.HasValue) return;
        legacyEnrollment = await BeneficiaryEnrollment();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var row in await OpenLegacyPayments(db, legacyEnrollment.Value))
        {
            suspended.Add(row.Renewal.Id, row.Renewal.Status);
            row.Renewal.Status = RenewalRequestStatus.Cancelled;
        }
        await db.SaveChangesAsync();
    }

    private static async Task<List<(RenewalRequest Renewal, PaymentRequest Payment)>> OpenLegacyPayments(FoundationDbContext db, Guid enrollment)
    {
        var rows = await (from renewal in db.RenewalRequests
                          join payment in db.PaymentRequests on renewal.PaymentRequestId equals (Guid?)payment.Id
                          where renewal.AcademyId == DemoSeed.NogoomAcademyId && payment.AcademyId == renewal.AcademyId &&
                                renewal.SportEnrollmentId == enrollment &&
                                (renewal.Status == RenewalRequestStatus.PendingPayment || renewal.Status == RenewalRequestStatus.PaymentInProgress) &&
                                (payment.Status == PaymentRequestStatus.Pending || payment.Status == PaymentRequestStatus.Created)
                          select new { Renewal = renewal, Payment = payment }).ToListAsync();
        return rows.Select(row => (row.Renewal, row.Payment)).ToList();
    }

    [Fact]
    public async Task Payer_can_resolve_valid_same_academy_reference()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, Slice3DemoSeed.ExternalRenewalCode);
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<Resolved>();
        Assert.Equal("عمر أحمد حسن", value!.PlayerDisplayName);
        Assert.Equal("كرة القدم", value.Sport);
        Assert.NotEmpty(value.Plans);
    }

    [Fact]
    public async Task Resolution_exposes_only_minimal_beneficiary_data()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, Slice3DemoSeed.ExternalRenewalCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray();
        Assert.Equal(new[] { "academyName", "plans", "playerDisplayName", "sport" }, names);
        var body = json.RootElement.GetRawText().ToLowerInvariant();
        foreach (var forbidden in new[] { "playerid", "enrollmentid", "dateofbirth", "phone", "guardian", "medical", "attendance", "evaluation", "gallery" }) Assert.DoesNotContain(forbidden, body);
    }

    [Fact]
    public async Task Invalid_reference_returns_safe_failure()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, "RNW-NOT-A-REAL-CODE");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("player", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    [Fact]
    public async Task Academy_A_reference_cannot_be_used_from_Academy_B()
    {
        using var client = await GuardianClient("+201000000003");
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(client, Slice3DemoSeed.ExternalRenewalCode)).StatusCode);
    }

    [Fact]
    public async Task Owner_renewal_code_rotation_has_one_winner_and_only_the_issuance_response_reveals_the_code()
    {
        var first = await AddSearchEnrollment("لاعب اصطناعي لاختبار كود المالك", Slice2DemoSeed.SwimmingGroupId);
        var other = await AddSearchEnrollment("لاعب اصطناعي لاختبار كود المالك", Slice2DemoSeed.FootballGroupId);
        using var owner = await StaffClient(DemoSeed.OwnerEmail);
        using var otherTab = await StaffClient(DemoSeed.OwnerEmail);
        using var payer = await GuardianClient();
        var path = $"/api/v1/subscriptions/beneficiary-references/{first.Enrollment}";
        var profile = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{first.Player}");
        Assert.True(Assert.Single(profile.GetProperty("enrollments").EnumerateArray()).GetProperty("canIssueRenewalCode").GetBoolean());
        var initial = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("none", initial.GetProperty("revision").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("expiresAtUtc").ValueKind);
        var issued = await Rotate(owner, path, "none");
        issued.EnsureSuccessStatusCode();
        Assert.True(issued.Headers.CacheControl?.NoStore);
        var old = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var oldCode = old.GetProperty("reference").GetString()!;
        var revision = old.GetProperty("revision").GetString()!;
        Assert.StartsWith("RNW-", oldCode);
        Assert.Equal(HttpStatusCode.OK, (await Resolve(payer, oldCode)).StatusCode);
        var read = await otherTab.GetStringAsync(path);
        Assert.DoesNotContain(oldCode, read);
        Assert.Equal(revision, JsonDocument.Parse(read).RootElement.GetProperty("revision").GetString());

        var rotations = await Task.WhenAll(Rotate(owner, path, revision), Rotate(otherTab, path, revision));
        var newCode = (await Assert.Single(rotations, response => response.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetString()!;
        Assert.Single(rotations, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(payer, oldCode)).StatusCode);
        var resolved = (await (await Resolve(payer, newCode)).Content.ReadFromJsonAsync<Resolved>())!;
        Assert.Equal("السباحة", resolved.Sport);
        Assert.Equal(HttpStatusCode.NotFound, (await payer.GetAsync($"/api/v1/guardian/children/{first.Player}")).StatusCode);
        Assert.Equal(1, await Count<BeneficiaryRenewalReference>(x => x.SportEnrollmentId == first.Enrollment && x.RevokedAtUtc == null));
        Assert.Equal(1, await Count<BeneficiaryRenewalReference>(x => x.SportEnrollmentId == first.Enrollment && x.CodeHash == BeneficiaryRenewalCodes.Hash(newCode)));
        Assert.Equal(0, await Count<BeneficiaryRenewalReference>(x => x.SportEnrollmentId == other.Enrollment));
        Assert.Equal(0, await Count<RenewalRequest>(x => x.SportEnrollmentId == first.Enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.SportEnrollmentId == first.Enrollment));
        Assert.Equal(0, await Count<GuardianPlayerLink>(x => x.PlayerId == first.Player));
    }

    [Fact]
    public async Task Owner_renewal_code_stops_when_enrollment_or_player_is_ineligible_and_stays_tenant_scoped()
    {
        var fixture = await AddSearchEnrollment("لاعب اصطناعي لاختبار إيقاف الكود", Slice2DemoSeed.FootballGroupId, createCode: true);
        using var owner = await StaffClient(DemoSeed.OwnerEmail);
        using var foreign = await StaffClient(DemoSeed.FutureOwnerEmail);
        using var payer = await GuardianClient();
        var path = $"/api/v1/subscriptions/beneficiary-references/{fixture.Enrollment}";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Rotate(foreign, path, "none")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await payer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Rotate(payer, path, "none")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Resolve(payer, fixture.Code!)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollment = await db.SportEnrollments.Include(e => e.Player).SingleAsync(e => e.Id == fixture.Enrollment);
        enrollment.Status = EnrollmentStatus.Inactive;
        await db.SaveChangesAsync();
        await AssertUnavailable();
        enrollment.Status = EnrollmentStatus.Active;
        enrollment.Player.IsActive = false;
        await db.SaveChangesAsync();
        await AssertUnavailable();

        async Task AssertUnavailable()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Rotate(owner, path, "none")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Resolve(payer, fixture.Code!)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await StartSearchRenewal(payer, fixture.Code!, Guid.NewGuid().ToString())).StatusCode);
            var profile = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{fixture.Player}");
            Assert.False(Assert.Single(profile.GetProperty("enrollments").EnumerateArray()).GetProperty("canIssueRenewalCode").GetBoolean());
        }
    }

    [Fact]
    public async Task Search_groups_identical_visible_results_before_applying_the_result_limit()
    {
        using var client = await GuardianClient();
        using var otherPayer = await GuardianClient("+201000000002");
        using var otherAcademy = await GuardianClient("+201000000003");
        const string prefix = "اختبار هوية التجديد";
        const string duplicate = prefix + " 99";
        for (var i = 0; i < 9; i++)
            await AddSearchEnrollment($"{prefix} {i:00}", Slice2DemoSeed.FootballGroupId);
        await AddSearchEnrollment(duplicate, Slice2DemoSeed.FootballGroupId);
        await AddSearchEnrollment(duplicate, Slice2DemoSeed.FootballGroupId);

        var firstPage = await Search(client, prefix);
        Assert.Equal(10, firstPage.GetArrayLength());
        var last = firstPage[9];
        Assert.Equal(duplicate, last.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("reference").ValueKind);

        await AddSearchEnrollment(duplicate, Slice2DemoSeed.SwimmingGroupId);
        await AddSearchEnrollment(duplicate, Slice2DemoSeed.SwimmingGroupId, active: false);
        await AddSearchEnrollment(duplicate, Slice2DemoSeed.JuniorGroupId);
        var rows = (await Search(client, duplicate)).EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal(4, row.EnumerateObject().Count()));
        var city = Assert.Single(rows, row => row.GetProperty("branch").GetString() == "فرع مدينة نصر");
        Assert.Equal(JsonValueKind.Null, city.GetProperty("reference").ValueKind);
        var zagazig = Assert.Single(rows, row => row.GetProperty("branch").GetString() == "فرع الزقازيق");
        Assert.StartsWith("SR1.", zagazig.GetProperty("reference").GetString());
        var swimming = Assert.Single(rows, row => row.GetProperty("sport").GetString() == "السباحة");
        var reference = swimming.GetProperty("reference").GetString()!;
        Assert.StartsWith("SR1.", reference);
        var resolved = await Resolve(client, reference);
        resolved.EnsureSuccessStatusCode();
        var swimmingSelection = (await resolved.Content.ReadFromJsonAsync<Resolved>())!;
        Assert.Equal("فرع التجمع الخامس", swimmingSelection.Branch);
        var swimmingPlans = swimmingSelection.Plans;
        Assert.Contains(swimmingPlans, plan => plan.Id == Slice3DemoSeed.SwimmingMonthlyId);
        Assert.DoesNotContain(swimmingPlans, plan => plan.Id == Slice3DemoSeed.FootballMonthlyId);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(otherPayer, reference)).StatusCode);
        Assert.Empty((await Search(otherAcademy, duplicate)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(otherAcademy, reference)).StatusCode);
    }

    [Fact]
    public async Task Newly_identical_enrollments_invalidate_unsaved_search_selections_without_disabling_academy_codes()
    {
        using var client = await GuardianClient();
        const string name = "اختبار مرجع تجديد متغير";
        var selected = await AddSearchEnrollment(name, Slice2DemoSeed.FootballGroupId);
        var reference = Assert.Single((await Search(client, name)).EnumerateArray()).GetProperty("reference").GetString()!;
        (await Resolve(client, reference)).EnsureSuccessStatusCode();
        var originalKey = Guid.NewGuid().ToString();
        var original = await StartSearchRenewal(client, reference, originalKey);
        original.EnsureSuccessStatusCode();
        var savedPayment = (await original.Content.ReadFromJsonAsync<Created>())!.PaymentId;

        var sibling = await AddSearchEnrollment(name, Slice2DemoSeed.FootballGroupId, createCode: true);
        Assert.Equal(JsonValueKind.Null, Assert.Single((await Search(client, name)).EnumerateArray()).GetProperty("reference").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(client, reference)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StartSearchRenewal(client, reference, Guid.NewGuid().ToString())).StatusCode);
        var replay = await StartSearchRenewal(client, reference, originalKey);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(savedPayment, (await replay.Content.ReadFromJsonAsync<Created>())!.PaymentId);
        Assert.Equal(1, await Count<RenewalRequest>(x => x.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<RenewalRequest>(x => x.SportEnrollmentId == sibling.Enrollment));

        var codePayment = await StartSearchRenewal(client, sibling.Code!, Guid.NewGuid().ToString());
        codePayment.EnsureSuccessStatusCode();
        var paymentId = (await codePayment.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        await Simulate(client, paymentId, "Success");
        await Simulate(client, paymentId, "Success");
        Assert.Equal(1, await Count<PaymentCollection>(x => x.SportEnrollmentId == sibling.Enrollment));
        Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.SportEnrollmentId == sibling.Enrollment));
        Assert.Equal(1, await Count<Receipt>(x => x.SportEnrollmentId == sibling.Enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{sibling.Player}")).StatusCode);
        Assert.Equal(0, await Count<GuardianPlayerLink>(x => x.PlayerId == sibling.Player));
    }

    [Fact]
    public async Task Search_reference_expires_in_ten_minutes_and_cannot_create_payment_after_expiry()
    {
        using var client = await GuardianClient();
        const string name = "لاعب اختبار مدة صلاحية اختيار التسجيل";
        var selected = await AddSearchEnrollment(name, Slice2DemoSeed.FootballGroupId);
        var identity = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        await using var scope = factory.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Academy.RenewForAnother.v1").ToTimeLimitedDataProtector();

        var before = DateTimeOffset.UtcNow;
        var reference = Assert.Single((await Search(client, name)).EnumerateArray()).GetProperty("reference").GetString()!;
        var after = DateTimeOffset.UtcNow;
        Assert.StartsWith("SR1.", reference);
        var payload = protector.Unprotect(reference[4..], out var expiry);
        Assert.InRange(expiry, before.AddMinutes(10), after.AddMinutes(10));
        var decoded = JsonSerializer.Deserialize<JsonElement>(payload);
        Assert.Equal(identity.GetProperty("userId").GetGuid(), decoded.GetProperty("Payer").GetGuid());
        Assert.Equal(selected.Enrollment, decoded.GetProperty("Enrollment").GetGuid());
        Assert.Equal(DemoSeed.NogoomAcademyId, decoded.GetProperty("Academy").GetGuid());
        (await Resolve(client, reference)).EnsureSuccessStatusCode();

        var expired = "SR1." + protector.Protect(payload, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(client, expired)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StartSearchRenewal(client, expired, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(0, await Count<RenewalRequest>(row => row.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(row => row.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<SubscriptionPeriod>(row => row.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<Receipt>(row => row.SportEnrollmentId == selected.Enrollment));
        Assert.Equal(0, await Count<GuardianPlayerLink>(row => row.PlayerId == selected.Player));
    }

    [Fact]
    public async Task External_selection_reopens_only_the_payers_current_request_after_a_new_session()
    {
        const string name = "لاعب اختبار استعادة طلب التجديد";
        var enrollment = await AddSearchEnrollment(name, Slice2DemoSeed.FootballGroupId, createCode: true);
        using var payer = await GuardianClient();
        using var reopened = await GuardianClient();
        using var other = await GuardianClient("+201000000002");
        using var foreign = await GuardianClient("+201000000003");
        var first = Assert.Single((await Search(payer, name)).EnumerateArray()).GetProperty("reference").GetString()!;
        var start = await StartSearchRenewal(payer, first, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var id = (await start.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        var fresh = Assert.Single((await Search(reopened, name)).EnumerateArray());
        Assert.Equal(4, fresh.EnumerateObject().Count());
        var freshReference = fresh.GetProperty("reference").GetString()!;
        Assert.NotEqual(first, freshReference);

        foreach (var reference in new[] { freshReference, enrollment.Code! })
        {
            using var resolved = await Resolve(reopened, reference);
            resolved.EnsureSuccessStatusCode();
            Assert.True(resolved.Headers.CacheControl?.NoStore);
            Assert.True(resolved.Headers.CacheControl?.Private);
            var data = await resolved.Content.ReadFromJsonAsync<JsonElement>();
            var open = data.GetProperty("openRenewal");
            Assert.Equal("Payment", open.GetProperty("kind").GetString());
            Assert.Equal(id, open.GetProperty("paymentId").GetGuid());
        }
        var otherReference = Assert.Single((await Search(other, name)).EnumerateArray()).GetProperty("reference").GetString()!;
        foreach (var reference in new[] { otherReference, enrollment.Code! })
        {
            using var response = await Resolve(other, reference);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            var open = data.GetProperty("openRenewal");
            Assert.Equal("Unavailable", open.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, open.GetProperty("paymentId").ValueKind);
            Assert.Equal(JsonValueKind.Null, open.GetProperty("familyRequestId").ValueKind);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(other, freshReference)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(foreign, freshReference)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(foreign, enrollment.Code!)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/guardian/subscriptions/payments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StartSearchRenewal(reopened, freshReference, Guid.NewGuid().ToString())).StatusCode);
        await Simulate(reopened, id, "Cancelled");
        using var cancelled = await Resolve(reopened, freshReference);
        var available = await cancelled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(available.TryGetProperty("openRenewal", out _));
        Assert.NotEmpty(available.GetProperty("plans").EnumerateArray());
        Assert.Equal(1, await Count<RenewalRequest>(row => row.SportEnrollmentId == enrollment.Enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(row => row.SportEnrollmentId == enrollment.Enrollment));
        Assert.Equal(0, await Count<SubscriptionPeriod>(row => row.SportEnrollmentId == enrollment.Enrollment));
        Assert.Equal(0, await Count<Receipt>(row => row.SportEnrollmentId == enrollment.Enrollment));
        Assert.Equal(0, await Count<GuardianPlayerLink>(row => row.PlayerId == enrollment.Player));
    }

    [Fact]
    public async Task Payer_cannot_access_beneficiary_player_after_payment()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}")).StatusCode);
    }

    [Fact]
    public async Task Payment_success_renews_beneficiary_enrollment()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        var enrollment = await BeneficiaryEnrollment();
        Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.SportEnrollmentId == enrollment && x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Payer_can_access_receipt_for_external_renewal()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        var state = await client.GetFromJsonAsync<PaymentState>($"/api/v1/guardian/subscriptions/payments/{payment}");
        var receipt = await client.GetFromJsonAsync<ReceiptView>($"/api/v1/guardian/subscriptions/receipts/{state!.ReceiptId}");
        Assert.Equal("عمر أحمد حسن", receipt!.PlayerNameSnapshot);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var paymentAmount = await db.PaymentRequests.Where(x => x.Id == payment).Select(x => x.Amount).SingleAsync();
            Assert.Equal(paymentAmount, receipt.Amount);
        }
        Assert.Equal("EGP", receipt.Currency);
        Assert.False(string.IsNullOrWhiteSpace(receipt.ProviderReference));
        var feed = await client.GetFromJsonAsync<JsonElement>("/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true");
        var row = Assert.Single(feed.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == state.ReceiptId);
        Assert.False(row.GetProperty("linkedChild").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("playerCode").ValueKind);
        Assert.DoesNotContain(feed.GetProperty("children").EnumerateArray(), child => child.GetProperty("id").GetGuid() == Slice2DemoSeed.OtherPlayerId);
    }

    [Fact]
    public async Task Payment_creates_no_guardian_player_link()
    {
        var before = await Count<GuardianPlayerLink>(x => x.GuardianId == Slice2DemoSeed.MainGuardianId && x.PlayerId == Slice2DemoSeed.OtherPlayerId);
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        Assert.Equal(before, await Count<GuardianPlayerLink>(x => x.GuardianId == Slice2DemoSeed.MainGuardianId && x.PlayerId == Slice2DemoSeed.OtherPlayerId));
    }

    [Fact]
    public async Task Failed_payment_does_not_renew_beneficiary()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Failed");
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
        Assert.Equal(0, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Retry_after_failure_creates_new_payment_request()
    {
        using var client = await GuardianClient();
        var oldPayment = await NewExternalPayment(client);
        await Simulate(client, oldPayment, "Failed");
        var response = await Post(client, $"/api/v1/guardian/subscriptions/payments/{oldPayment}/retry", new { }, await Csrf(client), Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        var fresh = (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        Assert.NotEqual(oldPayment, fresh);
        Assert.Equal(PaymentRequestStatus.Failed, await PaymentStatus(oldPayment));
        Assert.Equal(PaymentRequestStatus.Pending, await PaymentStatus(fresh));
    }

    [Fact]
    public async Task Late_success_for_failed_payment_has_no_financial_effect()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var lateSuccess = await SignedSuccess(payment);
        await Simulate(client, payment, "Failed");
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", lateSuccess)).EnsureSuccessStatusCode();
        Assert.Equal(PaymentRequestStatus.Failed, await PaymentStatus(payment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Late_success_for_cancelled_payment_has_no_financial_effect()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var lateSuccess = await SignedSuccess(payment);
        await Simulate(client, payment, "Cancelled");
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", lateSuccess)).EnsureSuccessStatusCode();
        Assert.Equal(PaymentRequestStatus.Cancelled, await PaymentStatus(payment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Duplicate_success_callback_is_idempotent()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var callback = await SignedSuccess(payment);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", callback)).EnsureSuccessStatusCode();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", callback)).EnsureSuccessStatusCode();
        Assert.Equal(1, await Count<PaymentProviderEvent>(x => x.ProviderEventId == callback.ProviderEventId));
        Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Double_click_external_renewal_start_is_idempotent()
    {
        using var client = await GuardianClient();
        var key = Guid.NewGuid().ToString();
        var first = await StartExternal(client, key);
        var second = await StartExternal(client, key);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstValue = (await first.Content.ReadFromJsonAsync<Created>())!;
        var secondValue = (await second.Content.ReadFromJsonAsync<Created>())!;
        Assert.Equal(firstValue.PaymentId, secondValue.PaymentId);
        Assert.Equal(1, await Count<RenewalRequest>(x => x.IdempotencyKey == key));
    }

    [Fact]
    public async Task Success_replay_creates_no_duplicate_collection_receipt_or_period()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        await Simulate(client, payment, "Success");
        Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
        Assert.Equal(1, await Count<Receipt>(x => x.Collection.PaymentRequestId == payment));
        Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Client_cannot_override_amount_currency_or_plan_scope()
    {
        using var client = await GuardianClient();
        await PrepareLegacyPayment();
        var key = Guid.NewGuid().ToString();
        var response = await Post(client, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId, amount = .01m, currency = "USD" }, await Csrf(client), key);
        response.EnsureSuccessStatusCode();
        var paymentId = (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var payment = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.Include(x => x.RenewalRequest).SingleAsync(x => x.Id == paymentId);
            Assert.Equal(900m, payment.RenewalRequest.OriginalAmount);
            Assert.Equal(payment.RenewalRequest.FinalAmount, payment.Amount);
            Assert.InRange(payment.Amount, 0.02m, 900m);
            Assert.Equal("EGP", payment.Currency);
        }
        var wrongPlan = await Post(client, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.SwimmingMonthlyId }, await Csrf(client), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, wrongPlan.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_reference_and_id_tampering_are_rejected()
    {
        using var academyA = await GuardianClient();
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(academyA, Slice3DemoSeed.FutureAcademyRenewalCode)).StatusCode);
        var bEnrollment = await Enrollment(Slice2DemoSeed.AcademyBPlayerId);
        var tampered = await Post(academyA, "/api/v1/guardian/subscriptions/renewals", new { sportEnrollmentId = bEnrollment, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, await Csrf(academyA), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
    }

    private async Task<Guid> NewExternalPayment(HttpClient client)
    {
        var response = await StartExternal(client, Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
    }

    private async Task<HttpResponseMessage> StartExternal(HttpClient client, string key)
    {
        await PrepareLegacyPayment();
        return await StartSearchRenewal(client, Slice3DemoSeed.ExternalRenewalCode, key);
    }
    private async Task<HttpResponseMessage> StartSearchRenewal(HttpClient client, string reference, string key) => await Post(client,
        "/api/v1/guardian/subscriptions/external/renewals", new { reference, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, await Csrf(client), key);
    private async Task<JsonElement> Search(HttpClient client, string name)
    {
        var response = await Post(client, "/api/v1/guardian/subscriptions/external/search", new { name }, await Csrf(client));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<(Guid Player, Guid Enrollment, string? Code)> AddSearchEnrollment(string name, Guid groupId, bool active = true, bool createCode = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await db.TrainingGroups.SingleAsync(x => x.Id == groupId);
        var now = DateTimeOffset.UtcNow;
        var player = new Player { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerCode = "QA-" + Guid.NewGuid().ToString("N")[..18], ArabicName = name,
            DateOfBirth = new DateOnly(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerId = player.Id, SportId = group.SportId,
            BranchId = group.BranchId, TrainingGroupId = group.Id, IsActive = active, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(player, enrollment);
        var code = createCode ? BeneficiaryRenewalCodes.Generate() : null;
        if (code is not null)
        {
            var ownerId = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
            db.BeneficiaryRenewalReferences.Add(new BeneficiaryRenewalReference { Id = Guid.NewGuid(), AcademyId = group.AcademyId,
                SportEnrollmentId = enrollment.Id, GeneratedByUserId = ownerId, CodeHash = BeneficiaryRenewalCodes.Hash(code),
                CodeHint = BeneficiaryRenewalCodes.Hint(code), ExpiresAtUtc = now.AddYears(1), CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync();
        return (player.Id, enrollment.Id, code);
    }
    private async Task<HttpResponseMessage> Resolve(HttpClient client, string reference) => await Post(client, "/api/v1/guardian/subscriptions/external/resolve", new { reference }, await Csrf(client));
    private async Task<HttpClient> StaffClient(string email)
    {
        var client = factory.CreateClient();
        (await Post(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, await Csrf(client))).EnsureSuccessStatusCode();
        return client;
    }
    private async Task<HttpResponseMessage> Rotate(HttpClient client, string path, string revision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/regenerate") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
        request.Headers.Add("If-Match", $"\"{revision}\"");
        return await client.SendAsync(request);
    }
    private async Task Simulate(HttpClient client, Guid payment, string outcome) => (await Post(client, $"/api/v1/guardian/subscriptions/payments/{payment}/simulate", new { outcome }, await Csrf(client))).EnsureSuccessStatusCode();
    private async Task<HttpClient> GuardianClient(string phone = DemoSeed.GuardianPhone) { var client = factory.CreateClient(); var request = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }, await Csrf(client)); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = phone, code = "246810" }, await Csrf(client))).EnsureSuccessStatusCode(); return client; }
    private async Task<ProviderEventEnvelope> SignedSuccess(Guid payment) { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); return scope.ServiceProvider.GetRequiredService<IPaymentGateway>().CreateTestEvent(await db.PaymentRequests.SingleAsync(x => x.Id == payment), PaymentEventOutcome.Success); }
    private async Task<Guid> BeneficiaryEnrollment() => await Enrollment(Slice2DemoSeed.OtherPlayerId);
    private async Task<Guid> Enrollment(Guid player) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(x => x.PlayerId == player).Select(x => x.Id).SingleAsync(); }
    private async Task<PaymentRequestStatus> PaymentStatus(Guid payment) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.Where(x => x.Id == payment).Select(x => x.Status).SingleAsync(); }
    private async Task<int> Count<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Set<T>().CountAsync(predicate); }
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf"))!.Token;
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token, string? key = null) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token); if (key is not null) request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }

    private sealed record CsrfDto(string Token);
    private sealed record Challenge(Guid ChallengeId);
    private sealed record Created(Guid PaymentId);
    private sealed record Resolved(string PlayerDisplayName, string Sport, string AcademyName, List<PlanView> Plans, string? Branch);
    private sealed record PlanView(Guid Id, string ArabicName, decimal Price, string Currency);
    private sealed record PaymentState(string Status, Guid? ReceiptId);
    private sealed record ReceiptView(string PlayerNameSnapshot, decimal Amount, string Currency, string ProviderReference);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
