using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice3;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Finance;
public sealed record AgreementSlot(Guid ResourceId,DateOnly Date,TimeOnly Start,TimeOnly End,int Seats,bool Exclusive);
public sealed record ExternalAgreementWrite(Guid RequestId,string Title,ExternalAgreementKind Kind,Guid BranchId,Guid SportId,DateOnly FromDate,DateOnly ToDate,DateOnly DueOn,decimal Amount,string? CounterpartyName=null,Guid? CounterpartyEmployeeId=null,string? Note=null,AgreementSlot[]? Slots=null);
public sealed record ExternalReceiptWrite(Guid RequestId,decimal Amount,string PaymentMethod,bool ConfirmedReceived,string? PaymentReference=null,Guid? CashSessionId=null);
public sealed record ExternalCancelWrite(uint Version,string? Reason=null);

public static class ExternalAgreementEndpoints
{
    public static void MapExternalAgreements(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1/finance/external-agreements").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        g.MapGet("/",List);g.MapGet("/options",Options);g.MapGet("/{id:guid}",Detail);
        g.MapPost("/",Create).AddEndpointFilter<CsrfFilter>();g.MapPost("/{id:guid}/receipts",Collect).AddEndpointFilter<CsrfFilter>();g.MapPost("/{id:guid}/cancel",Cancel).AddEndpointFilter<CsrfFilter>();
    }
    private static string Hash(object b)=>AcademyConfigurationService.Hash(JsonSerializer.Serialize(b));
    private static bool Money(decimal amount)=>amount>0&&amount<=999999999&&decimal.Round(amount,2)==amount;
    private static IResult Invalid(string text)=>Results.UnprocessableEntity(new{message=text});
    private static async Task<(TenantMembership T,Guid Actor,bool CanManage)> Authority(CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService configuration,HttpContext h,bool write)
    {
        var t=await tenant.ResolveAsync()??throw new CashRejected("يلزم تسجيل الدخول.",401);h.Response.Headers.CacheControl="private, no-store";
        if(write){await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+t.AcademyId},0))");}
        if(write)configuration.DiscardRequestCache(t.AcademyId);
        var member=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==t.MembershipId&&m.AcademyId==t.AcademyId&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive);
        if(member is null||member.Role!=t.Role||member.Role is not(AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin))throw new CashRejected("العملية غير مخولة.",403);
        var canManage=await configuration.Enabled(t.AcademyId,"external_partners")&&(t.Role==AcademyRole.AcademyOwner||member.UseActionGrants&&await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.Module=="finance"&&g.IsActive&&g.Read&&g.Add&&g.Edit));
        if(write&&!canManage)throw new CashRejected("إدارة الاتفاقات تحتاج إتاحة الشركاء وتفويض إضافة وتعديل الماليات.",403);
        return(t,Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!),canManage);
    }
    private static async Task<IResult> Options(CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http)
    {
        try{var(t,_,canManage)=await Authority(tenant,db,config,http,false);var facilitiesEnabled=await config.Enabled(t.AcademyId,"facilities");var canEmployees=t.Role==AcademyRole.AcademyOwner||await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="payroll"&&g.IsActive&&g.Read);
            return Results.Ok(new{canManage,currency=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.DefaultCurrency).SingleAsync(),branches=await db.Branches.Where(b=>b.IsActive).OrderBy(b=>b.ArabicName).Select(b=>new{b.Id,name=b.ArabicName}).ToArrayAsync(),sports=await db.Sports.Where(s=>s.IsActive).OrderBy(s=>s.ArabicName).Select(s=>new{s.Id,name=s.ArabicName,s.Kind}).ToArrayAsync(),employees=await db.Employees.Where(e=>canEmployees&&e.IsActive).OrderBy(e=>e.Name).Select(e=>new{e.Id,e.Name,e.BranchId}).ToArrayAsync(),resources=await(from r in db.FacilityResources join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id where facilitiesEnabled&&r.IsActive&&s.IsActive&&r.ApprovedByUserId!=null&&s.Kind==Academy.Infrastructure.Facilities.FacilitySpaceKind.Pool&&r.ServiceKinds.Contains("FacilityBooking") orderby r.Name select new{r.Id,r.Name,r.BranchId,s.SportId,r.Capacity}).ToArrayAsync()});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
    }
    private static async Task<IResult> List(CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http,TimeProvider time,Guid? branchId,Guid? sportId,Guid? employeeId,string? search,string? state,string? kind,DateOnly? from,DateOnly? to,bool? allDates,int? page,int? pageSize)
    {
        try{var(t,_,canManage)=await Authority(tenant,db,config,http,false);if(search?.Length>120||page is <1 or >100000||pageSize is <1 or >100||state is not(null or "" or "Open" or "Paid" or "Cancelled" or "Overdue")||kind is not(null or "" or "PoolRental" or "ExternalProgram"))return Invalid("راجع فلاتر الاتفاقات والصفحات.");
            var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);if(allDates!=true&&from==null&&to==null){from=new(today.Year,today.Month,1);to=today;}if(from>to)return Invalid("بداية الفترة بعد نهايتها.");
            var q=db.ExternalAgreements.AsNoTracking().Where(a=>a.AcademyId==t.AcademyId&&(!branchId.HasValue||a.BranchId==branchId)&&(!sportId.HasValue||a.SportId==sportId)&&(!employeeId.HasValue||a.CounterpartyEmployeeId==employeeId)&&(!from.HasValue||a.ToDate>=from)&&(!to.HasValue||a.FromDate<=to));
            if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim();q=q.Where(a=>a.Title.Contains(term)||a.CounterpartyName.Contains(term));}if(kind=="PoolRental")q=q.Where(a=>a.Kind==ExternalAgreementKind.PoolRental);if(kind=="ExternalProgram")q=q.Where(a=>a.Kind==ExternalAgreementKind.ExternalProgram);
            var values=q.Select(a=>new{a.Id,a.Title,a.CounterpartyName,a.CounterpartyEmployeeId,a.Kind,a.FromDate,a.ToDate,a.DueOn,a.Amount,a.Currency,a.CancelledAtUtc,a.BranchId,a.SportId,branch=db.Branches.Where(b=>b.Id==a.BranchId).Select(b=>b.ArabicName).FirstOrDefault(),sport=db.Sports.Where(s=>s.Id==a.SportId).Select(s=>s.ArabicName).FirstOrDefault(),paid=db.ExternalAgreementReceipts.Where(r=>r.AgreementId==a.Id).Sum(r=>(decimal?)r.Amount)??0});
            if(state=="Cancelled")values=values.Where(a=>a.CancelledAtUtc!=null);if(state=="Paid")values=values.Where(a=>a.CancelledAtUtc==null&&a.paid==a.Amount);if(state=="Open")values=values.Where(a=>a.CancelledAtUtc==null&&a.paid<a.Amount);if(state=="Overdue")values=values.Where(a=>a.CancelledAtUtc==null&&a.paid<a.Amount&&a.DueOn<today);
            var size=pageSize??25;var current=page??1;return Results.Ok(new{from,to,canManage,count=await values.CountAsync(),page=current,pageSize=size,items=(await values.OrderByDescending(a=>a.FromDate).ThenBy(a=>a.Id).Skip((current-1)*size).Take(size).ToArrayAsync()).Select(a=>new{a.Id,a.Title,a.CounterpartyName,a.CounterpartyEmployeeId,a.Kind,a.FromDate,a.ToDate,a.DueOn,a.Amount,a.Currency,a.CancelledAtUtc,a.BranchId,a.SportId,a.branch,a.sport,a.paid,remaining=a.CancelledAtUtc!=null?0:a.Amount-a.paid,state=a.CancelledAtUtc!=null?"Cancelled":a.paid>=a.Amount?"Paid":a.DueOn<today?"Overdue":"Open"}),basis="رصيد اتفاق خارجي مستقل؛ لا يخصم من راتب الموظف. الإيراد هنا لا يثبت إلا من إيصال تحصيل."});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
    }
    private static async Task<IResult> Detail(Guid id,CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http,CashRegisterService cash)
    {
        try{var(t,_,canManage)=await Authority(tenant,db,config,http,false);var a=await db.ExternalAgreements.AsNoTracking().SingleOrDefaultAsync(a=>a.AcademyId==t.AcademyId&&a.Id==id);if(a is null)return Results.NotFound();
            var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(x=>x.Id==t.AcademyId).Select(x=>x.TimeZone).SingleAsync());
            var receipts=await db.ExternalAgreementReceipts.AsNoTracking().Where(r=>r.AgreementId==id).OrderByDescending(r=>r.ReceivedAtUtc).ToArrayAsync();
            return Results.Ok(new{agreement=a,receipts=receipts.Select(r=>new{r.Id,r.Amount,r.Currency,r.ReceiptNumber,r.PaymentMethod,r.PaymentReference,r.ReceivedAtUtc,receivedOn=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.ReceivedAtUtc,zone).DateTime)}),paid=receipts.Sum(r=>r.Amount),remaining=a.CancelledAtUtc.HasValue?0:a.Amount-receipts.Sum(r=>r.Amount),canManage,cash=await cash.ExternalReceiptOptions(a.BranchId,a.Currency),bookings=await(from l in db.ExternalAgreementBookings join b in db.ResourceBookings on l.ResourceBookingId equals b.Id join r in db.FacilityResources on b.ResourceId equals r.Id where l.AgreementId==id orderby b.Date,b.StartTime select new{b.Id,b.Date,b.StartTime,b.EndTime,resource=r.Name,b.State,b.ReservedSeats}).ToArrayAsync()});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
    }
    private static async Task<IResult> Create(ExternalAgreementWrite b,CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http,TimeProvider clock,FacilityService facilities)
    {
        try{await using var tx=await db.Database.BeginTransactionAsync();var(t,actor,_)=await Authority(tenant,db,config,http,true);
            if(b.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(b.Title)||b.Title.Length>180||b.CounterpartyName?.Length>180||b.Note?.Length>500||!Money(b.Amount)||b.FromDate>b.ToDate||b.FromDate.Year<2000||b.ToDate.DayNumber-b.FromDate.DayNumber>366||b.DueOn.Year<2000||!Enum.IsDefined(b.Kind)||b.Slots?.Length>32)return Invalid("راجع اسم الاتفاق والطرف والفترة والمبلغ ومواعيد الحجز.");
            var hash=Hash(b);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"external-agreement-create:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");var prior=await db.ExternalAgreements.SingleOrDefaultAsync(a=>a.ActorMembershipId==t.MembershipId&&a.RequestId==b.RequestId);if(prior is not null)return prior.RequestHash==hash?Results.Ok(new{prior.Id,replay=true}):Results.Conflict(new{message="معرف المحاولة استخدم لبيانات أخرى."});
            if(!await db.Branches.AnyAsync(x=>x.Id==b.BranchId&&x.IsActive)||!await db.Sports.AnyAsync(s=>s.Id==b.SportId&&s.IsActive))return Results.NotFound();
            var name=b.CounterpartyName?.Trim();if(b.CounterpartyEmployeeId.HasValue){if(t.Role!=AcademyRole.AcademyOwner&&!await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="payroll"&&g.IsActive&&g.Read))return Results.Forbid();name=await db.Employees.Where(e=>e.Id==b.CounterpartyEmployeeId&&e.IsActive).Select(e=>e.Name).SingleOrDefaultAsync();if(name is null)return Results.NotFound();}if(string.IsNullOrWhiteSpace(name))return Invalid("أدخل اسم الطرف أو اختر سجله الوظيفي.");
            var slots=b.Slots??[];if(b.Kind==ExternalAgreementKind.PoolRental){if(slots.Length==0||!await config.Enabled(t.AcademyId,"facilities")||!await db.Sports.AnyAsync(s=>s.Id==b.SportId&&s.Kind==Academy.Infrastructure.Structure.SportKind.Swimming))return Invalid("تأجير المسبح يحتاج رياضة سباحة مفعلة وحجز مورد فعلي واحد على الأقل.");await facilities.Authority("Add");}else if(slots.Length!=0)return Invalid("حجوزات الحارات مخصصة لاتفاق تأجير مسبح فقط.");
            var now=clock.GetUtcNow();var currency=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.DefaultCurrency).SingleAsync();var a=new ExternalAgreement{Id=Guid.NewGuid(),AcademyId=t.AcademyId,BranchId=b.BranchId,SportId=b.SportId,CounterpartyEmployeeId=b.CounterpartyEmployeeId,CounterpartyName=name,Title=b.Title.Trim(),Kind=b.Kind,FromDate=b.FromDate,ToDate=b.ToDate,DueOn=b.DueOn,Amount=b.Amount,Currency=currency,Note=b.Note?.Trim(),ActorUserId=actor,ActorMembershipId=t.MembershipId,RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.ExternalAgreements.Add(a);await db.SaveChangesAsync();
            foreach(var (slot,i) in slots.Select((s,i)=>(s,i)).OrderBy(x=>x.s.ResourceId).ThenBy(x=>x.s.Date).ThenBy(x=>x.s.Start)){if(slot.Date<b.FromDate||slot.Date>b.ToDate||!await(from r in db.FacilityResources join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id where r.Id==slot.ResourceId&&r.BranchId==b.BranchId&&s.SportId==b.SportId&&s.Kind==Academy.Infrastructure.Facilities.FacilitySpaceKind.Pool select r.Id).AnyAsync())return Invalid("موعد الحجز أو المورد لا يطابق اتفاق السباحة وفرعه.");var booking=await facilities.ReserveLocked(new(slot.ResourceId,slot.Date,slot.Start,slot.End,slot.Seats,slot.Exclusive,"FacilityBooking"),$"external-{a.Id:N}-{i}",t.AcademyId,actor);db.ExternalAgreementBookings.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AgreementId=a.Id,ResourceBookingId=booking.Id,BranchId=a.BranchId,SportId=a.SportId,CreatedAtUtc=now,UpdatedAtUtc=now});}
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"/api/v1/finance/external-agreements/{a.Id}",new{a.Id});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:"23514" or "23505" or "23503"}){return Results.Conflict(new{message="تغير السجل أو تعارض الحجز أو التحصيل مع ضوابطه؛ حدّث الصفحة وراجع المصدر."});}
        catch(PostgresException e) when(e.SqlState is "23514" or "23505" or "40001" or "40P01"){return Results.Conflict(new{message="تعذر إتمام العملية بسبب تعارض متزامن؛ حدّث السجل وأعد المحاولة."});}
    }
    private static async Task<IResult> Collect(Guid id,ExternalReceiptWrite b,CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http,ISubscriptionClock financialClock,TimeProvider time,CashRegisterService cash)
    {
        try{await using var tx=await db.Database.BeginTransactionAsync();var(t,actor,_)=await Authority(tenant,db,config,http,true);if(!b.ConfirmedReceived||b.RequestId==Guid.Empty||!Money(b.Amount)||b.PaymentMethod is not("Cash" or "Online")||b.PaymentReference?.Length>100)return Invalid("أكد استلام المبلغ فعليًا وراجع القيمة وطريقة الدفع.");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"external-receipt:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");var hash=Hash(new{id,b});var replay=await db.ExternalAgreementReceipts.SingleOrDefaultAsync(r=>r.ActorMembershipId==t.MembershipId&&r.RequestId==b.RequestId);if(replay is not null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay.ReceiptNumber,replay=true}):Results.Conflict(new{message="معرف المحاولة استخدم لتحصيل آخر."});
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"external-agreement:{t.AcademyId}:{id}"},0))");var a=await db.ExternalAgreements.SingleOrDefaultAsync(a=>a.Id==id);if(a is null)return Results.NotFound();var paid=await db.ExternalAgreementReceipts.Where(r=>r.AgreementId==id).SumAsync(r=>(decimal?)r.Amount)??0;if(a.CancelledAtUtc.HasValue||b.Amount>a.Amount-paid)return Results.Conflict(new{message="الاتفاق ملغى أو المبلغ يتجاوز المتبقي؛ حدّث السجل."});
            var cashRequired=b.PaymentMethod=="Cash"&&(await config.Active(t.AcademyId))?.CashEnabled==true;if(cashRequired)await cash.PrepareExternalReceipt(b.CashSessionId,a.BranchId,a.Currency);else if(b.CashSessionId.HasValue)return Invalid("لا تختار وردية لتحصيل إلكتروني أو نقدي غير مربوط بالصناديق.");
            var receiptId=Guid.NewGuid();var now=time.GetUtcNow();var r=new ExternalAgreementReceipt{Id=receiptId,AcademyId=t.AcademyId,AgreementId=id,BranchId=a.BranchId,SportId=a.SportId,Amount=b.Amount,Currency=a.Currency,PaymentMethod=b.PaymentMethod,PaymentReference=b.PaymentReference?.Trim(),ReceiptNumber="EXT-"+receiptId.ToString("N"),CashPostingRequired=cashRequired,ReceivedAtUtc=financialClock.UtcNow,ActorUserId=actor,ActorMembershipId=t.MembershipId,RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.ExternalAgreementReceipts.Add(r);await db.SaveChangesAsync();if(cashRequired)await cash.PostExternalReceipt(b.CashSessionId!.Value,r.Id);
            await tx.CommitAsync();return Results.Created($"/api/v1/finance/external-agreements/{id}",new{r.Id,r.ReceiptNumber});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:"23514" or "23505" or "23503"}){return Results.Conflict(new{message="تغير السجل أو تعارض الحجز أو التحصيل مع ضوابطه؛ حدّث الصفحة وراجع المصدر."});}
        catch(PostgresException e) when(e.SqlState is "23514" or "23505" or "40001" or "40P01"){return Results.Conflict(new{message="تعذر إتمام العملية بسبب تعارض متزامن؛ حدّث السجل وأعد المحاولة."});}
    }
    private static async Task<IResult> Cancel(Guid id,ExternalCancelWrite b,CurrentTenant tenant,FoundationDbContext db,AcademyConfigurationService config,HttpContext http,TimeProvider time,FacilityService facilities)
    {
        try{await using var tx=await db.Database.BeginTransactionAsync();var(t,actor,_)=await Authority(tenant,db,config,http,true);if(b.Reason?.Length>500)return Invalid("الملاحظة حتى500حرف.");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"external-agreement:{t.AcademyId}:{id}"},0))");var a=await db.ExternalAgreements.SingleOrDefaultAsync(a=>a.Id==id);if(a is null)return Results.NotFound();if(a.CancelledAtUtc.HasValue)return Results.Ok(new{a.Id,replay=true});if(a.Version!=b.Version||await db.ExternalAgreementReceipts.AnyAsync(r=>r.AgreementId==id))return Results.Conflict(new{message="لا يلغى اتفاق محصل أو نسخة تغيرت؛ التاريخ المالي محفوظ."});
            var bookings=await(from l in db.ExternalAgreementBookings join r in db.ResourceBookings on l.ResourceBookingId equals r.Id where l.AgreementId==id select r).OrderBy(r=>r.ResourceId).ThenBy(r=>r.Id).ToArrayAsync();if(bookings.Length>0)await facilities.Authority("Edit");foreach(var booking in bookings){if(booking.State==Academy.Infrastructure.Facilities.ResourceBookingState.Cancelled)continue;await facilities.Lock(booking.ResourceId);booking.State=Academy.Infrastructure.Facilities.ResourceBookingState.Cancelled;booking.CancelledAtUtc=time.GetUtcNow();booking.CancelledByUserId=actor;booking.UpdatedAtUtc=time.GetUtcNow();}
            a.CancelledAtUtc=time.GetUtcNow();a.CancelledByUserId=actor;a.CancellationReason=string.IsNullOrWhiteSpace(b.Reason)?null:b.Reason.Trim();a.UpdatedAtUtc=time.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{a.Id});
        }catch(CashRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:"23514" or "23505" or "23503"}){return Results.Conflict(new{message="تغير السجل أو تعارض الحجز أو التحصيل مع ضوابطه؛ حدّث الصفحة وراجع المصدر."});}
        catch(PostgresException e) when(e.SqlState is "23514" or "23505" or "40001" or "40P01"){return Results.Conflict(new{message="تعذر إتمام العملية بسبب تعارض متزامن؛ حدّث السجل وأعد المحاولة."});}
    }
}
