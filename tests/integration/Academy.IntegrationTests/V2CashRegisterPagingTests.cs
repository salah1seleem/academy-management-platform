using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Cash_register_search_and_pages_keep_all_records_reachable_and_tenant_scoped()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var prefix = "تصفح صناديق اصطناعي " + Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(0, 101).Select(i => new CashRegister
        {
            Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = Slice2DemoSeed.CityBranchId,
            Name = prefix + " " + i, Currency = "EGP", CreatedByUserId = actor,
            RequestKey = Guid.NewGuid().ToString("N"), RequestHash = new string('A', 64), CreatedAtUtc = at, UpdatedAtUtc = at
        }).ToArray();
        db.CashRegisters.AddRange(rows);
        await db.SaveChangesAsync();
        var url = CashRoot + "?search=" + Uri.EscapeDataString(prefix);
        var first = await owner.GetFromJsonAsync<JsonElement>(url);
        var second = await owner.GetFromJsonAsync<JsonElement>(url + "&offset=100");
        Assert.Equal(101, first.GetProperty("total").GetInt32());
        Assert.Equal(100, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(r => r.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(rows.Select(r => r.Id).Order(), ids.Order());
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>(url)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(CashRoot + "?offset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(CashRoot + "?search=" + new string('a', 101))).StatusCode);
    }
}
