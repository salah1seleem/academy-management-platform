using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2ReceivablesTests
{
    private async Task<HttpResponseMessage> Inventory(HttpClient client,Guid product,ProductVariants.InventoryWrite body)
    {
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        var request=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/content/catalog/{product}/inventory"){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",csrf);return await client.SendAsync(request);
    }
    [Fact]
    public async Task Approved_inventory_reconciles_legacy_stock_rejects_duplicates_stale_versions_and_other_academies()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashProductFixture(owner);var product=f.Body.ProductId;
        var body=new ProductVariants.InventoryWrite(0,true,true,[new("M",7,2),new("L",10,1)]);
        Assert.Equal(HttpStatusCode.Conflict,(await Inventory(owner,product,body with{ReconcileExistingStock=false})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Inventory(owner,product,body with{Variants=[new("M",7,1)]})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Inventory(owner,product,body with{Variants=[new("m",7,1),new(" M ",7,2)]})).StatusCode);
        (await Inventory(owner,product,body)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Inventory(owner,product,body)).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/content/catalog/{product}/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Inventory(foreign,product,body with{ExpectedRevision=1})).StatusCode);
        using var guardian=await Guardian();Assert.Equal(HttpStatusCode.Forbidden,(await Inventory(guardian,product,body)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var p=await db.SportCatalogItems.Include(p=>p.Variants).SingleAsync(p=>p.Id==product);Assert.Equal(3,p.StockQuantity);Assert.Equal(3,p.Variants.Sum(v=>v.Quantity));Assert.Equal(1,p.StockRevision);
        Assert.All(p.Variants,v=>Assert.Equal(DemoSeed.NogoomAcademyId,v.AcademyId));
    }
    [Fact]
    public async Task V513_colors_keep_same_size_stock_independent_and_sale_snapshot_immutable()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);
        var f=await CashProductFixture(owner);var p=f.Body.ProductId;
        (await Inventory(owner,p,new(0,false,true,[new("M",null,2,"كحلي"),new("M",null,1,"أبيض")]))).EnsureSuccessStatusCode();
        var inventory=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/content/catalog/{p}/inventory");
        var blue=inventory.GetProperty("variants").EnumerateArray().Single(v=>v.GetProperty("color").GetString()=="كحلي").GetProperty("id").GetGuid();
        var body=f.Body with{Quantity=2,ExpectedAmount=180,VariantId=blue};
        var key=Guid.NewGuid().ToString();var response=await Post(owner,SalesRoot,body,key);response.EnsureSuccessStatusCode();
        (await Post(owner,SalesRoot,body,key)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var balances=await db.ProductStockVariants.Where(v=>v.ProductId==p).ToListAsync();
        Assert.Equal(0,balances.Single(v=>v.Color=="كحلي").Quantity);Assert.Equal(1,balances.Single(v=>v.Color=="أبيض").Quantity);
        var sale=await db.ProductSales.SingleAsync(s=>s.ProductId==p);Assert.Equal("كحلي",sale.ColorSnapshot);Assert.Equal("M",sale.SizeSnapshot);
        sale.ColorSnapshot="أبيض";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Approved_last_numbered_stock_competing_cash_sales_commit_one_snapshot_and_old_hash_remains_compatible()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashProductFixture(owner);var original=f.Body;
        var oldHash=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{original.ProductId,original.Quantity,original.PlayerId,original.PaymentMethod,original.BranchId,original.ExpectedAmount,original.CashSessionId}));
        Assert.Equal(oldHash,CashRegisterService.ProductHash(original));
        (await Inventory(owner,original.ProductId,new(0,true,true,[new("M",7,3)]))).EnsureSuccessStatusCode();
        var inventory=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/content/catalog/{original.ProductId}/inventory");var variant=inventory.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var body=original with{Quantity=3,ExpectedAmount=270,VariantId=variant};var keys=new[]{Guid.NewGuid().ToString(),Guid.NewGuid().ToString()};
        var quotes=await owner.GetFromJsonAsync<JsonElement>($"{ProductQuoteUrl(body)}&variantId={variant}");Assert.Equal("270.00",quotes.GetProperty("amount").GetString());
        var results=await Task.WhenAll(Post(owner,SalesRoot,body,keys[0]),Post(owner,SalesRoot,body,keys[1]));
        Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        var success=Array.FindIndex(results,r=>r.IsSuccessStatusCode);var id=(await results[success].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Post(owner,SalesRoot,body,keys[success])).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,SalesRoot,body with{VariantId=Guid.NewGuid()},keys[success])).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sale=Assert.Single(await db.ProductSales.Where(s=>s.ProductId==body.ProductId).ToArrayAsync());Assert.Equal(id,sale.Id);Assert.Equal("M",sale.SizeSnapshot);Assert.Equal(7,sale.ShirtNumberSnapshot);Assert.Equal(270,sale.Amount);
        Assert.Single(await db.CashProductPostings.Where(p=>p.ProductSaleId==id).ToArrayAsync());Assert.Equal(0,await db.ProductStockVariants.Where(v=>v.Id==variant).Select(v=>v.Quantity).SingleAsync());Assert.Equal(0,await db.SportCatalogItems.Where(p=>p.Id==body.ProductId).Select(p=>p.StockQuantity).SingleAsync());
    }
    [Fact]
    public async Task Approved_guardian_number_is_enrollment_scoped_explicit_order_and_competing_reviews_never_substitute()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var guardian=await Guardian();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user=await db.Users.Where(u=>u.PhoneNumber==DemoSeed.GuardianPhone).Select(u=>u.Id).SingleAsync();
        var e=await db.SportEnrollments.Where(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.IsActive&&e.Player.IsActive&&db.GuardianPlayerLinks.Any(l=>l.PlayerId==e.PlayerId&&l.IsActive&&l.Guardian.UserId==user)).FirstAsync();
        var p=new SportCatalogItem{Id=Guid.NewGuid(),AcademyId=e.AcademyId,SportId=e.SportId,ArabicName="زي تجريبي "+Guid.NewGuid(),ImageReference="/demo-assets/catalog-football.svg",DisplayPrice=100,Currency="EGP",StockQuantity=2};db.Add(p);await db.SaveChangesAsync();
        (await Inventory(owner,p.Id,new(0,true,true,[new("M",7,1),new("M",8,1)]))).EnsureSuccessStatusCode();
        var csrf=(await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();var shirt=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/people/enrollments/{e.Id}/shirt-number"){Content=JsonContent.Create(new ProductVariants.ShirtWrite(7))};shirt.Headers.Add("X-CSRF-TOKEN",csrf);(await owner.SendAsync(shirt)).EnsureSuccessStatusCode();
        var choices=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/catalog/{p.Id}/variants?enrollmentId={e.Id}");Assert.Equal(7,choices.GetProperty("assignedShirtNumber").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/catalog/{p.Id}/variants?enrollmentId={Guid.NewGuid()}")).StatusCode);
        var variant=choices.GetProperty("variants").EnumerateArray().Single(v=>v.GetProperty("shirtNumber").GetInt32()==7).GetProperty("id").GetGuid();
        var body=new SaleWrite(p.Id,1,e.PlayerId,null,e.BranchId,ExpectedAmount:100,VariantId:variant,SportEnrollmentId:e.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(guardian,"/api/v1/guardian/product-sales",body with{ExpectedAmount=99})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(guardian,"/api/v1/guardian/product-sales",body with{VariantId=null})).StatusCode);
        var ids=new List<Guid>();for(var i=0;i<2;i++){
            var created=await Post(guardian,"/api/v1/guardian/product-sales",body,Guid.NewGuid().ToString());created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();ids.Add(id);
            var proof=new PrivateAsset{Id=Guid.NewGuid(),AcademyId=e.AcademyId,UploadedByUserId=user,ContentType="image/png",StorageName="synthetic-variant-proof-"+Guid.NewGuid()+".png",Length=20,Purpose="Proof"};db.Add(proof);await db.SaveChangesAsync();
            (await Post(guardian,$"/api/v1/guardian/product-sales/{id}/proof",new{assetId=proof.Id})).EnsureSuccessStatusCode();
        }
        var reviewed=await Task.WhenAll(Post(owner,$"{SalesRoot}/{ids[0]}/review",new{accept=true}),Post(owner,$"{SalesRoot}/{ids[1]}/review",new{accept=true}));Assert.Single(reviewed,r=>r.IsSuccessStatusCode);Assert.Single(reviewed,r=>r.StatusCode==HttpStatusCode.Conflict);
        var winner=ids[Array.FindIndex(reviewed,r=>r.IsSuccessStatusCode)];(await Post(owner,$"{SalesRoot}/{winner}/review",new{accept=true})).EnsureSuccessStatusCode();
        var loser=ids.Single(id=>id!=winner);(await Post(owner,$"{SalesRoot}/{loser}/review",new{accept=false,reason="مراجعة الرصيد الاصطناعي"})).EnsureSuccessStatusCode();
        var balances=await db.ProductStockVariants.AsNoTracking().Where(v=>v.ProductId==p.Id).ToArrayAsync();Assert.Equal(0,balances.Single(v=>v.ShirtNumber==7).Quantity);Assert.Equal(1,balances.Single(v=>v.ShirtNumber==8).Quantity);
        var detail=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/product-sales/{winner}");Assert.Equal(7,detail.GetProperty("shirtNumberSnapshot").GetInt32());Assert.Equal("M",detail.GetProperty("sizeSnapshot").GetString());Assert.Equal(e.Id,detail.GetProperty("sportEnrollmentId").GetGuid());
        Assert.Single(await db.ProductSales.Where(s=>s.ProductId==p.Id&&s.Status=="Confirmed").ToArrayAsync());
        var otherPhone=await db.GuardianProfiles.Where(g=>g.AcademyId==e.AcademyId&&g.UserId!=user&&g.User.IsActive&&g.User.PhoneNumber!=null&&db.AcademyMemberships.Any(m=>m.UserId==g.UserId&&m.AcademyId==e.AcademyId&&m.IsActive&&m.Role==Academy.Infrastructure.Tenancy.AcademyRole.Guardian)).Select(g=>g.User.PhoneNumber).FirstAsync();
        using var stranger=await Guardian(otherPhone);Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/v1/guardian/product-sales/{winner}")).StatusCode);
        db.ChangeTracker.Clear();var confirmed=await db.ProductSales.SingleAsync(s=>s.Id==winner);confirmed.ShirtNumberSnapshot=8;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());

    }
}
