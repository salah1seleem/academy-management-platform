using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class NoticeEndpoints
{
    public static readonly IReadOnlyDictionary<string,string> Defaults=new Dictionary<string,string>{
        ["Overdue"]="أهلًا {اسم_ولي_الأمر}، نذكركم بمستحقات {اسم_اللاعب} بقيمة {القيمة}. راجعوا كشف المستحقات أو تواصلوا مع الإدارة.",
        ["Renewal"]="أهلًا {اسم_ولي_الأمر}، حان موعد مراجعة اشتراك {اسم_اللاعب}. يمكنكم مراجعة الباقات والتجديد من التطبيق.",
        ["Enrollment"]="تم تفعيل اشتراك جديد لـ{اسم_اللاعب}. راجعوا تفاصيل الاشتراك ومواعيد التدريب.",
        ["SingleRenewal"]="تم تجديد اشتراك {اسم_اللاعب}. راجعوا الفترة الجديدة ومواعيد التدريب.",
        ["FamilyRenewal"]="تم تجديد اشتراك {أسماء_الأبناء}. راجعوا إيصالات الأسرة ومواعيد التدريب.",
        ["RecordedAbsence"]="سُجل غياب {اسم_اللاعب} عن حصة {التاريخ}.",
        ["DaysAdded"]="أُضيف {عدد_المرات} يوم إلى اشتراك {اسم_اللاعب}. راجعوا تاريخ الانتهاء المحدّث.",
        ["SessionsAdded"]="أُضيف {عدد_المرات} حصص إلى اشتراك {اسم_اللاعب} وأصبحت نهاية الفترة {التاريخ}.",
        ["DaysDeducted"]="خُصم {عدد_المرات} يوم من اشتراك {اسم_اللاعب}. راجعوا تاريخ الانتهاء المحدّث.",
        ["Expiring"]="اقترب انتهاء اشتراك {اسم_اللاعب}. راجعوا مواعيد التجديد من التطبيق.",
        ["Expired"]="انتهى اشتراك {اسم_اللاعب}. نتطلع لعودتهم إلى التدريب؛ يمكنكم التجديد من التطبيق.",
        ["Absence"]="غاب ابنك {اسم_اللاعب} {عدد_المرات} مرات متتالية. للتواصل أو تقديم شكوى أو اقتراح افتحوا التواصل مع الأكاديمية.",
        ["Evaluation"]="نشر المدرب تقييمًا جديدًا لـ{اسم_اللاعب}. افتحوا التقييمات لمتابعة التقدم.",
        ["Star"]="أحسنتم! حصل {اسم_اللاعب} على نجمة هذا الشهر. شاهدوا إنجازه في التطبيق.",
        ["Match"]="تم تحديث مباراة {اسم_الفريق}. راجعوا تفاصيل المباراة والمكان والموعد.",
        ["Payment"]="لديكم تحديث على طلب الدفع. راجعوا تفاصيل الطلب في التطبيق.",
        ["VipReward"]="لديكم مكافأة اشتراك جديدة. راجعوا النسبة وشروط الاستخدام والصلاحية في مكافآت الأسرة."
    };
    private static Guid User(ClaimsPrincipal p)=>Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapNoticeInbox(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/notifications",Inbox);
        api.MapPut("/notifications/{id:guid}/read",Read).AddEndpointFilter<CsrfFilter>();
        var manage=app.MapGroup("/api/v1/manage/notifications").RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        manage.MapGet("/templates",Templates);
        manage.MapGet("/history",History);
        manage.MapGet("/automation",Automation);
        manage.MapPut("/templates/{key}",SaveTemplate).AddEndpointFilter<CsrfFilter>();
        manage.MapGet("/recipients",Recipients);
        manage.MapPost("/preview",Preview).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/send",Send).AddEndpointFilter<CsrfFilter>();
    }
    public static IQueryable<AcademyNotice> OwnNotices(FoundationDbContext db,Guid academy,Guid user,AcademyRole role) =>
        db.AcademyNotices.Where(n=>n.AcademyId==academy&&n.UserId==user&&n.IsActive&&(n.SubjectPlayerId==null||role==AcademyRole.Guardian&&db.GuardianPlayerLinks.Any(l=>l.AcademyId==academy&&l.PlayerId==n.SubjectPlayerId&&l.Guardian.UserId==user&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive))&&(n.AudienceRole==role.ToString()||n.AudienceRole==null&&!db.AcademyMemberships.Any(m=>m.AcademyId==academy&&m.UserId==user&&m.Role!=role)));
    // Same live family/role predicate for the home preview and the paged inbox.
    public static IQueryable<AcademyNotice> GuardianUrgentMatches(FoundationDbContext db, Guid academy, Guid user, DateTimeOffset now) =>
        OwnNotices(db, academy, user, AcademyRole.Guardian).Where(n => !n.IsRead && n.UrgentUntilUtc > now && n.MatchId != null &&
            db.MatchParticipants.Any(p => p.MatchId == n.MatchId && p.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == academy && l.Guardian.UserId == user && l.Guardian.IsActive && l.IsActive && l.Player.IsActive && l.PlayerId == p.PlayerId)));
    private static async Task<IResult> Inbox(int? page,bool? urgentMatchesOnly,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db,HttpResponse response,TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;response.Headers.CacheControl="private, no-store";var u=User(actor);
        if(page is <1 or >100000)return Results.UnprocessableEntity(new{message="رقم الصفحة غير صالح."});
        if(urgentMatchesOnly == true && t.Role != AcademyRole.Guardian) return Results.Forbid();
        var q=urgentMatchesOnly == true ? GuardianUrgentMatches(db,t.AcademyId,u,time.GetUtcNow()) : OwnNotices(db,t.AcademyId,u,t.Role);
        var items=await q.OrderByDescending(n=>n.CreatedAtUtc).ThenBy(n=>n.Id).Skip(((page??1)-1)*25).Take(25).Select(n=>new{n.Id,n.Message,n.IsRead,n.CreatedAtUtc,n.MatchId,n.TargetKind,n.TargetId,n.EventKey,
            paymentReviewType = n.TargetKind != "PaymentQueue" ? null :
                db.NewEnrollmentRequests.Any(r => r.AcademyId == t.AcademyId && r.Id == n.TargetId) ? "Enrollment" :
                db.FamilyRenewalRequests.Any(r => r.AcademyId == t.AcademyId && r.Id == n.TargetId) ? "FamilyRenewal" :
                db.PaymentRequests.Any(r => r.AcademyId == t.AcademyId && r.Id == n.TargetId) ? "Renewal" : null,
            reward=t.Role==AcademyRole.Guardian&&db.VipRewards.Any(r=>r.AcademyId==t.AcademyId&&r.NoticeId==n.Id&&r.GuardianId==db.GuardianProfiles.Where(g=>g.UserId==u&&g.IsActive).Select(g=>g.Id).FirstOrDefault())}).ToArrayAsync();
        return Results.Ok(new{items,unread=await q.CountAsync(n=>!n.IsRead),total=await q.CountAsync(),page=page??1});
    }
    public sealed record ReadWrite(bool IsRead);
    private static async Task<IResult> Read(Guid id,ReadWrite body,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db)
    {
        var t=(await tenant.ResolveAsync())!;var u=User(actor);var n=await OwnNotices(db,t.AcademyId,u,t.Role).SingleOrDefaultAsync(n=>n.Id==id);
        if(n==null)return Results.NotFound();n.IsRead=body.IsRead;await db.SaveChangesAsync();return Results.NoContent();
    }
    private static async Task<IResult> Templates(CurrentTenant tenant,FoundationDbContext db)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        var saved=await db.NoticeTemplates.AsNoTracking().Where(n=>n.AcademyId==t.AcademyId).ToDictionaryAsync(n=>n.Key);
        return Results.Ok(Defaults.Select(d=>new{key=d.Key,message=saved.GetValueOrDefault(d.Key)?.Message??d.Value,variables=NoticeText.EditorVariables(d.Key),
            enabled=saved.GetValueOrDefault(d.Key)?.IsActive??true,version=saved.GetValueOrDefault(d.Key)?.Version??0,
            timingDays=d.Key=="Expiring"?(int?)(saved.GetValueOrDefault(d.Key)?.TimingDays??7):null,
            condition=Condition(d.Key),recipients=d.Key=="Match"?new[]{"ولي الأمر المرتبط بالمشارك","مدرب المباراة المسند فعليًا"}:d.Key=="Payment"?new[]{"ولي الأمر صاحب الطلب","المالك عند وصول إثبات"}:d.Key=="FamilyRenewal"?new[]{"ولي الأمر صاحب طلب الأسرة"}:new[]{"ولي الأمر المرتبط باللاعب"}}));
    }
    public sealed record TemplateWrite(string Message,bool? Enabled=null,int? TimingDays=null,uint? ExpectedVersion=null);
    private static async Task<IResult> SaveTemplate(string key,TemplateWrite body,CurrentTenant tenant,FoundationDbContext db,TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        if(!Defaults.ContainsKey(key)||string.IsNullOrWhiteSpace(body.Message)||body.Message.Length>1000)return Results.UnprocessableEntity(new{message="أدخل نص تنبيه صحيحًا حتى1000حرف."});
        if(body.TimingDays.HasValue&&(key!="Expiring"||body.TimingDays is <1 or >30))return Results.UnprocessableEntity(new{message="اختر موعد قرب الانتهاء بين يوم و30 يومًا فقط لهذا الحدث."});
        if(!NoticeText.ValidVariables(key,body.Message))return Results.UnprocessableEntity(new{message="القالب يحتوي متغيرًا غير مدعوم لهذا الحدث. استخدم قائمة المتغيرات المتاحة."});
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"notice-template:{t.AcademyId}:{key}"},0))");
        var row=await db.NoticeTemplates.SingleOrDefaultAsync(n=>n.AcademyId==t.AcademyId&&n.Key==key);
        if(body.ExpectedVersion.HasValue&&body.ExpectedVersion!=(row?.Version??0))return Results.Conflict(new{message="تغير إعداد التنبيه؛ أعد تحميله قبل الحفظ."});
        if(row==null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,Key=key,Message=body.Message.Trim(),CreatedAtUtc=time.GetUtcNow()};db.NoticeTemplates.Add(row);}
        if(body.Enabled.HasValue)row.IsActive=body.Enabled.Value;
        if(body.TimingDays.HasValue)row.TimingDays=body.TimingDays;
        row.Message=body.Message.Trim();row.UpdatedAtUtc=time.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{version=row.Version,enabled=row.IsActive});
    }
    private static string Condition(string key)=>key switch{
        "Enrollment"=>"تفعيل أول فترة اشتراك فعلية بعد التحصيل أو الإعفاء المعتمد؛ الاستيراد التاريخي لا يرسل هذا الحدث.",
        "SingleRenewal"=>"تفعيل فترة تجديد فردية فعلية، مرة لكل فترة ومستلم.",
        "FamilyRenewal"=>"قبول طلب الأسرة كاملًا؛ تنبيه واحد بأسماء الأبناء لصاحب الطلب.",
        "DaysAdded" or "DaysDeducted"=>"بعد حفظ تعديل الأيام؛ لا توليد حصص أو تحويل أموال.",
        "SessionsAdded"=>"بعد إضافة حصص فعلية لرصيد الاشتراك وتحديد نهاية الفترة كلها؛ دون تحصيل أو تغيير سعر.",
        "RecordedAbsence"=>"تسجيل غياب فعلي في حصة محددة، مرة للواقعة والمستلم.",
        "Absence"=>"بلوغ حد الغياب المتتالي المحفوظ في إعدادات الأكاديمية، اعتمادًا على الحصص الفعلية السابقة فقط.",
        "Expiring"=>"فترة تدريب حالية تقترب نهايتها المؤرخة؛ توقيت بالأيام قبل الانتهاء، مرة لكل فترة ومستلم.",
        "Expired"=>"انتهاء آخر فترة تدريب بالمدة أو استنفاد حصصها، دون فترة أحدث؛ مرة لكل فترة ومستلم.",
        "Evaluation"=>"نشر تقييم له درجات فعلية؛ تعديل المسودة لا يرسل تنبيهًا.",
        "Star"=>"اعتماد نجمة شهرية فعلية دون تكرار الاعتماد.",
        "Match"=>"نشر المباراة أو تغير بصمتها المنشورة؛ الجمهور من المشاركين والإسناد الفعلي.",
        "Payment"=>"إثبات دفع محفوظ أو قرار فعلي عليه؛ لا يعني تحويلًا بنكيًا أو تحصيلًا جديدًا.",
        "VipReward"=>"إصدار مكافأة فعلية من إدارة المكافآت؛ القالب لا ينشئ المكافأة.",
        _=>"متابعة يدوية لجمهور ومعاينة محددين؛ ليست مشغلًا تلقائيًا."};
    private static async Task<IResult> Automation(CurrentTenant tenant,IConfiguration configuration,IWebHostEnvironment environment)
    {
        var t=(await tenant.ResolveAsync())!;return t.Role!=AcademyRole.AcademyOwner?Results.Forbid():Results.Ok(new{scheduledEnabled=configuration.GetValue("Notifications:WorkerEnabled",environment.IsProduction()),delivery="StoredInApp",externalDelivery=false});
    }
    private static async Task<IResult> History(int? page,string? eventKey,CurrentTenant tenant,FoundationDbContext db,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();response.Headers.CacheControl="private, no-store";
        if(page is <1 or >100000||!string.IsNullOrEmpty(eventKey)&&!Defaults.ContainsKey(eventKey))return Results.UnprocessableEntity(new{message="اختر صفحة ونوع تنبيه صحيحين."});
        var query=db.AcademyNotices.AsNoTracking().Where(n=>n.AcademyId==t.AcademyId&&(string.IsNullOrEmpty(eventKey)||n.EventKey==eventKey));
        return Results.Ok(new{total=await query.CountAsync(),page=page??1,pageSize=25,items=await query.OrderByDescending(n=>n.CreatedAtUtc).ThenBy(n=>n.Id).Skip(((page??1)-1)*25).Take(25).Select(n=>new{n.Id,n.EventKey,n.Message,n.IsRead,n.CreatedAtUtc,n.AudienceRole,recipient=db.Users.Where(u=>u.Id==n.UserId).Select(u=>u.DisplayName).FirstOrDefault(),status=n.IsActive?"StoredInApp":n.EventKey==null?"HistoricalHidden":"Disabled"}).ToArrayAsync()});
    }
    public sealed record Child(Guid Id,string Name,decimal? Amount);
    public sealed record Recipient(Guid Id,Guid UserId,string Name,Child[] Children,decimal? Amount,string Currency);
    private static async Task<Recipient[]> Audience(Guid academy,string kind,Guid? branch,FoundationDbContext db,ISubscriptionClock clock,DiscountRules pricing,Guid user)
    {
        var currency=await db.Academies.Where(a=>a.Id==academy).Select(a=>a.DefaultCurrency).SingleAsync();
        List<(Guid id,string name,decimal? amount,string currency)> players;
        if(kind=="Overdue")players=(await ReceivableEndpoints.Rows(db,academy,clock.UtcNow,clock.Today,branch,null,null).Where(r=>r.Overdue&&r.Outstanding>0&&r.Currency==currency).ToArrayAsync()).GroupBy(r=>new{r.PlayerId,r.Player,r.Currency}).Select(g=>(g.Key.PlayerId,g.Key.Player,(decimal?)g.Sum(r=>r.Outstanding),g.Key.Currency)).ToList();
        else if(kind=="Renewal")players=(await RenewalOpportunityEndpoints.Build(db,pricing,clock,academy,user,new(branch,null,null,null,null))).Items.Where(r=>r.Currency==currency).GroupBy(r=>new{r.PlayerId,r.Player,r.Currency}).Select(g=>(g.Key.PlayerId,g.Key.Player,g.Any(r=>r.Quote==null)?null:(decimal?)g.Sum(r=>r.Quote??0),g.Key.Currency)).ToList();
        else return [];
        var ids=players.Select(p=>p.id).Distinct().ToArray();
        var links=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==academy&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive&&l.Player.IsActive&&ids.Contains(l.PlayerId)).Select(l=>new{l.GuardianId,l.PlayerId,l.Guardian.UserId,l.Guardian.DisplayName}).ToArrayAsync();
        return links.GroupBy(l=>new{l.GuardianId,l.UserId,l.DisplayName}).Select(g=>{var own=players.Where(p=>g.Select(l=>l.PlayerId).Contains(p.id)).OrderBy(p=>p.name).ThenBy(p=>p.id).ToArray();return new Recipient(g.Key.GuardianId,g.Key.UserId,g.Key.DisplayName,own.Select(p=>new Child(p.id,p.name,p.amount)).ToArray(),own.Any(p=>p.amount==null)?null:own.Sum(p=>p.amount??0),own.First().currency);}).OrderBy(r=>r.Name).ThenBy(r=>r.Id).ToArray();
    }
    private static object View(Recipient r)=>new{r.Id,r.Name,r.Children,r.Amount,r.Currency};
    private static async Task<IResult> Recipients(string? kind,Guid? branchId,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db,ISubscriptionClock clock,DiscountRules pricing)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();if(kind is not("Overdue" or "Renewal"))return Results.UnprocessableEntity(new{message="اختر نوع المتابعة."});
        var items=await Audience(t.AcademyId,kind,branchId,db,clock,pricing,User(actor));
        var template=await db.NoticeTemplates.Where(n=>n.AcademyId==t.AcademyId&&n.Key==kind).Select(n=>n.Message).SingleOrDefaultAsync()??Defaults[kind];
        return Results.Ok(new{items=items.Take(200).Select(View),total=items.Length,shown=Math.Min(200,items.Length),template,kind});
    }
    public sealed record DispatchWrite(string Kind,Guid? BranchId,Guid[] Recipients,string Message,string? PreviewHash,Guid RequestId);
    private static string Hash(object x)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(x))));
    private static string Render(string text,Recipient r)=>NoticeText.RenderValues(text,string.Join("، ",r.Children.Select(c=>c.Name)),guardian:r.Name,amount:r.Amount is null?"تحتاج مراجعة":$"{r.Amount:0.00} {r.Currency}");
    private static async Task<IResult> Preview(DispatchWrite body,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db,ISubscriptionClock clock,DiscountRules pricing)
        =>await Dispatch(body,false,tenant,actor,db,clock,pricing);
    private static async Task<IResult> Send(DispatchWrite body,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db,ISubscriptionClock clock,DiscountRules pricing)
        =>await Dispatch(body,true,tenant,actor,db,clock,pricing);
    private static async Task<IResult> Dispatch(DispatchWrite body,bool send,CurrentTenant tenant,ClaimsPrincipal actor,FoundationDbContext db,ISubscriptionClock clock,DiscountRules pricing)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        if(body.Kind is not("Overdue" or "Renewal")||body.Recipients is not{Length:>0 and <=200}||body.Recipients.Distinct().Count()!=body.Recipients.Length||string.IsNullOrWhiteSpace(body.Message)||body.Message.Length>1000||body.RequestId==Guid.Empty)return Results.UnprocessableEntity(new{message="حدد مستلمين ورسالة صحيحة قبل المعاينة."});
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"notice-dispatch:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        var requestHash=Hash(new{body.Kind,body.BranchId,recipients=body.Recipients.Order().ToArray(),body.Message});
        var prior=await db.NoticeDispatches.SingleOrDefaultAsync(n=>n.AcademyId==t.AcademyId&&n.ActorMembershipId==t.MembershipId&&n.RequestId==body.RequestId);
        if(prior!=null)return prior.RequestHash==requestHash?Results.Ok(new{sent=prior.RecipientCount,replay=true}):Results.Conflict(new{message="مرجع الإرسال مستخدم لرسالة مختلفة."});
        var audience=(await Audience(t.AcademyId,body.Kind,body.BranchId,db,clock,pricing,User(actor))).Where(r=>body.Recipients.Contains(r.Id)).ToArray();
        if(audience.Length!=body.Recipients.Length)return Results.Conflict(new{message="تغيّر نطاق المستلمين أو المستحقات؛ حدّث القائمة."});
        var messages=audience.Select(r=>new{r.Id,message=Render(body.Message,r)}).ToArray();
        var previewHash=Hash(new{requestHash,scope=audience.Select(View),messages});
        if(!send)return Results.Ok(new{previewHash,count=audience.Length,messages,delivery="StoredInApp"});
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"notice-template:{t.AcademyId}:{body.Kind}"},0))");
        if(await db.NoticeTemplates.AnyAsync(n=>n.AcademyId==t.AcademyId&&n.Key==body.Kind&&!n.IsActive))return Results.Conflict(new{message="هذا النوع من التنبيهات معطل. فعّله من إدارة التنبيهات قبل الإرسال."});
        if(body.PreviewHash!=previewHash)return Results.Conflict(new{message="تغيّرت الرسالة أو بيانات المتابعة؛ أعد المعاينة قبل الإرسال."});
        foreach(var r in audience)db.AcademyNotices.Add(new(){EventKey=body.Kind,Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=r.UserId,AudienceRole="Guardian",TargetKind=body.Kind=="Overdue"?"Receivables":"Renewal",Message=Render(body.Message,r),CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow});
        db.NoticeDispatches.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,ActorMembershipId=t.MembershipId,RequestId=body.RequestId,RequestHash=requestHash,Kind=body.Kind,RecipientCount=audience.Length,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{sent=audience.Length,replay=false});
    }
}
