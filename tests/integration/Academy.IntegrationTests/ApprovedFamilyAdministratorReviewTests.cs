using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    [Fact]
    public async Task Approved_family_authorized_admin_reviews_once_then_revoked_permission_denies_replay()
    {
        await using var family = await NewFamily();
        var request = await FamilyProof(family, await CreateFamily(family));
        var id = request.GetProperty("id").GetGuid();
        using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        var key = Guid.NewGuid().ToString("N");
        var email = "family.reviewer." + key + "@example.test";
        var create = await Post(owner, "/api/v1/manage/staff",
            new StaffWrite("إداري مراجعة أسرة اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999),
                "Demo-Only-123!", AcademyRole.AcademyAdmin, "مراجع اختبار"), await Csrf(owner));
        create.EnsureSuccessStatusCode();
        var member = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m,
            m is "subscriptions" or "payments" or "media", false,
            m is "subscriptions" or "payments", false)).ToArray();
        async Task Grant(StaffActionValue[] values)
        {
            using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access")
            {
                Content = JsonContent.Create(new StaffAccessWrite(false, [Slice2DemoSeed.CityBranchId], values))
            };
            message.Headers.Add("X-CSRF-TOKEN", await Csrf(owner));
            (await owner.SendAsync(message)).EnsureSuccessStatusCode();
        }
        await Grant(actions);
        using var admin = await InboxStaff(email);
        var read = await admin.GetFromJsonAsync<JsonElement>($"{FamilyStaff}/{id}");
        Assert.True(read.GetProperty("canReview").GetBoolean());
        var approval = FamilyApproval(request);
        var response = await Post(admin, $"{FamilyStaff}/{id}/review", approval, await Csrf(admin));
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", saved.GetProperty("status").GetString());
        (await Post(admin, $"{FamilyStaff}/{id}/review", approval, await Csrf(admin))).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollments = family.Choices.Select(c => c.SportEnrollmentId).ToArray();
        Assert.Equal(2, await db.Receipts.CountAsync(r => enrollments.Contains(r.SportEnrollmentId)));
        Assert.Equal(2, await db.SubscriptionPeriods.CountAsync(r => enrollments.Contains(r.SportEnrollmentId)));
        Assert.Equal(2, await db.Collections.CountAsync(r => enrollments.Contains(r.SportEnrollmentId)));
        Assert.Equal(Decimal(saved, "totalAmount"),
            await db.Collections.Where(r => enrollments.Contains(r.SportEnrollmentId)).SumAsync(r => r.Amount));
        Assert.Equal(1, await db.AcademyNotices.CountAsync(n => n.UserId == family.User && n.TargetId == id));
        var own = await family.Client.GetFromJsonAsync<JsonElement>($"{FamilyRequests}/{id}");
        Assert.Equal("Approved", own.GetProperty("status").GetString());
        Assert.Equal(saved.GetProperty("items").GetArrayLength(), own.GetProperty("items").GetArrayLength());
        await Grant(actions.Select(a => a with { Edit = false }).ToArray());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, $"{FamilyStaff}/{id}/review", approval, await Csrf(admin))).StatusCode);
        Assert.Equal(2, await db.Collections.CountAsync(r => enrollments.Contains(r.SportEnrollmentId)));
    }
}
