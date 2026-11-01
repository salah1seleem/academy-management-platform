using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Approved_manual_renewal_reminder_requires_preview_persists_once_and_is_recipient_scoped()
    {
        using var owner = await Staff();
        using var admin = await Staff(DemoSeed.AdminEmail);
        using var guardian = await Mobile(AcademyRole.Guardian);
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Reuse the existing expired, explicitly synthetic LegacyRegression fixture.
        // Only its family link is added in this dedicated integration database.
        var player = RenewalOpportunityDemoSeed.Id(6, 1);
        var parent = await db.GuardianProfiles.SingleAsync(g => g.Id == Slice2DemoSeed.MainGuardianId);
        if (!await db.GuardianPlayerLinks.AnyAsync(l => l.GuardianId == parent.Id && l.PlayerId == player && l.IsActive))
        {
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink
            {
                Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId,
                GuardianId = parent.Id, PlayerId = player, CreatedByUserId = parent.UserId,
                RelationshipType = "رابط اختبار اصطناعي"
            });
            await db.SaveChangesAsync();
        }
        var before = (await db.Collections.CountAsync(), await db.Receipts.CountAsync(),
            await db.SubscriptionPeriods.CountAsync(), await db.PaymentRequests.CountAsync());
        var candidates = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/notifications/recipients?kind=Renewal");
        Assert.Contains(candidates.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetGuid() == parent.Id);
        var request = Guid.NewGuid();
        var marker = "تذكير اختبار معزول " + request.ToString("N");
        var body = new NoticeEndpoints.DispatchWrite("Renewal", null, [parent.Id], marker + " {اسم_ولي_الأمر} {اسم_اللاعب}", null, request);
        const string root = "/api/v1/manage/notifications";
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, root + "/send", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(guardian, root + "/send", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(other, root + "/preview", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, root + "/send", body)).StatusCode);
        Assert.False(await db.NoticeDispatches.AnyAsync(d => d.RequestId == request));
        Assert.False(await db.AcademyNotices.AnyAsync(n => n.Message.Contains(marker)));

        var previewResponse = await Send(owner, root + "/preview", body);
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("StoredInApp", preview.GetProperty("delivery").GetString());
        Assert.Equal(1, preview.GetProperty("count").GetInt32());
        Assert.False(await db.AcademyNotices.AnyAsync(n => n.Message.Contains(marker)));
        var confirmed = body with { PreviewHash = preview.GetProperty("previewHash").GetString() };
        var sent = await Send(owner, root + "/send", confirmed);
        sent.EnsureSuccessStatusCode();
        Assert.False((await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        var replay = await Send(owner, root + "/send", confirmed);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, root + "/send", confirmed with { Message = marker + " رسالة مختلفة" })).StatusCode);
        var notice = Assert.Single(await db.AcademyNotices.AsNoTracking().Where(n => n.Message.Contains(marker)).ToArrayAsync());
        Assert.Equal(parent.UserId, notice.UserId);
        Assert.Equal(DemoSeed.NogoomAcademyId, notice.AcademyId);
        Assert.Equal("Guardian", notice.AudienceRole);
        Assert.Equal("Renewal", notice.EventKey);
        Assert.Equal("Renewal", notice.TargetKind);
        Assert.Null(notice.TargetId);
        Assert.DoesNotContain("{اسم_", notice.Message);
        var inbox = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Contains(inbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == notice.Id);
        foreach (var nonRecipient in new[] { owner, admin, other })
        {
            var hidden = await nonRecipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
            Assert.DoesNotContain(hidden.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == notice.Id);
            Assert.Equal(HttpStatusCode.NotFound,
                (await Send(nonRecipient, $"/api/v1/notifications/{notice.Id}/read", new { isRead = true }, true)).StatusCode);
        }
        Assert.Equal(1, await db.NoticeDispatches.CountAsync(d => d.RequestId == request));
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.Receipts.CountAsync(),
            await db.SubscriptionPeriods.CountAsync(), await db.PaymentRequests.CountAsync()));
    }
}
