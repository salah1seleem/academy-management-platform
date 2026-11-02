using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact] public async Task V4_golden_110000_receipts_minus_69000_actual_costs_reconcile_to_41000_without_double_salary_or_external_netting()
    {
        var f=await PrivateFixture(isolatedBranch:true);using var owner=f.Owner;
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var academy=DemoSeed.NogoomAcademyId;var now=DateTimeOffset.UtcNow;var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        var config=await db.AcademyConfigurationVersions.SingleAsync(v=>v.AcademyId==academy&&v.State==ConfigurationState.Active);var original=config.Payload;var originalHash=config.ContentHash;
        var licenses=await db.AcademyFeatureEntitlements.Where(l=>l.AcademyId==academy&&new[]{"external_partners","facilities"}.Contains(l.Feature)).ToArrayAsync();var originalLicenses=licenses.ToDictionary(l=>l.Id,l=>l.Enabled);
        try
        {
            var settings=AcademyRuntimeSettings.Read(config.Payload);settings.Features["external_partners"]=true;settings.Features["facilities"]=true;config.Payload=settings.Serialize();config.ContentHash=AcademyConfigurationService.Hash(config.Payload);foreach(var l in licenses)l.Enabled=true;await db.SaveChangesAsync();
            // No hardcoded report totals: all figures below are persisted source transactions in this isolated branch.
            var plan=await db.SubscriptionPlans.SingleAsync(p=>p.Id==f.Plan);plan.Price=18000;plan.SessionCount=1;plan.Revision++;await db.SaveChangesAsync();
            var charge=await PrivateFinancialWrite(owner,"/api/v1/finance/receivables/",new ReceivableEndpoints.ChargeWrite(f.Enrollment,f.Plan,18000,f.Today,ReceivableSource.ManualCharge,"مقابل برنامج خاص اصطناعي"));charge.EnsureSuccessStatusCode();var document=(await charge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var package=await Write(owner,PrivateRoot(f.Coach)+"/packages",new PrivatePackageWrite(document,1),false);package.EnsureSuccessStatusCode();var packageId=(await package.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await Write(owner,$"{PrivateRoot(f.Coach)}/{packageId}/deliveries",new PrivateDeliveryWrite(f.Sessions[0],1),false)).EnsureSuccessStatusCode();
            var collect=scope.ServiceProvider.GetRequiredService<CollectionService>();
            await using(var tx=await db.Database.BeginTransactionAsync())
            {
                var payment=await collect.CreateCashAsync(academy,f.Actor,f.Enrollment,f.Plan,"v4-golden-private-"+f.Branch,"Online");var collection=await db.Collections.Where(c=>c.PaymentRequestId==payment).Select(c=>c.Id).SingleAsync();await tx.CommitAsync();
                var allocation=await PrivateFinancialWrite(owner,$"/api/v1/finance/receivables/{document}/movements",new{kind="PaymentAllocation",amount=18000m,collectionId=collection,reason="تخصيص تحصيل البرايفت الاصطناعي"});allocation.EnsureSuccessStatusCode();
            }
            var path=(await PrivateRows(owner,f.Coach)).Single(r=>r.GetProperty("documentId").GetGuid()==document);var unit=$"{PrivateRoot(f.Coach)}/{path.GetProperty("id").GetGuid()}";
            (await PrivatePost(owner,unit,await PrivatePreview(owner,unit))).EnsureSuccessStatusCode();Assert.Equal(7200m,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("privateCompensation").GetDecimal());
            var salary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/employees/{f.Employee}/salary-versions");
            (await PrivateFinancialWrite(owner,$"/api/v1/manage/employees/{f.Employee}/salary-versions/",new SalaryChange(month,32000,salary.GetProperty("revision").GetInt32(),salary.GetProperty("currentSalary").GetDecimal(),"راتب سيناريو اصطناعي"))).EnsureSuccessStatusCode();
            (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();(await StatementPay(owner,f.Employee,month,39200,0,39200)).EnsureSuccessStatusCode();
            // A second actual group subscription, not a fake external counterparty standing in for a player.
            var player=Guid.NewGuid();var enrollment=Guid.NewGuid();var groupPlan=Guid.NewGuid();db.Players.Add(new(){Id=player,AcademyId=academy,ArabicName="طفل اشتراك إجمالي اصطناعي",PlayerCode="V4G-"+player.ToString("N")[..10],DateOfBirth=new(2016,1,1),CreatedAtUtc=now,UpdatedAtUtc=now});
            db.SportEnrollments.Add(new(){Id=enrollment,AcademyId=academy,PlayerId=player,SportId=f.Sport,BranchId=f.Branch,RegistrationFormat=TrainingRegistrationFormat.Group,CreatedAtUtc=now,UpdatedAtUtc=now});
            db.SubscriptionPlans.Add(new(){Id=groupPlan,AcademyId=academy,SportId=f.Sport,ArabicName="اشتراكات نموذج قبول "+groupPlan.ToString("N")[..8],PlanType=SubscriptionPlanType.Duration,TrainingFormat=TrainingFormat.Group,Price=80000,Currency="EGP",DurationMonths=1,Revision=1,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
            await using(var tx=await db.Database.BeginTransactionAsync()){await collect.CreateCashAsync(academy,f.Actor,enrollment,groupPlan,"v4-golden-group-"+f.Branch,"Online");await tx.CommitAsync();}
            var swim=Guid.NewGuid();db.Sports.Add(new(){Id=swim,AcademyId=academy,ArabicName="سباحة نموذج القبول "+swim.ToString("N")[..8],Kind=Academy.Infrastructure.Structure.SportKind.Swimming,CreatedAtUtc=now,UpdatedAtUtc=now});db.SwimmingBranchPolicies.Add(new(){Id=Guid.NewGuid(),AcademyId=academy,SportId=swim,BranchId=f.Branch,Enabled=true,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
            async Task<Guid> Created(string url,object body){var r=await Write(owner,url,body,false);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
            var facility=await Created("/api/v1/facilities/",new FacilityWrite("مسبح نموذج قبول",f.Branch));var space=await Created("/api/v1/facilities/spaces",new SpaceWrite("مسبح اصطناعي",facility,swim,FacilitySpaceKind.Pool));var resource=await Created("/api/v1/facilities/resources",new ResourceWrite("A",space,8,false,[0,1,2,3,4,5,6],new(8,0),new(22,0),["FacilityBooking"]));
            (await Write(owner,$"/api/v1/facilities/resources/{resource}/approve",new CapacityApproval("مراجعة سعة اصطناعية للاختبار فقط",true),false)).EnsureSuccessStatusCode();
            var rental=await Created("/api/v1/finance/external-agreements/",new ExternalAgreementWrite(Guid.NewGuid(),"تأجير قبول مالي",ExternalAgreementKind.PoolRental,f.Branch,swim,f.Today,f.Today,f.Today,12000,"جهة تأجير اصطناعية",f.Employee,Slots:[new(resource,f.Today,new(9,0),new(10,0),8,true)]));
            (await Write(owner,$"/api/v1/finance/external-agreements/{rental}/receipts",new ExternalReceiptWrite(Guid.NewGuid(),12000,"Online",true),false)).EnsureSuccessStatusCode();
            foreach(var (category,amount) in new[]{("Rent",20000m),("Electricity",4000m),("Water",2000m),("Maintenance",3000m),("CollectionFees",800m)})
            {
                var expense=await PrivateFinancialWrite(owner,"/api/v1/finance/operating-expenses/",new OperatingExpenseWrite(f.Branch,OperatingExpenseScope.Branch,f.Branch,category,month,f.Today,f.Today,amount,"EGP","تكلفة قبول اصطناعية"));expense.EnsureSuccessStatusCode();var id=(await expense.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await Write(owner,$"/api/v1/finance/operating-expenses/{id}/approve",new{},false)).EnsureSuccessStatusCode();var quote=await Write(owner,$"/api/v1/finance/operating-expenses/{id}/quote",new{},false);quote.EnsureSuccessStatusCode();(await PrivateFinancialWrite(owner,$"/api/v1/finance/operating-expenses/{id}/post",new OperatingExpensePost((await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()!))).EnsureSuccessStatusCode();
            }
            var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/operating-statement?allDates=true&branchId={f.Branch}");
            decimal Value(string section,string key)=>report.GetProperty(section).EnumerateArray().Single(x=>x.GetProperty("key").GetString()==key).GetProperty("amount").GetDecimal();
            Assert.Equal(80000m,Value("income","subscription"));Assert.Equal(18000m,Value("income","private"));Assert.Equal(12000m,Value("income","rental"));Assert.Equal(32000m,Value("expenses","salary"));Assert.Equal(7200m,Value("expenses","commission"));Assert.Equal(110000m,report.GetProperty("totalIncome").GetDecimal());Assert.Equal(69000m,report.GetProperty("totalExpenses").GetDecimal());Assert.Equal(41000m,report.GetProperty("surplus").GetDecimal());
            Assert.Equal(39200m,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("netDue").GetDecimal());Assert.Equal(110000m,(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&branchId={f.Branch}")).GetProperty("netAmount").GetDecimal());
            Console.WriteLine($"V4 golden synthetic branch={f.Branch}; employee={f.Employee}; external={rental}; 110000-69000=41000; basis=Cash; actual persisted sources");
        }
        finally{db.ChangeTracker.Clear();var restore=await db.AcademyConfigurationVersions.SingleAsync(v=>v.Id==config.Id);restore.Payload=original;restore.ContentHash=originalHash;foreach(var l in await db.AcademyFeatureEntitlements.Where(l=>l.AcademyId==academy&&new[]{"external_partners","facilities"}.Contains(l.Feature)).ToArrayAsync())l.Enabled=originalLicenses[l.Id];await db.SaveChangesAsync();}
    }
}
