using System.Text.Json;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;

namespace Academy.Api.Finance;

public sealed record CrossBranchCashSearch(string Search);
public sealed record CrossBranchCashQuote(string Reference,Guid PlanId);
public sealed record CrossBranchCashWrite(string Reference,Guid PlanId,string QuoteHash);
public sealed record CrossBranchFamilySearch(string Search);
public sealed record CashFamilyReferenceRequest(string Reference);
public sealed record CrossBranchFamilyQuote(string Reference,IReadOnlyList<FamilyRenewalChoice>? Lines);
public sealed record CrossBranchFamilyWrite(string Reference,IReadOnlyList<FamilyRenewalChoice>? Lines,string QuoteHash);

public sealed class CrossBranchCashPayments(CashRegisterService authority,IServiceScopeFactory scopes,CashPaymentReferences references)
{
    private static readonly JsonSerializerOptions Json=JsonSerializerOptions.Web;
    // This private context is disposed with the operation; the HTTP context is
    // never rebound/widened. Its tenant remains fixed and writes are purpose-limited.
    private AsyncServiceScope PaymentScope(CashPaymentActor actor,Guid? enrollment=null,Guid? plan=null)
    {
        var scope=scopes.CreateAsyncScope();
        try
        {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            db.BindRequestScope(actor.Tenant.AcademyId,actor.User,actor.Tenant.MembershipId,null);
            db.LimitToCashPayment(actor.Session,actor.CollectingBranch,enrollment,plan);
            return scope;
        }
        catch{scope.Dispose();throw;}
    }
    private AsyncServiceScope FamilyPaymentScope(CashPaymentActor actor,Guid guardian,IReadOnlyList<FamilyRenewalChoice>? lines)
    {
        if(lines is not {Count:>0 and <=20}||lines.Any(x=>x is null||x.SportEnrollmentId==Guid.Empty||x.SubscriptionPlanId==Guid.Empty)||lines.Select(x=>x.SportEnrollmentId).Distinct().Count()!=lines.Count)throw new CashRejected("اختر تسجيلات الأسرة وباقة واحدة لكل تسجيل.",422);
        var scope=scopes.CreateAsyncScope();
        try
        {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            db.BindRequestScope(actor.Tenant.AcademyId,actor.User,actor.Tenant.MembershipId,null);
            db.LimitToCashFamilyPayment(actor.Session,actor.CollectingBranch,guardian,lines.ToDictionary(x=>x.SportEnrollmentId,x=>x.SubscriptionPlanId));
            return scope;
        }
        catch{scope.Dispose();throw;}
    }
    public async Task<object> Search(Guid session,CrossBranchCashSearch body)
    {
        var actor=await authority.AuthorizeExternalPayment(session);
        if(string.IsNullOrWhiteSpace(body.Search)||body.Search.Trim().Length is <2 or >100)throw new CashRejected("اكتب اسم اللاعب أو كوده، من حرفين إلى100حرف.",422);
        await using var scope=PaymentScope(actor);
        var result=JsonSerializer.SerializeToElement(await scope.ServiceProvider.GetRequiredService<CashRegisterService>().ExternalCollectionOptions(session,body.Search,actor),Json);
        var items=result.GetProperty("items").EnumerateArray().Select(item=>new{
            enrollmentId=item.GetProperty("enrollmentId").GetGuid(),player=item.GetProperty("player").GetString(),code=item.GetProperty("code").GetString(),sport=item.GetProperty("sport").GetString(),
            enrollmentBranch=item.GetProperty("enrollmentBranch").GetString(),plans=item.GetProperty("plans").Clone(),paymentReference=references.Create(actor,item.GetProperty("enrollmentId").GetGuid())}).ToArray();
        return new{items,crossBranch=true,collectionBranch=result.GetProperty("collectionBranch").GetString(),purpose="PaymentOnly"};
    }
    public async Task<object> Quote(Guid session,CrossBranchCashQuote body)
    {
        var actor=await authority.AuthorizeExternalPayment(session);var reference=references.Resolve(body.Reference,actor,true);
        await using var scope=PaymentScope(actor);
        return await scope.ServiceProvider.GetRequiredService<CashRegisterService>().ExternalCollectionQuote(session,new(reference.Enrollment,body.PlanId),scope.ServiceProvider.GetRequiredService<DiscountRules>(),reference);
    }
    public async Task<object> Collect(Guid session,CrossBranchCashWrite body,string key)
    {
        var actor=await authority.AuthorizeExternalPayment(session,replay:true);var reference=references.Resolve(body.Reference,actor,false);
        await using var scope=PaymentScope(actor,reference.Enrollment,body.PlanId);
        return await scope.ServiceProvider.GetRequiredService<CashRegisterService>().CollectExternal(session,new(reference.Enrollment,body.PlanId,body.QuoteHash),key,
            scope.ServiceProvider.GetRequiredService<DiscountRules>(),scope.ServiceProvider.GetRequiredService<CollectionService>(),reference);
    }
    public async Task<object> SearchFamilies(Guid session,CrossBranchFamilySearch body)
    {
        var actor=await authority.AuthorizeExternalPayment(session);
        if(string.IsNullOrWhiteSpace(body.Search)||body.Search.Trim().Length is <3 or >100)throw new CashRejected("اكتب اسم ولي الأمر أو هاتفه من ثلاثة أحرف على الأقل.",422);
        await using var scope=PaymentScope(actor);
        var result=JsonSerializer.SerializeToElement(await scope.ServiceProvider.GetRequiredService<FamilyRenewalQuoteService>().StaffFamilies(body.Search),Json);
        var items=result.GetProperty("items").EnumerateArray().Select(item=>new{
            name=item.GetProperty("name").GetString(),phoneHint=Mask(item.GetProperty("phone").GetString()),reference=references.CreateFamily(actor,item.GetProperty("id").GetGuid())}).ToArray();
        return new{items,purpose="PaymentOnly"};
    }
    private static string Mask(string? phone)=>phone is {Length:>=4} ? "•••"+phone[^4..] : "•••";
    public async Task<object> FamilyOptions(Guid session,string reference)
    {
        var actor=await authority.AuthorizeExternalPayment(session);var family=references.ResolveFamily(reference,actor,true);
        await using var scope=PaymentScope(actor);
        return await scope.ServiceProvider.GetRequiredService<FamilyRenewalQuoteService>().StaffOptions(family.Guardian);
    }
    public async Task<object> FamilyQuote(Guid session,CrossBranchFamilyQuote body)
    {
        var actor=await authority.AuthorizeExternalPayment(session);var family=references.ResolveFamily(body.Reference,actor,true);
        await using var scope=PaymentScope(actor);
        return await scope.ServiceProvider.GetRequiredService<CashRegisterService>().ExternalFamilyQuote(session,new(family.Guardian,body.Lines),scope.ServiceProvider.GetRequiredService<FamilyRenewalQuoteService>(),family);
    }
    public async Task<object> FamilyCollect(Guid session,CrossBranchFamilyWrite body,string key)
    {
        var actor=await authority.AuthorizeExternalPayment(session,replay:true);var family=references.ResolveFamily(body.Reference,actor,false);
        await using var scope=FamilyPaymentScope(actor,family.Guardian,body.Lines);
        return await scope.ServiceProvider.GetRequiredService<CashRegisterService>().CollectExternalFamily(session,new(family.Guardian,body.Lines,body.QuoteHash),key,
            scope.ServiceProvider.GetRequiredService<FamilyRenewalQuoteService>(),scope.ServiceProvider.GetRequiredService<CollectionService>(),family);
    }
}
