using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Finance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    // Each case owns a synthetic tenant. Immutable receipts never contaminate the baseline tenant's totals.
    private sealed record ExternalFixture(HttpClient Owner,Guid Academy,Guid Branch,Guid Sport,Guid Member,Guid User);
    private async Task<ExternalFixture> ExternalTenant(bool cash=false,bool swimming=false,string currency="EGP")
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now=DateTimeOffset.UtcNow;var a=Guid.NewGuid();var member=Guid.NewGuid();var user=Guid.NewGuid();var branch=Guid.NewGuid();var sport=Guid.NewGuid();var email=$"v4-external-{a:N}@example.test";
        var person=new ApplicationUser{Id=user,DisplayName="مالك اصطناعي للاتفاقات",UserName=email,Email=email,EmailConfirmed=true,CreatedAtUtc=now,UpdatedAtUtc=now};
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(person,"Demo-Only-123!")).Succeeded);
        db.Academies.Add(new(){Id=a,ArabicName="أكاديمية اتفاقات اختبار معزولة",Slug="v4-external-"+a.ToString("N"),TimeZone="Africa/Cairo",DefaultCurrency=currency,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.AcademyMemberships.Add(new(){Id=member,AcademyId=a,UserId=user,Role=AcademyRole.AcademyOwner,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.Branches.Add(new(){Id=branch,AcademyId=a,ArabicName="فرع اتفاقات اصطناعي",CreatedAtUtc=now,UpdatedAtUtc=now});
        db.Sports.Add(new(){Id=sport,AcademyId=a,ArabicName=swimming?"سباحة اصطناعية":"كرة قدم اصطناعية",Kind=swimming?SportKind.Swimming:SportKind.Football,CreatedAtUtc=now,UpdatedAtUtc=now});
        if(swimming)db.SwimmingBranchPolicies.Add(new(){Id=Guid.NewGuid(),AcademyId=a,SportId=sport,BranchId=branch,Enabled=true,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await AcademyConfigurationService.SeedDemo(db,[a]);
        foreach(var license in await db.AcademyFeatureEntitlements.Where(x=>x.AcademyId==a&&new[]{"external_partners","facilities"}.Contains(x.Feature)).ToArrayAsync())license.Enabled=true;
        await db.SaveChangesAsync();using var read=factory.Services.CreateScope();var active=await read.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.AsNoTracking().SingleAsync(x=>x.AcademyId==a&&x.State==ConfigurationState.Active);
        var settings=AcademyRuntimeSettings.Read(active.Payload);settings.Features["external_partners"]=true;settings.Features["facilities"]=swimming;
        var owner=await Staff(email);await AdActivate(owner,settings with{CashEnabled=cash});return new(owner,a,branch,sport,member,user);
    }
    private static ExternalAgreementWrite ExternalBody(ExternalFixture f,decimal amount=1000)=>new(Guid.NewGuid(),"اتفاق برنامج اصطناعي",Academy.Infrastructure.Finance.ExternalAgreementKind.ExternalProgram,f.Branch,f.Sport,new(2026,9,1),new(2026,10,31),new(2026,9,28),amount,"طرف خارجي اصطناعي");
    private static async Task<Guid> ExternalCreate(HttpClient owner,ExternalAgreementWrite body)
    {
        var r=await Send(owner,"/api/v1/finance/external-agreements/",body);Assert.Equal(HttpStatusCode.Created,r.StatusCode);return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    [Fact] public async Task V4_external_partial_receipts_are_idempotent_scope_isolated_and_match_every_financial_projection()
    {
        var f=await ExternalTenant();using var owner=f.Owner;using var other=await Staff();using var coach=await Mobile(AcademyRole.Coach);var body=ExternalBody(f);var id=await ExternalCreate(owner,body);
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,"/api/v1/finance/external-agreements/",body)).StatusCode);
        var receipt=new ExternalReceiptWrite(Guid.NewGuid(),400,"Online",true,"مرجع اصطناعي دون تحويل");var path=$"/api/v1/finance/external-agreements/{id}/receipts";
        Assert.Equal(HttpStatusCode.Created,(await Send(owner,path,receipt)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Send(owner,path,receipt)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,path,receipt with{Amount=500})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,path,receipt with{RequestId=Guid.NewGuid(),ConfirmedReceived=false})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,path,receipt with{RequestId=Guid.NewGuid(),Amount=601})).StatusCode);
        var concurrent=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Send(owner,path,new ExternalReceiptWrite(Guid.NewGuid(),500,"Online",true))));
        Assert.Single(concurrent,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(concurrent,r=>r.StatusCode==HttpStatusCode.Conflict);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/external-agreements/{id}");Assert.Equal(900m,detail.GetProperty("paid").GetDecimal());Assert.Equal(100m,detail.GetProperty("remaining").GetDecimal());Assert.Equal(2,detail.GetProperty("receipts").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,$"/api/v1/finance/external-agreements/{id}/cancel",new ExternalCancelWrite(detail.GetProperty("agreement").GetProperty("version").GetUInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/finance/external-agreements/{id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync($"/api/v1/finance/external-agreements/{id}")).StatusCode);
        var financial=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true&collectionType=External&externalKind=ExternalProgram");Assert.Equal(900m,financial.GetProperty("netAmount").GetDecimal());Assert.All(financial.GetProperty("items").EnumerateArray(),r=>Assert.Equal("External",r.GetProperty("collectionType").GetString()));
        Assert.Equal(0,(await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true&collectionType=External&externalKind=PoolRental")).GetProperty("totalCount").GetInt32());
        var statement=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?allDates=true");Assert.Equal(900m,statement.GetProperty("totalIncome").GetDecimal());Assert.Equal(100m,statement.GetProperty("externalReceivables").GetDecimal());Assert.Equal(JsonValueKind.Null,statement.GetProperty("earnedRevenue").ValueKind);
        var cockpit=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-cockpit?allDates=true");Assert.Contains("900",cockpit.GetRawText());
        Assert.Contains("اتفاق خارجي",await owner.GetStringAsync("/api/v1/reports/financial/export?allDates=true&collectionType=External"));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.PayrollAdjustments.AnyAsync(x=>x.AcademyId==f.Academy));Assert.False(await db.Collections.AnyAsync(x=>x.AcademyId==f.Academy));
        var saved=await db.ExternalAgreementReceipts.FirstAsync(r=>r.AgreementId==id);saved.Amount=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact] public async Task V4_external_cash_requires_collectors_open_session_and_posts_exactly_once()
    {
        var f=await ExternalTenant(cash:true,currency:"SAR");using var owner=f.Owner;var id=await ExternalCreate(owner,ExternalBody(f));var path=$"/api/v1/finance/external-agreements/{id}/receipts";var receipt=new ExternalReceiptWrite(Guid.NewGuid(),400,"Cash",true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,path,receipt)).StatusCode);
        var register=await Send(owner,"/api/v1/finance/cash-registers/",new CashRegisterWrite(f.Branch,"صندوق تجريبي"),key:Guid.NewGuid().ToString());register.EnsureSuccessStatusCode();var registerId=(await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(owner,$"/api/v1/finance/cash-registers/{registerId}/cashiers",new CashierWrite(f.Member,true),true)).EnsureSuccessStatusCode();
        var open=await Send(owner,$"/api/v1/finance/cash-registers/{registerId}/open",new CashOpenWrite(100),key:Guid.NewGuid().ToString());open.EnsureSuccessStatusCode();var session=(await open.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();receipt=receipt with{CashSessionId=session};
        Assert.Equal(HttpStatusCode.Created,(await Send(owner,path,receipt)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Send(owner,path,receipt)).StatusCode);
        var position=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/cash-registers/sessions/{session}");Assert.Equal("500.00",position.GetProperty("balance").GetString());Assert.Single(position.GetProperty("externalReceipts").EnumerateArray());
        Assert.Equal("SAR",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/operating-statement?allDates=true")).GetProperty("currency").GetString());
        (await Send(owner,$"/api/v1/finance/cash-registers/sessions/{session}/close",new CashCloseWrite(500,null),key:Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,path,receipt with{RequestId=Guid.NewGuid(),Amount=100})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(1,await db.CashExternalReceiptPostings.CountAsync(x=>x.AcademyId==f.Academy));
    }
    [Fact] public async Task V4_rental_is_a_real_capacity_reservation_and_unpaid_cancellation_releases_it_atomically()
    {
        var f=await ExternalTenant(swimming:true);using var owner=f.Owner;
        var facility=await Send(owner,"/api/v1/facilities/",new FacilityWrite("مرفق اختبار",f.Branch));facility.EnsureSuccessStatusCode();var facilityId=(await facility.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var space=await Send(owner,"/api/v1/facilities/spaces",new SpaceWrite("مسبح اصطناعي",facilityId,f.Sport,FacilitySpaceKind.Pool));space.EnsureSuccessStatusCode();var spaceId=(await space.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var resource=await Send(owner,"/api/v1/facilities/resources",new ResourceWrite("A",spaceId,8,false,[0,1,2,3,4,5,6],new(8,0),new(22,0),["FacilityBooking"]));resource.EnsureSuccessStatusCode();var resourceId=(await resource.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(owner,$"/api/v1/facilities/resources/{resourceId}/approve",new CapacityApproval("اعتماد اصطناعي للاختبار فقط وليس إثبات سلامة",true))).EnsureSuccessStatusCode();
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime).AddDays(1);
        var body=ExternalBody(f,12000) with{Kind=Academy.Infrastructure.Finance.ExternalAgreementKind.PoolRental,FromDate=day,ToDate=day,DueOn=day,Slots=[new(resourceId,day,new(9,0),new(10,0),8,true)]};var id=await ExternalCreate(owner,body);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/finance/external-agreements/",body with{RequestId=Guid.NewGuid()})).StatusCode);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/external-agreements/{id}");var booking=Assert.Single(detail.GetProperty("bookings").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,$"/api/v1/facilities/bookings/{booking}/cancel",new{})).StatusCode);
        (await Send(owner,$"/api/v1/finance/external-agreements/{id}/cancel",new ExternalCancelWrite(detail.GetProperty("agreement").GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
        await ExternalCreate(owner,body with{RequestId=Guid.NewGuid()});
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(ResourceBookingState.Cancelled,await db.ResourceBookings.Where(b=>b.Id==booking).Select(b=>b.State).SingleAsync());Assert.Equal(2,await db.ExternalAgreements.CountAsync(x=>x.AcademyId==f.Academy));Assert.Equal(0,await db.ExternalAgreementReceipts.CountAsync(x=>x.AcademyId==f.Academy));
    }
}
