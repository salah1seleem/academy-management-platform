using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice6;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    [Theory]
    [InlineData("71000000-0000-0000-0000-000000000001")]
    [InlineData("71000000-0000-0000-0000-000000000004")]
    public async Task Enrollment_payment_source_includes_authorized_child_guardian_sport_and_branch_without_collecting(string source)
    {
        using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        using var foreign = await InboxStaff(DemoSeed.FutureOwnerEmail);
        using var guardian = await GuardianClient();
        var id = Guid.Parse(source);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var expected = await db.NewEnrollmentRequests.AsNoTracking().Include(x => x.ExistingPlayer).Include(x => x.RequestedByGuardianUser).Include(x => x.Sport).Include(x => x.PreferredBranch).SingleAsync(x => x.Id == id);
        var collections = await db.Collections.CountAsync();
        var result = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/enrollment-requests/{id}/payment");
        Assert.Equal(id, result.GetProperty("id").GetGuid());
        Assert.Equal(expected.ExistingPlayer?.ArabicName ?? expected.NewChildArabicName, result.GetProperty("child").GetString());
        Assert.Equal(expected.RequestedByGuardianUser.DisplayName, result.GetProperty("guardian").GetString());
        Assert.Equal(expected.Sport.ArabicName, result.GetProperty("sport").GetString());
        Assert.Equal(expected.PreferredBranch.ArabicName, result.GetProperty("branch").GetString());
        var own = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/enrollment-requests/{id}/payment");
        Assert.Equal(result.GetProperty("child").GetString(), own.GetProperty("child").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/manage/enrollment-requests/{id}/payment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/enrollment-requests/{Slice6DemoSeed.AcademyBRequestId}/payment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"/api/v1/manage/enrollment-requests/{id}/payment")).StatusCode);
        Assert.Equal(collections, await db.Collections.CountAsync());
    }

    [Fact]
    public async Task Payment_notice_review_type_comes_from_own_academy_source_and_preserves_recipient_role_isolation()
    {
        using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        using var other = await InboxStaff(DemoSeed.FutureOwnerEmail);
        using var guardian = await GuardianClient();
        var payment = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId);
        await using var family = await NewFamily();
        var draft = await CreateFamily(family); var familyId = draft.GetProperty("id").GetGuid();
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me"); var user = me.GetProperty("userId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections = await db.Collections.CountAsync(); var now = DateTimeOffset.UtcNow;
        var sources = new[] { (Slice6DemoSeed.PendingRequestId, "Enrollment"), (payment, "Renewal"), (familyId, "FamilyRenewal"), (Slice6DemoSeed.AcademyBRequestId, (string?)null), (Guid.NewGuid(), (string?)null) };
        var notices = sources.Select((source, i) => new AcademyNotice { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, UserId = user, AudienceRole = "AcademyOwner", TargetKind = "PaymentQueue", TargetId = source.Item1, EventKey = "Payment", Message = "إشعار اختبار مصدر طلب محفوظ", CreatedAtUtc = now.AddSeconds(i), UpdatedAtUtc = now }).ToArray();
        var hidden = new AcademyNotice { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, UserId = user, AudienceRole = "Coach", TargetKind = "PaymentQueue", TargetId = payment, Message = "دور آخر غير ظاهر", CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AcademyNotices.AddRange(notices); db.AcademyNotices.Add(hidden); await db.SaveChangesAsync();
        try
        {
            var inbox = await owner.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
            var rows = inbox.GetProperty("items").EnumerateArray().ToArray();
            for (var i = 0; i < sources.Length; i++)
            {
                var row = rows.Single(n => n.GetProperty("id").GetGuid() == notices[i].Id);
                Assert.Equal(sources[i].Item2, row.GetProperty("paymentReviewType").GetString());
                Assert.Equal(sources[i].Item1, row.GetProperty("targetId").GetGuid());
            }
            Assert.DoesNotContain(rows, n => n.GetProperty("id").GetGuid() == hidden.Id);
            foreach (var client in new[] { guardian, other })
            {
                var foreign = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
                Assert.DoesNotContain(foreign.GetProperty("items").EnumerateArray(), n => notices.Any(own => own.Id == n.GetProperty("id").GetGuid()));
                using var change = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/notifications/{notices[0].Id}/read") { Content = JsonContent.Create(new { isRead = true }) };
                change.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
                var write = await client.SendAsync(change);
                Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
            }
            Assert.Equal(collections, await db.Collections.CountAsync());
        }
        finally { foreach (var notice in notices.Append(hidden)) notice.IsActive = false; await db.SaveChangesAsync(); }
    }
}
