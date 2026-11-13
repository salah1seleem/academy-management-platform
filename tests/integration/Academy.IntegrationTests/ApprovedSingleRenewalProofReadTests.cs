using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Guardian_single_payment_persists_proof_state_without_disclosing_another_request_or_collecting()
    {
        var f = await Fixture(); using var owner = await Staff(); using var guardian = await Guardian();
        var plan = await CreatePlan(owner, f.A);
        var created = await Write(guardian, "/api/v1/guardian/subscriptions/renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan });
        created.EnsureSuccessStatusCode();
        var payment = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        var readPath = $"/api/v1/guardian/subscriptions/payments/{payment}";
        var initial = await guardian.GetFromJsonAsync<JsonElement>(readPath);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("proofStatus").ValueKind);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.RenewalRequests.Where(r => r.PaymentRequestId == payment).Select(r => r.RequestedByUserId).SingleAsync();
        var asset = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        foreach (var file in new[] { asset, replacement })
            db.PrivateAssets.Add(new() { Id = file, AcademyId = DemoSeed.NogoomAcademyId, UploadedByUserId = user,
                ContentType = "image/png", StorageName = $"synthetic-proof-{file}.png", Length = 10, Purpose = "Proof" });
        await db.SaveChangesAsync();
        var proofPath = $"/api/v1/guardian/payments/{payment}/proof";
        (await Write(guardian, proofPath, new { assetId = asset })).EnsureSuccessStatusCode();
        var repeated = await Write(guardian, proofPath, new { assetId = asset });
        repeated.EnsureSuccessStatusCode();
        Assert.True((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        var pending = await guardian.GetFromJsonAsync<JsonElement>(readPath);
        Assert.Equal("Pending", pending.GetProperty("proofStatus").GetString());
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal("Manual", pending.GetProperty("provider").GetString());
        Assert.False(pending.GetProperty("simulationAvailable").GetBoolean());
        Assert.Equal(1, await db.PaymentProofs.CountAsync(p => p.PaymentRequestId == payment));
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == payment));
        var other = await Fixture(linkedGuardian: false); var otherPlan = await CreatePlan(owner, other.A);
        var otherCreated = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = other.Enrollment, subscriptionPlanId = otherPlan });
        otherCreated.EnsureSuccessStatusCode();
        var otherPayment = (await otherCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/subscriptions/payments/{otherPayment}")).StatusCode);
        var managePath = $"/api/v1/manage/payments/{payment}";
        Assert.Equal(asset, (await owner.GetFromJsonAsync<JsonElement>($"{managePath}/proof")).GetProperty("assetId").GetGuid());
        (await Write(guardian, proofPath, new { assetId = replacement })).EnsureSuccessStatusCode();
        var decision = new ReviewWrite(false, null, "رفض اصطناعي لحالة الإثبات", replacement);
        foreach (var expectedAsset in new Guid?[] { null, asset })
        {
            var stale = await Write(owner, $"{managePath}/review", decision with { ExpectedProofAssetId = expectedAsset });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("renewal_proof_changed", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal("Pending", (await guardian.GetFromJsonAsync<JsonElement>(readPath)).GetProperty("status").GetString());
        (await Write(owner, $"{managePath}/review", decision)).EnsureSuccessStatusCode();
        var replay = await Write(owner, $"{managePath}/review", decision);
        replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"{managePath}/review", decision with { Reason = "ملاحظة مختلفة" })).StatusCode);
        var replayUpload = await Write(guardian, proofPath, new { assetId = replacement });
        replayUpload.EnsureSuccessStatusCode();
        Assert.Equal("Rejected", (await replayUpload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var rejected = await guardian.GetFromJsonAsync<JsonElement>(readPath);
        Assert.Equal("Rejected", rejected.GetProperty("proofStatus").GetString());
        Assert.Equal("Cancelled", rejected.GetProperty("status").GetString());
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == payment));
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("receiptId").ValueKind);
    }
}
