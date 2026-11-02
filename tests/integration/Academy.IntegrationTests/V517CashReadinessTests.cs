using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Cash_readiness_disabled_renewal_is_readable_but_cannot_collect_or_reveal_another_academy()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);
        var f=await CashCollectionFixture(owner);var payment=await PendingCashRequest(owner,f.Enrollment);var amount=await PendingCashAmount(owner,payment);var settings=await CashSettings();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before=(await db.Collections.CountAsync(),await db.Receipts.CountAsync(),await db.SubscriptionPeriods.CountAsync(),await db.CashReceiptPostings.CountAsync());
        try {
            await CashSetConfiguration(owner,settings with{CashEnabled=false});
            var response=await owner.GetAsync($"/api/v1/manage/payments/{payment}/cash-options");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var data=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.False(data.GetProperty("cashEnabled").GetBoolean());Assert.True(data.GetProperty("canPrepare").GetBoolean());Assert.Empty(data.GetProperty("sessions").EnumerateArray());Assert.Equal(amount,data.GetProperty("amount").GetString());
            Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/payments/{payment}/cash-options")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await PendingCash(owner,payment,f.Session,amount)).StatusCode);
            Assert.Equal(before,(await db.Collections.CountAsync(),await db.Receipts.CountAsync(),await db.SubscriptionPeriods.CountAsync(),await db.CashReceiptPostings.CountAsync()));
        } finally {await CashSetConfiguration(owner,settings);}
        var ready=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payments/{payment}/cash-options");Assert.True(ready.GetProperty("cashEnabled").GetBoolean());Assert.Contains(ready.GetProperty("sessions").EnumerateArray(),s=>s.GetProperty("id").GetGuid()==f.Session);
    }

    [Fact]
    public async Task Cash_readiness_disabled_registration_preserves_resource_scope_and_never_creates_a_player_or_receipt()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);var f=await CashRegistrationFixture(owner);var settings=await CashSettings();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var before=(await db.Players.CountAsync(),await db.Collections.CountAsync(),await db.Receipts.CountAsync());
        var path=$"/api/v1/people/registration-cash-options?branchId={f.Body.BranchId}&planId={f.Body.PlanId}";
        try {
            await CashSetConfiguration(owner,settings with{CashEnabled=false});
            var response=await owner.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var data=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.False(data.GetProperty("cashEnabled").GetBoolean());Assert.Empty(data.GetProperty("sessions").EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,"/api/v1/people/registrations",f.Body)).StatusCode);
            Assert.Equal(before,(await db.Players.CountAsync(),await db.Collections.CountAsync(),await db.Receipts.CountAsync()));
        } finally {await CashSetConfiguration(owner,settings);}
    }
}
