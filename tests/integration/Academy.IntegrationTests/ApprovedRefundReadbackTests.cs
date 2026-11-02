using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_refund_readback_identifies_exact_collection_payout_and_shift_without_foreign_access(bool cash)
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);
        Guid player,collection; Guid? shift=null; HttpResponseMessage created;
        if(cash){var f=await CashRefundFixture(owner);player=f.Player;collection=f.Collection;shift=f.Session;var body=await CashRefundQuote(owner,player,collection,f.Session,13.34m);created=await Post(owner,RefundRoot(player),body,Guid.NewGuid().ToString());}
        else{var f=await Fixture();player=f.Player;collection=await Collection(f.Enrollment,f.Owner,100);created=await Refund(owner,player,collection,"13.34","سبب اصطناعي لقراءة المصدر");}
        created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var history=await owner.GetFromJsonAsync<JsonElement>(RefundRoot(player));
        var saved=Assert.Single(history.GetProperty("refunds").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==id);
        Assert.Equal(collection,saved.GetProperty("collectionId").GetGuid());
        Assert.Equal(13.34m,saved.GetProperty("amount").GetDecimal());Assert.Equal("EGP",saved.GetProperty("currency").GetString());
        Assert.Equal(cash?"Cash":"Online",saved.GetProperty("payoutMethod").GetString());
        if(shift.HasValue)Assert.Equal(shift,saved.GetProperty("cashSessionId").GetGuid());
        else{Assert.Equal(JsonValueKind.Null,saved.GetProperty("cashSessionId").ValueKind);Assert.Equal("سبب اصطناعي لقراءة المصدر",saved.GetProperty("reason").GetString());}
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Guardian();
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(RefundRoot(player))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(RefundRoot(player))).StatusCode);
    }
}
