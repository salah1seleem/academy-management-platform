using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CashRegisterWrite(Guid BranchId, string Name);
public sealed record CashierWrite(Guid MembershipId, bool Enabled);
public sealed record CashOpenWrite([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal OpeningAmount);
public sealed record CashCloseWrite([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal CountedAmount, string? Reason);
public sealed record CashTransferWrite(Guid FromSessionId, Guid ToSessionId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Reason);
public sealed class CashRejected(string message, int status = 409) : Exception(message) { public int Status { get; } = status; }

public static class CashRegisterEndpoints
{
    public static void MapCashRegisters(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/cash-registers").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/", async (string? search, int? offset, CashRegisterService service) => await Run(() => service.Read(search, offset ?? 0)));
        api.MapGet("/options", async (CashRegisterService service) => await Run(service.Options));
        api.MapGet("/receipts/{id:guid}/void-options", async (Guid id, CashRegisterService service) => await Run(()=>service.VoidOptions(id)));
        api.MapPost("/receipts/{id:guid}/void-quote", async (Guid id, CashReceiptVoidWrite body, CashRegisterService service) => await Run(()=>service.VoidQuote(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/receipts/{id:guid}/void", async (Guid id, CashReceiptVoidWrite body, HttpRequest request, CashRegisterService service) => await Run(()=>service.VoidReceipt(id,body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/sessions/{id:guid}", async (Guid id, CashRegisterService service) => await Run(()=>service.Detail(id)));
        api.MapGet("/sessions/{id:guid}/collection-options", async (Guid id, string? search, CashRegisterService service) => await Run(()=>service.CollectionOptions(id,search)));
        api.MapPost("/sessions/{id:guid}/collection-result", async (Guid id,CashCollectionResultRequest body,CashRegisterService service,CashPaymentReferences references)=>await Run(()=>service.CollectionResult(id,body,references))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/family-collection-result", async (Guid id,CashFamilyResultRequest body,CashRegisterService service,CashPaymentReferences references)=>await Run(()=>service.FamilyCollectionResult(id,body,references))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/search", async (Guid id,CrossBranchCashSearch body,CrossBranchCashPayments service)=>await Run(()=>service.Search(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/quote", async (Guid id,CrossBranchCashQuote body,CrossBranchCashPayments service)=>await Run(()=>service.Quote(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/collect", async (Guid id,CrossBranchCashWrite body,HttpRequest request,CrossBranchCashPayments service)=>await Run(()=>service.Collect(id,body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/families/search", async (Guid id,CrossBranchFamilySearch body,CrossBranchCashPayments service)=>await Run(()=>service.SearchFamilies(id,body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("renewal-search");
        api.MapPost("/sessions/{id:guid}/cross-branch/families/options", async (Guid id,CashFamilyReferenceRequest body,CrossBranchCashPayments service)=>await Run(()=>service.FamilyOptions(id,body.Reference))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/families/quote", async (Guid id,CrossBranchFamilyQuote body,CrossBranchCashPayments service)=>await Run(()=>service.FamilyQuote(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/cross-branch/families/collect", async (Guid id,CrossBranchFamilyWrite body,HttpRequest request,CrossBranchCashPayments service)=>await Run(()=>service.FamilyCollect(id,body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/family-quote", async (Guid id, CashFamilyQuote body, CashRegisterService service, Academy.Api.Slice3.FamilyRenewalQuoteService family) => await Run(()=>service.FamilyQuote(id,body,family))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/family-collections", async (Guid id, CashFamilyCollect body, HttpRequest request, CashRegisterService service, Academy.Api.Slice3.FamilyRenewalQuoteService family, Academy.Api.Operations.CollectionService collections) => await Run(()=>service.CollectFamily(id,body,Key(request),family,collections))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/collection-quote", async (Guid id, CashCollectionQuote body, CashRegisterService service, Academy.Api.Operations.DiscountRules discounts) => await Run(()=>service.CollectionQuote(id,body,discounts))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/collections", async (Guid id, CashCollectionWrite body, HttpRequest request, CashRegisterService service, Academy.Api.Operations.DiscountRules discounts, Academy.Api.Operations.CollectionService collections) => await Run(()=>service.Collect(id,body,Key(request),discounts,collections))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/", async (CashRegisterWrite body, HttpRequest request, CashRegisterService service) => await Run(()=>service.Create(body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/cashiers", async (Guid id, CashierWrite body, CashRegisterService service) => await Run(()=>service.Assign(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/open", async (Guid id, CashOpenWrite body, HttpRequest request, CashRegisterService service) => await Run(()=>service.Open(id,body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/sessions/{id:guid}/close", async (Guid id, CashCloseWrite body, HttpRequest request, CashRegisterService service) => await Run(()=>service.Close(id,body,Key(request)))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/transfers", async (CashTransferWrite body, HttpRequest request, CashRegisterService service) => await Run(()=>service.Transfer(body,Key(request)))).AddEndpointFilter<CsrfFilter>();
    }
    private static string Key(HttpRequest request) => request.Headers["Idempotency-Key"].ToString();
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { return Results.Ok(await work()); }
        catch(CashRejected e) { return Results.Problem(statusCode:e.Status,title:e.Message); }
        catch(ResourceScopeException) { return Results.Problem(statusCode:403,title:"هذه العملية خارج نطاق الدفع المصرح به."); }
        catch(DbUpdateConcurrencyException) { return Results.Problem(statusCode:409,title:"تغير السجل أثناء التنفيذ. حدّث المراجعة قبل التأكيد."); }
        catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException {SqlState:"23505"}) { return Results.Problem(statusCode:409,title:"الصندوق أو الوردية مسجل بالفعل. حدّث القائمة وراجع العملية السابقة."); }
    }
}

public sealed partial class CashRegisterService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, AcademyConfigurationService config, TimeProvider time, Academy.Api.Slice3.ISubscriptionClock subscriptionClock)
{
    private async Task<(TenantMembership Tenant, Guid User)> Context(bool owner = false, bool write = false)
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.",401);
        if(t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || owner && t.Role!=AcademyRole.AcademyOwner) throw new CashRejected("هذه العملية للمالك المخول فقط.",403);
        if(write && t.Role==AcademyRole.AcademyAdmin && (!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants) || !await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="finance"&&g.IsActive&&g.Read&&g.Add))) throw new CashRejected("تحتاج صلاحية إضافة مالية صريحة.",403);
        return(t,Guid.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
    }
    public static string Money(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
    private static string? Reason(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static void Amount(decimal value, bool zero = true) { if(value < 0 || !zero&&value==0 || value>999999999m || decimal.Round(value,2)!=value) throw new CashRejected("أدخل مبلغًا صحيحًا بحد أقصى منزلتين عشريتين.",422); }
    private static void ReasonLimit(string? value) { if(value?.Length>400) throw new CashRejected("السبب لا يتجاوز400حرف.",422); }
    private static void Same(string stored,string hash) { if(stored!=hash) throw new CashRejected("معرف العملية مستخدم بتفاصيل أخرى."); }
    private Task Lock(Guid academy,string resource)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"cash:{academy}:{resource}"},0))");
    private async Task<string> Request(Guid academy,Guid user,string operation,string key)
    {
        if(key.Length is <8 or >100 || string.IsNullOrWhiteSpace(key)) throw new CashRejected("أعد إرسال العملية بمعرف صحيح.",422);
        var full=$"{user:N}:{operation}:{key}"; await Lock(academy,$"request:{full}"); return full;
    }
    private async Task Enabled(Guid academy) { if((await config.Active(academy))?.CashEnabled!=true) throw new CashRejected("النقدي غير مفعل. يجهز المالك الصندوق والصلاحيات ثم يفعل النقدي من إعدادات الأكاديمية."); }
    private async Task<CashRegister> Register(Guid id) => await db.CashRegisters.SingleOrDefaultAsync(r=>r.Id==id) ?? throw new CashRejected("الصندوق غير متاح.",404);
    private async Task<CashSession> Session(Guid id) => await db.CashSessions.SingleOrDefaultAsync(s=>s.Id==id) ?? throw new CashRejected("الوردية غير متاحة.",404);
    private async Task Usable(CashRegister register)
    {
        if(!register.IsActive || !await db.Branches.AnyAsync(b=>b.Id==register.BranchId&&b.IsActive)) throw new CashRejected("الصندوق أو فرعه متوقف.");
    }
    private async Task Cashier(CashRegister register,TenantMembership t)
    {
        if(!await db.CashierAssignments.AnyAsync(a=>a.RegisterId==register.Id&&a.MembershipId==t.MembershipId&&a.IsActive)) throw new CashRejected("لم يعيّنك المالك أمينًا لهذا الصندوق.",403);
    }
    private async Task<decimal> Balance(CashSession session) => session.OpeningAmount+(await db.CashMovements.Where(m=>m.SessionId==session.Id).SumAsync(m=>(decimal?)m.Amount)??0)+(await db.CashReceiptPostings.Where(m=>m.SessionId==session.Id).SumAsync(m=>(decimal?)m.Amount)??0)+(await db.CashProductPostings.Where(m=>m.SessionId==session.Id).SumAsync(m=>(decimal?)m.Amount)??0)-(await db.CashRefundPostings.Where(m=>m.SessionId==session.Id).SumAsync(m=>(decimal?)m.Amount)??0);
    public async Task<object> Options()
    {
        var (t,_)=await Context();
        return new { isOwner=t.Role==AcademyRole.AcademyOwner, branches=await db.Branches.Where(b=>b.IsActive).Select(b=>new{b.Id,name=b.ArabicName}).ToListAsync(), members=t.Role==AcademyRole.AcademyOwner ? await db.AcademyMemberships.Where(m=>m.IsActive&&m.User.IsActive&&(m.Role==AcademyRole.AcademyOwner||m.Role==AcademyRole.AcademyAdmin)).Select(m=>new{m.Id,name=m.User.DisplayName}).ToListAsync() : [] };
    }
    public async Task<object> Read(string? search = null, int offset = 0)
    {
        var (t,_)=await Context();
        search = search?.Trim();
        if (search?.Length > 100 || offset < 0) throw new CashRejected("البحث حتى100حرف ورقم الصفحة غير سالب.", 422);
        var query = db.CashRegisters.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(r => r.Name.Contains(search));
        const int pageSize = 100;
        var total = await query.CountAsync();
        var registers=await query.OrderByDescending(r=>r.CreatedAtUtc).ThenBy(r=>r.Id).Skip(offset).Take(pageSize).ToListAsync();
        var ids=registers.Select(r=>r.Id).ToArray();
        var sessions=await db.CashSessions.AsNoTracking().Where(s=>ids.Contains(s.RegisterId)&&s.ClosedAtUtc==null).ToListAsync();
        var sessionIds=sessions.Select(s=>s.Id).ToArray();
        var balances=await db.CashMovements.Where(m=>sessionIds.Contains(m.SessionId)).GroupBy(m=>m.SessionId).Select(g=>new{id=g.Key,amount=g.Sum(m=>m.Amount)}).ToDictionaryAsync(g=>g.id,g=>g.amount);
        var collected=await db.CashReceiptPostings.Where(m=>sessionIds.Contains(m.SessionId)).GroupBy(m=>m.SessionId).Select(g=>new{id=g.Key,amount=g.Sum(m=>m.Amount)}).ToListAsync();
        foreach(var row in collected) balances[row.id]=balances.GetValueOrDefault(row.id)+row.amount;
        var productCash=await db.CashProductPostings.Where(m=>sessionIds.Contains(m.SessionId)).GroupBy(m=>m.SessionId).Select(g=>new{id=g.Key,amount=g.Sum(m=>m.Amount)}).ToListAsync();
        foreach(var row in productCash) balances[row.id]=balances.GetValueOrDefault(row.id)+row.amount;
        var refunds=await db.CashRefundPostings.Where(m=>sessionIds.Contains(m.SessionId)).GroupBy(m=>m.SessionId).Select(g=>new{id=g.Key,amount=g.Sum(m=>m.Amount)}).ToListAsync();
        foreach(var row in refunds) balances[row.id]=balances.GetValueOrDefault(row.id)-row.amount;
        var branchIds=registers.Select(r=>r.BranchId).Distinct().ToArray();
        var branches=await db.Branches.Where(b=>branchIds.Contains(b.Id)).ToDictionaryAsync(b=>b.Id,b=>b.ArabicName);
        var cashiers=await db.CashierAssignments.Where(a=>ids.Contains(a.RegisterId)).Join(db.AcademyMemberships,a=>a.MembershipId,m=>m.Id,(a,m)=>new{a.Id,a.RegisterId,a.MembershipId,a.IsActive,name=m.User.DisplayName}).ToListAsync();
        var history=await db.CashSessions.AsNoTracking().Where(s=>ids.Contains(s.RegisterId)&&s.ClosedAtUtc!=null).OrderByDescending(s=>s.ClosedAtUtc).Take(30).Select(s=>new{s.Id,s.RegisterId,s.ClosedAtUtc,s.CountedAmount,s.ExpectedAmount}).ToListAsync();
        return new {total,offset,pageSize,cashEnabled=(await config.Active(t.AcademyId))?.CashEnabled==true,history,items=registers.Select(r=>new{r.Id,r.Name,r.BranchId,branch=branches.GetValueOrDefault(r.BranchId),r.Currency,r.IsActive,cashiers=cashiers.Where(a=>a.RegisterId==r.Id),canOpen=cashiers.Any(a=>a.RegisterId==r.Id&&a.MembershipId==t.MembershipId&&a.IsActive),session=sessions.Where(s=>s.RegisterId==r.Id).Select(s=>new{s.Id,s.OpenedAtUtc,s.OpenedByMembershipId,opening=Money(s.OpeningAmount),balance=Money(s.OpeningAmount+balances.GetValueOrDefault(s.Id)),canClose=t.Role==AcademyRole.AcademyOwner||s.OpenedByMembershipId==t.MembershipId&&cashiers.Any(a=>a.RegisterId==r.Id&&a.MembershipId==t.MembershipId&&a.IsActive)}).SingleOrDefault()})};
    }
    public async Task<object> Detail(Guid id)
    {
        var (t,_)=await Context(); var session=await Session(id); var register=await Register(session.RegisterId);
        var canClose=t.Role==AcademyRole.AcademyOwner||session.OpenedByMembershipId==t.MembershipId&&await db.CashierAssignments.AnyAsync(a=>a.RegisterId==register.Id&&a.MembershipId==t.MembershipId&&a.IsActive);
        var canCollect= !session.ClosedAtUtc.HasValue && session.OpenedByMembershipId==t.MembershipId && register.IsActive && (await config.Active(t.AcademyId))?.CashEnabled==true && await db.CashierAssignments.AnyAsync(a=>a.RegisterId==register.Id&&a.MembershipId==t.MembershipId&&a.IsActive) && await CollectionPermissions(t,false);
        var canCrossBranchPayment=canCollect&&t.Role==AcademyRole.AcademyAdmin&&await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="payments"&&g.IsActive&&g.Read&&g.Add);
        // Session/register have ALREADY been authorized in normal branch scope.
        // An exact-session financial projection may read receipt snapshots without
        // acquiring operational access to a foreign child or group.
        var receipts=await (from p in db.CashReceiptPostings
            join r in db.Receipts.IgnoreQueryFilters(["trusted-branches"]).Where(r=>r.AcademyId==t.AcademyId) on p.ReceiptId equals r.Id
            where p.AcademyId==t.AcademyId&&p.SessionId==session.Id
            orderby p.CreatedAtUtc descending
            select new{p.Id,p.ReceiptId,amount=Money(p.Amount),p.Currency,p.CreatedAtUtc,r.ReceiptNumber,r.PlayerNameSnapshot,r.PlanNameSnapshot}).Take(100).ToListAsync();
        var payouts=await(from p in db.CashRefundPostings
            join refund in db.CollectionRefunds.IgnoreQueryFilters(["trusted-branches"]).Where(r=>r.AcademyId==t.AcademyId) on p.RefundId equals refund.Id
            where p.AcademyId==t.AcademyId&&p.SessionId==session.Id
            orderby p.CreatedAtUtc descending
            select new{p.Id,p.RefundId,refund.PlayerId,amount=Money(p.Amount),p.Currency,p.CreatedAtUtc,refund.Reason,
                receiptId=db.Receipts.Where(r=>r.AcademyId==t.AcademyId&&r.CollectionId==refund.CollectionId).Select(r=>r.Id).Single(),
                voidReceiptId=db.CashReceiptVoids.Where(v=>v.AcademyId==t.AcademyId&&v.RefundId==p.RefundId).Select(v=>(Guid?)v.ReceiptId).SingleOrDefault()}).Take(100).ToListAsync();
        var payoutPlayers=payouts.Select(p=>p.PlayerId).Distinct().ToArray();
        var visiblePlayers=await db.Players.Where(p=>payoutPlayers.Contains(p.Id)).Select(p=>p.Id).ToListAsync();
        var refunds=payouts.Select(p=>new{p.Id,p.RefundId,p.PlayerId,p.amount,p.Currency,p.CreatedAtUtc,p.Reason,p.receiptId,p.voidReceiptId,canReadPlayer=visiblePlayers.Contains(p.PlayerId)});
        return new {session,register.Name,register.Currency,canClose,canCollect,canCrossBranchPayment,balance=Money(await Balance(session)),variance=session.CountedAmount.HasValue?Money(session.CountedAmount.Value-session.ExpectedAmount!.Value):null,movements=await db.CashMovements.Where(m=>m.SessionId==id).OrderBy(m=>m.CreatedAtUtc).ToListAsync(),receipts,productSales=await db.CashProductPostings.Where(p=>p.SessionId==id).OrderByDescending(p=>p.CreatedAtUtc).Take(100).Select(p=>new{p.Id,p.ProductSaleId,amount=Money(p.Amount),p.Currency,p.ProductSale.ProductName,p.ProductSale.Quantity,p.CreatedAtUtc}).ToListAsync(),refunds};
    }
    public async Task<object> Create(CashRegisterWrite body,string key)
    {
        var (t,user)=await Context(owner:true); var name=body.Name?.Trim(); if(string.IsNullOrEmpty(name)||name.Length>100) throw new CashRejected("أدخل اسم الصندوق حتى100حرف.",422);
        await using var tx=await db.Database.BeginTransactionAsync(); var request=await Request(t.AcademyId,user,"create",key); var hash=AcademyConfigurationService.Hash(System.Text.Json.JsonSerializer.Serialize(new{body.BranchId,name}));
        var prior=await db.CashRegisters.SingleOrDefaultAsync(r=>r.RequestKey==request); if(prior is not null){Same(prior.RequestHash,hash);return new{prior.Id,replay=true};}
        if(!await db.Branches.AnyAsync(b=>b.Id==body.BranchId&&b.IsActive)) throw new CashRejected("الفرع غير متاح.",404);
        var currency=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.DefaultCurrency).SingleAsync(); var now=time.GetUtcNow();
        var row=new CashRegister{Id=Guid.NewGuid(),AcademyId=t.AcademyId,BranchId=body.BranchId,Name=name,Currency=currency,CreatedByUserId=user,RequestKey=request,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.CashRegisters.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id};
    }
    public async Task<object> Assign(Guid id,CashierWrite body)
    {
        var (t,user)=await Context(owner:true); await using var tx=await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId,$"register:{id}"); var register=await Register(id);
        var member=await db.AcademyMemberships.SingleOrDefaultAsync(m=>m.Id==body.MembershipId&&m.IsActive&&m.User.IsActive&&(m.Role==AcademyRole.AcademyOwner||m.Role==AcademyRole.AcademyAdmin));
        if(member is null || !member.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(a=>a.MembershipId==member.Id&&a.BranchId==register.BranchId&&a.IsActive)) throw new CashRejected("الموظف غير متاح لهذا الفرع.",404);
        var row=await db.CashierAssignments.SingleOrDefaultAsync(a=>a.RegisterId==id&&a.MembershipId==body.MembershipId);var before=row?.IsActive??false;
        if(row is not null&&before==body.Enabled)return new{row.Id,replay=true};
        var now=time.GetUtcNow(); if(row is null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,RegisterId=id,BranchId=register.BranchId,MembershipId=member.Id,CreatedAtUtc=now};db.CashierAssignments.Add(row);} row.IsActive=body.Enabled;row.UpdatedAtUtc=now;
        db.StaffAccessAudits.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=member.Id,ActorUserId=user,Before=System.Text.Json.JsonSerializer.Serialize(new{cashRegisterId=id,cashierEnabled=before}),After=System.Text.Json.JsonSerializer.Serialize(new{cashRegisterId=id,cashierEnabled=body.Enabled}),CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id};
    }
    public async Task<object> Open(Guid id,CashOpenWrite body,string key)
    {
        var (t,user)=await Context(write:true);Amount(body.OpeningAmount);await using var tx=await db.Database.BeginTransactionAsync();var request=await Request(t.AcademyId,user,"open",key);
        await Lock(t.AcademyId,$"register:{id}");var register=await Register(id);await Enabled(t.AcademyId);await Usable(register);await Cashier(register,t);
        var hash=AcademyConfigurationService.Hash($"{id}:{Money(body.OpeningAmount)}");var prior=await db.CashSessions.SingleOrDefaultAsync(s=>s.OpenKey==request);if(prior is not null){Same(prior.OpenHash,hash);return new{prior.Id,replay=true};}
        if(await db.CashSessions.AnyAsync(s=>s.RegisterId==id&&s.ClosedAtUtc==null)) throw new CashRejected("لهذا الصندوق وردية مفتوحة بالفعل.");
        var now=time.GetUtcNow();var row=new CashSession{Id=Guid.NewGuid(),AcademyId=t.AcademyId,RegisterId=id,BranchId=register.BranchId,OpenedByMembershipId=t.MembershipId,OpeningAmount=body.OpeningAmount,OpenedAtUtc=now,OpenKey=request,OpenHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.CashSessions.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id};
    }
    public async Task<object> Close(Guid id,CashCloseWrite body,string key)
    {
        var (t,user)=await Context(write:true);Amount(body.CountedAmount);ReasonLimit(body.Reason);await using var tx=await db.Database.BeginTransactionAsync();var request=await Request(t.AcademyId,user,"close",key);await Lock(t.AcademyId,$"session:{id}");var session=await Session(id);var register=await Register(session.RegisterId);
        // Disabling cash/branch or revoking a cashier must not strand money: the
        // scoped Owner can still reconcile and close. Admin must retain its grant.
        if(t.Role!=AcademyRole.AcademyOwner){await Cashier(register,t);if(session.OpenedByMembershipId!=t.MembershipId)throw new CashRejected("يمكنك إقفال ورديتك فقط.",403);}
        var hash=AcademyConfigurationService.Hash($"{id}:{Money(body.CountedAmount)}:{Reason(body.Reason)}");
        if(session.ClosedAtUtc.HasValue){if(session.CloseKey!=request)throw new CashRejected("الوردية مقفلة ولا تعدل.");Same(session.CloseHash!,hash);return new{session.Id,replay=true};}
        session.ExpectedAmount=await Balance(session);session.CountedAmount=body.CountedAmount;session.ClosingReason=Reason(body.Reason);session.ClosedAtUtc=time.GetUtcNow();session.ClosedByUserId=user;session.CloseKey=request;session.CloseHash=hash;session.UpdatedAtUtc=time.GetUtcNow();
        await db.SaveChangesAsync();await tx.CommitAsync();return new{session.Id,expected=Money(session.ExpectedAmount.Value),counted=Money(body.CountedAmount),variance=Money(body.CountedAmount-session.ExpectedAmount.Value)};
    }
    public async Task<object> Transfer(CashTransferWrite body,string key)
    {
        var (t,user)=await Context(owner:true);Amount(body.Amount,false);ReasonLimit(body.Reason);if(body.FromSessionId==body.ToSessionId)throw new CashRejected("اختر ورديتين مختلفتين.",422);
        await using var tx=await db.Database.BeginTransactionAsync();var request=await Request(t.AcademyId,user,"transfer",key);
        var hash=AcademyConfigurationService.Hash($"{body.FromSessionId}:{body.ToSessionId}:{Money(body.Amount)}:{Reason(body.Reason)}");var prior=await db.CashTransfers.SingleOrDefaultAsync(x=>x.RequestKey==request);if(prior is not null){Same(prior.RequestHash,hash);return new{prior.Id,replay=true};}
        foreach(var id in new[]{body.FromSessionId,body.ToSessionId}.Order())await Lock(t.AcademyId,$"session:{id}");
        var from=await Session(body.FromSessionId);var to=await Session(body.ToSessionId);var a=await Register(from.RegisterId);var b=await Register(to.RegisterId);await Enabled(t.AcademyId);await Usable(a);await Usable(b);
        if(from.ClosedAtUtc.HasValue||to.ClosedAtUtc.HasValue)throw new CashRejected("التحويل بين ورديتين مفتوحتين فقط.");if(a.Currency!=b.Currency)throw new CashRejected("لا تحول بين عملتين مختلفتين.");if(await Balance(from)<body.Amount)throw new CashRejected("رصيد الصندوق المصدر لا يكفي.");
        var now=time.GetUtcNow();var row=new CashTransfer{Id=Guid.NewGuid(),AcademyId=t.AcademyId,FromSessionId=from.Id,ToSessionId=to.Id,BranchId=from.BranchId,ToBranchId=to.BranchId,Amount=body.Amount,Currency=a.Currency,ActorUserId=user,Reason=Reason(body.Reason),RequestKey=request,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.CashTransfers.Add(row);
        foreach(var (s,amount,kind) in new[]{(from,-body.Amount,CashMovementKind.TransferOut),(to,body.Amount,CashMovementKind.TransferIn)})db.CashMovements.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,SessionId=s.Id,BranchId=s.BranchId,TransferId=row.Id,Kind=kind,Amount=amount,Currency=a.Currency,ActorUserId=user,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id};
    }
}
