using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string FamilyCashRoot="/api/v1/finance/cash-registers";
    private async Task<AcademyRuntimeSettings> FamilyCashSettings() { await using var s=factory.Services.CreateAsyncScope();return AcademyRuntimeSettings.Read((await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.SingleAsync(v=>v.AcademyId==DemoSeed.NogoomAcademyId&&v.State==ConfigurationState.Active)).Payload); }
    private async Task FamilyCashConfiguration(HttpClient owner,AcademyRuntimeSettings settings)
    {
        var draft=await Post(owner,"/api/v1/manage/configuration/drafts",settings,await Csrf(owner));draft.EnsureSuccessStatusCode();var id=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var target in new[]{ConfigurationState.Validated,ConfigurationState.Staged,ConfigurationState.Active}) { await using var s=factory.Services.CreateAsyncScope();var version=await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.Where(v=>v.Id==id).Select(v=>v.Version).SingleAsync();var response=await Post(owner,$"/api/v1/manage/configuration/{id}/transition",new{target=target.ToString(),version},await Csrf(owner));Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync()); }
    }
    private async Task<(Guid Session,Guid Register,Guid Member)> FamilyCashShift(HttpClient owner)
    {
        var created=await Post(owner,FamilyCashRoot,new{branchId=Slice2DemoSeed.CityBranchId,name="صندوق أسرة اصطناعية "+Guid.NewGuid().ToString("N")[..10]},await Csrf(owner),Guid.NewGuid().ToString("N"));created.EnsureSuccessStatusCode();var register=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var member=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.OwnerEmail).Select(m=>m.Id).SingleAsync();
        using var assign=new HttpRequestMessage(HttpMethod.Put,$"{FamilyCashRoot}/{register}/cashiers"){Content=JsonContent.Create(new{membershipId=member,enabled=true})};assign.Headers.Add("X-CSRF-TOKEN",await Csrf(owner));(await owner.SendAsync(assign)).EnsureSuccessStatusCode();
        var opened=await Post(owner,$"{FamilyCashRoot}/{register}/open",new{openingAmount="100"},await Csrf(owner),Guid.NewGuid().ToString("N"));opened.EnsureSuccessStatusCode();return((await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),register,member);
    }
    private async Task<CashFamilyCollect> FamilyCashBody(HttpClient owner,Guid shift,FamilyFixture f)
    {
        var r=await Post(owner,$"{FamilyCashRoot}/sessions/{shift}/family-quote",new CashFamilyQuote(f.Guardian,f.Choices),await Csrf(owner));r.EnsureSuccessStatusCode();return new(f.Guardian,f.Choices,(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()!);
    }
    private async Task<HttpResponseMessage> CashFamilySend(HttpClient owner,Guid shift,CashFamilyCollect body,string? key=null)=>await Post(owner,$"{FamilyCashRoot}/sessions/{shift}/family-collections",body,await Csrf(owner),key??Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Cash_family_posts_all_children_once_and_replays_after_close_without_double_revenue()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var settings=await FamilyCashSettings();await FamilyCashConfiguration(owner,settings with{CashEnabled=true});
        try
        {
            var shift=await FamilyCashShift(owner);var body=await FamilyCashBody(owner,shift.Session,f);var key=Guid.NewGuid().ToString("N");var results=await Task.WhenAll(CashFamilySend(owner,shift.Session,body,key),CashFamilySend(owner,shift.Session,body,key));Assert.All(results,r=>r.EnsureSuccessStatusCode());var first=await results[0].Content.ReadFromJsonAsync<JsonElement>();var again=await results[1].Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(first.GetProperty("items").ToString(),again.GetProperty("items").ToString());
            await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var ids=f.Choices.Select(l=>l.SportEnrollmentId).ToArray();Assert.Equal(2,await db.Receipts.CountAsync(r=>ids.Contains(r.SportEnrollmentId)));Assert.Equal(2,await db.SubscriptionPeriods.CountAsync(r=>ids.Contains(r.SportEnrollmentId)));var posts=await db.CashReceiptPostings.Where(p=>p.SessionId==shift.Session).ToListAsync();Assert.Equal(2,posts.Count);Assert.Single(posts.Select(p=>p.RequestHash).Distinct());Assert.Equal(Decimal(first,"amount"),posts.Sum(p=>p.Amount));
            foreach(var item in first.GetProperty("items").EnumerateArray())Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync($"/api/v1/guardian/subscriptions/receipts/{item.GetProperty("receiptId").GetGuid()}")).StatusCode);
            (await Post(owner,$"{FamilyCashRoot}/sessions/{shift.Session}/close",new{countedAmount=CashRegisterService.Money(100+posts.Sum(p=>p.Amount))},await Csrf(owner),Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();(await CashFamilySend(owner,shift.Session,body,key)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await CashFamilySend(owner,shift.Session,body with{QuoteHash=new string('A',64)},key)).StatusCode);
        }
        finally{await FamilyCashConfiguration(owner,settings);}
    }
    [Fact]
    public async Task Cash_family_changed_price_pending_request_wrong_branch_role_and_csrf_are_rejected()
    {
        await using var f=await NewFamily();await using var mixed=await NewFamily(true);using var owner=await InboxStaff(DemoSeed.OwnerEmail);using var coach=await InboxStaff(DemoSeed.CoachEmail);using var foreign=await InboxStaff(DemoSeed.FutureOwnerEmail);var settings=await FamilyCashSettings();await FamilyCashConfiguration(owner,settings with{CashEnabled=true});
        try
        {
            var shift=await FamilyCashShift(owner);var body=await FamilyCashBody(owner,shift.Session,f);
            Assert.Equal(HttpStatusCode.Forbidden,(await CashFamilySend(coach,shift.Session,body)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await CashFamilySend(f.Client,shift.Session,body)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await CashFamilySend(foreign,shift.Session,body)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync($"{FamilyCashRoot}/sessions/{shift.Session}/family-quote",new CashFamilyQuote(f.Guardian,f.Choices))).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,$"{FamilyCashRoot}/sessions/{shift.Session}/family-quote",new CashFamilyQuote(mixed.Guardian,mixed.Choices),await Csrf(owner))).StatusCode);
            await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var plan=await db.SubscriptionPlans.SingleAsync(p=>p.Id==f.Choices[0].SubscriptionPlanId);var price=plan.Price;
            try{plan.Price+=1;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await CashFamilySend(owner,shift.Session,body)).StatusCode);}finally{plan.Price=price;await db.SaveChangesAsync();}
            _=await CreateFamily(f);Assert.Equal(HttpStatusCode.Conflict,(await CashFamilySend(owner,shift.Session,body)).StatusCode);Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==shift.Session));Assert.False(await db.Receipts.AnyAsync(r=>f.Choices.Select(l=>l.SportEnrollmentId).Contains(r.SportEnrollmentId)));
        }
        finally{await FamilyCashConfiguration(owner,settings);}
    }
    [Fact]
    public async Task Cash_family_last_line_database_failure_rolls_back_all_postings_receipts_periods_and_retry_succeeds()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var settings=await FamilyCashSettings();await FamilyCashConfiguration(owner,settings with{CashEnabled=true});var shift=await FamilyCashShift(owner);var body=await FamilyCashBody(owner,shift.Session,f);var last=f.Choices.Select(l=>l.SportEnrollmentId).Order().Last();var key=Guid.NewGuid().ToString("N");var name="cash_family_fail_"+last.ToString("N");
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        try
        {
            // DDL identifiers are exclusively this test's internally generated UUID hex.
            var createTrigger=$"CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"SportEnrollmentId\"='{last}'::uuid THEN RAISE EXCEPTION 'synthetic family rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER {name} BEFORE INSERT ON \"Receipts\" FOR EACH ROW EXECUTE FUNCTION {name}();";
            await db.Database.ExecuteSqlRawAsync(createTrigger);
            Assert.Equal(HttpStatusCode.InternalServerError,(await CashFamilySend(owner,shift.Session,body,key)).StatusCode);var ids=f.Choices.Select(l=>l.SportEnrollmentId).ToArray();Assert.False(await db.Receipts.AnyAsync(r=>ids.Contains(r.SportEnrollmentId)));Assert.False(await db.SubscriptionPeriods.AnyAsync(r=>ids.Contains(r.SportEnrollmentId)));Assert.False(await db.Collections.AnyAsync(r=>ids.Contains(r.SportEnrollmentId)));Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==shift.Session));
        }
        finally{var dropTrigger=$"DROP TRIGGER IF EXISTS {name} ON \"Receipts\"; DROP FUNCTION IF EXISTS {name}();";await db.Database.ExecuteSqlRawAsync(dropTrigger);await FamilyCashConfiguration(owner,settings);}
        await FamilyCashConfiguration(owner,settings with{CashEnabled=true});try{(await CashFamilySend(owner,shift.Session,body,key)).EnsureSuccessStatusCode();}finally{await FamilyCashConfiguration(owner,settings);}
    }
    [Fact]
    public async Task Cash_family_full_waiver_and_busy_enrollment_never_create_partial_or_zero_cash_postings()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var settings=await FamilyCashSettings();await FamilyCashConfiguration(owner,settings with{CashEnabled=true});
        try
        {
            var shift=await FamilyCashShift(owner);var body=await FamilyCashBody(owner,shift.Session,f);var key=Guid.NewGuid().ToString("N");await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();
            await using(var tx=await db.Database.BeginTransactionAsync())
            {
                var e=f.Choices[1].SportEnrollmentId;await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"renewal-enrollment:{DemoSeed.NogoomAcademyId:N}:{e:N}"},0))");
                Assert.Equal(HttpStatusCode.Conflict,(await CashFamilySend(owner,shift.Session,body,key)).StatusCode);Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==shift.Session));await tx.RollbackAsync();
            }
            var rule=new DiscountRule{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName="إعفاء نقدي اصطناعي "+f.Guardian.ToString("N"),Percentage=100,Reason="اختبار منع القبض الصفري"};db.DiscountRules.Add(rule);
            foreach(var player in await db.SportEnrollments.Where(e=>f.Choices.Select(l=>l.SportEnrollmentId).Contains(e.Id)).Select(e=>e.PlayerId).ToArrayAsync())db.PlayerDiscountLinks.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,PlayerId=player,DiscountRuleId=rule.Id});await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,$"{FamilyCashRoot}/sessions/{shift.Session}/family-quote",new CashFamilyQuote(f.Guardian,f.Choices),await Csrf(owner))).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await CashFamilySend(owner,shift.Session,body,key)).StatusCode);Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==shift.Session));
        }
        finally{await FamilyCashConfiguration(owner,settings);}
    }
    [Fact]
    public async Task Cash_family_scoped_financial_administrator_collects_only_from_own_shift_and_explicit_add_grants()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var settings=await FamilyCashSettings();await FamilyCashConfiguration(owner,settings with{CashEnabled=true});
        try
        {
            var email=$"cash.family.{Guid.NewGuid():N}@example.test";var saved=await Post(owner,"/api/v1/manage/staff",new StaffWrite("أمين أسرة اصطناعي",email,"010"+Random.Shared.Next(10000000,99999999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"اختبار"),await Csrf(owner));saved.EnsureSuccessStatusCode();var member=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var grants=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m is "players" or "subscriptions" or "payments" or "finance",m is "finance" or "subscriptions",false,false)).ToArray();
            async Task Access(){using var r=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new StaffAccessWrite(false,[Slice2DemoSeed.CityBranchId],grants))};r.Headers.Add("X-CSRF-TOKEN",await Csrf(owner));(await owner.SendAsync(r)).EnsureSuccessStatusCode();}
            await Access();using var admin=await InboxStaff(email);var shift=await FamilyCashShift(owner);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{FamilyCashRoot}/sessions/{shift.Session}/family-quote",new CashFamilyQuote(f.Guardian,f.Choices),await Csrf(admin))).StatusCode);
            (await Post(owner,$"{FamilyCashRoot}/sessions/{shift.Session}/close",new{countedAmount="100"},await Csrf(owner),Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();using(var a=new HttpRequestMessage(HttpMethod.Put,$"{FamilyCashRoot}/{shift.Register}/cashiers"){Content=JsonContent.Create(new{membershipId=member,enabled=true})}){a.Headers.Add("X-CSRF-TOKEN",await Csrf(owner));(await owner.SendAsync(a)).EnsureSuccessStatusCode();}
            var opened=await Post(admin,$"{FamilyCashRoot}/{shift.Register}/open",new{openingAmount="0"},await Csrf(admin),Guid.NewGuid().ToString("N"));opened.EnsureSuccessStatusCode();var own=(await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var body=await FamilyCashBody(admin,own,f);
            grants=grants.Select(g=>g.Module=="finance"?g with{Add=false}:g).ToArray();await Access();Assert.Equal(HttpStatusCode.Forbidden,(await CashFamilySend(admin,own,body)).StatusCode);grants=grants.Select(g=>g.Module=="finance"?g with{Add=true}:g).ToArray();await Access();(await CashFamilySend(admin,own,body)).EnsureSuccessStatusCode();
        }
        finally{await FamilyCashConfiguration(owner,settings);}
    }
}
