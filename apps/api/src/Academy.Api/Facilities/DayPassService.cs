using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice3;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;

public sealed record DayPassChoice(Guid PlayerId, Guid BookingId, Guid PlanId);
public sealed record DayPassOrder(Guid PlayerId, Guid BookingId, Guid PlanId, string QuoteHash);
public sealed record DayPassProof(Guid AssetId);
public sealed record DayPassReview(bool Accept, string? Reason);

public sealed class DayPassService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, TimeProvider time, FacilityService facilities)
{
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private Guid Actor => Guid.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<TenantMembership> Guardian()
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.Guardian || !await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UserId == Actor && m.IsActive && m.Academy.IsActive)) throw new FacilityRejected("خدمة الزيارة غير متاحة لهذا الحساب.", 403);
        return t;
    }
    private async Task Feature(Guid academy)
    {
        var payload = await db.AcademyConfigurationVersions.AsNoTracking().Where(c => c.State == ConfigurationState.Active).Select(c => c.Payload).SingleOrDefaultAsync();
        if (payload is null || !AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(e => e.AcademyId == academy && e.Feature == "facilities" && e.IsActive && e.Enabled)) throw new FacilityRejected("خدمة الزيارة اليومية غير متاحة حاليًا.", 403);
    }
    private async Task<DateOnly> Today(Guid academy)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync());
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
    }
    private async Task LinkedChild(TenantMembership t, Guid playerId)
    {
        if (!await db.GuardianPlayerLinks.AnyAsync(l => l.AcademyId == t.AcademyId && l.PlayerId == playerId && l.IsActive && l.Player.IsActive && l.Guardian.IsActive && l.Guardian.UserId == Actor))
            throw new FacilityRejected("الطفل غير متاح لهذا الحساب.", 404);
    }
    private sealed record Prepared(ResourceBooking Booking, SubscriptionPlan Plan, string Player, string Resource, string Hash);
    private async Task<Prepared> Prepare(DayPassChoice choice, TenantMembership t)
    {
        await Feature(t.AcademyId);
        await LinkedChild(t, choice.PlayerId);
        var booking = await db.ResourceBookings.AsNoTracking().SingleOrDefaultAsync(b => b.AcademyId == t.AcademyId && b.Id == choice.BookingId && b.State == ResourceBookingState.Reserved && b.ServiceKind == nameof(AcademyServiceKind.DayPass) && b.TrainingSessionId == null && b.TrainingGroupId == null) ?? throw new FacilityRejected("موعد الزيارة غير متاح.", 404);
        if (booking.Date < await Today(t.AcademyId)) throw new FacilityRejected("انتهى موعد الزيارة؛ اختر موعدًا جديدًا.");
        var resource = await db.FacilityResources.AsNoTracking().Where(r => r.Id == booking.ResourceId && r.IsActive && r.ApprovedAtUtc != null && r.ServiceKinds.Contains(nameof(AcademyServiceKind.DayPass)) && db.FacilitySpaces.Any(s => s.Id == r.FacilitySpaceId && s.IsActive && db.Facilities.Any(f => f.Id == s.FacilityId && f.IsActive))).Select(r => new { r.Name, r.FacilitySpaceId }).SingleOrDefaultAsync() ?? throw new FacilityRejected("المورد غير متاح.", 404);
        await SwimmingPolicyGate.ForResource(db, booking.ResourceId, nameof(AcademyServiceKind.DayPass));
        var sport = await db.FacilitySpaces.Where(s => s.Id == resource.FacilitySpaceId).Select(s => s.SportId).SingleAsync();
        var plan = await db.SubscriptionPlans.AsNoTracking().ForBranch(booking.BranchId).SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == choice.PlanId && p.ServiceKind == AcademyServiceKind.DayPass && p.SportId == sport && p.IsActive && p.Price > 0 && p.Currency == db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.DefaultCurrency).Single()) ?? throw new FacilityRejected("باقة الزيارة غير منشورة لهذا الفرع والرياضة.", 404);
        var player = await db.Players.Where(p => p.Id == choice.PlayerId).Select(p => p.ArabicName).SingleAsync();
        var hash = Hash(new { t.AcademyId, choice.PlayerId, BookingId=booking.Id, booking.Version, booking.Date, booking.StartTime, booking.EndTime, booking.ResourceId, booking.ReservedSeats, PlanId=plan.Id, plan.Revision, plan.Price, plan.Currency });
        return new(booking, plan, player, resource.Name, hash);
    }
    public async Task<object> Options()
    {
        var t = await Guardian(); await Feature(t.AcademyId); var today = await Today(t.AcademyId);
        var children = await db.GuardianPlayerLinks.AsNoTracking().Where(l => l.Guardian.UserId == Actor && l.IsActive && l.Player.IsActive).Select(l => new { id = l.PlayerId, name = l.Player.ArabicName }).OrderBy(x => x.name).ToArrayAsync();
        var bookings = await (from b in db.ResourceBookings.AsNoTracking() join r in db.FacilityResources on b.ResourceId equals r.Id join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id
            where b.State == ResourceBookingState.Reserved && b.ServiceKind == nameof(AcademyServiceKind.DayPass) && b.Date >= today && b.Date <= today.AddDays(30) && r.IsActive && r.ApprovedAtUtc != null && s.IsActive
                && (s.Kind != FacilitySpaceKind.Pool || db.Sports.Any(sp => sp.Id == s.SportId && sp.Kind == Academy.Infrastructure.Structure.SportKind.Swimming && sp.IsActive)
                    && db.SwimmingBranchPolicies.Any(p => p.SportId == s.SportId && p.BranchId == b.BranchId && p.IsActive && p.Enabled && p.DayPassEnabled))
            orderby b.Date,b.StartTime select new { b.Id,b.BranchId,b.Date,b.StartTime,b.EndTime,b.ReservedSeats,resource=r.Name,sportId=s.SportId }).Take(50).ToArrayAsync();
        var result = new List<object>();
        foreach (var b in bookings)
        {
            var plans = await db.SubscriptionPlans.AsNoTracking().ForBranch(b.BranchId).Where(p => p.IsActive && p.ServiceKind == AcademyServiceKind.DayPass && p.SportId == b.sportId && p.Price > 0)
                .OrderBy(p => p.DisplayOrder).Select(p => new { p.Id, name=p.ArabicName, amount=p.Price.ToString("0.00",CultureInfo.InvariantCulture),p.Currency }).ToArrayAsync();
            if (plans.Length > 0) result.Add(new { bookingId=b.Id,b.Date,b.StartTime,b.EndTime,b.resource,b.ReservedSeats,plans });
        }
        return new { children, bookings=result };
    }
    public async Task<object> Quote(DayPassChoice choice)
    {
        var t = await Guardian(); var p = await Prepare(choice,t);
        var transfer = await db.AcademyTransferSettings.AsNoTracking().SingleOrDefaultAsync(s => s.AcademyId == t.AcademyId && s.IsActive);
        return new { p.Player, bookingDate=p.Booking.Date,p.Booking.StartTime,p.Booking.EndTime,p.Resource,plan=p.Plan.ArabicName,amount=Money(p.Plan.Price),p.Plan.Currency,quoteHash=p.Hash,transfer=new { transfer?.InstaPayAddress,transfer?.WalletNumber,transfer?.RecipientName },proofRequired=true };
    }
    public async Task<object> Order(DayPassOrder body,string key)
    {
        var t = await Guardian(); if (key.Length is <8 or >100 || body.QuoteHash?.Length !=64) throw new FacilityRejected("راجع الزيارة والمبلغ قبل إنشاء الطلب.",422);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"day-pass:{t.AcademyId}:{Actor}:{key}"},0))");
        var requestHash = Hash(new { body.PlayerId,body.BookingId,body.PlanId,body.QuoteHash });
        var existing = await db.DayPassTickets.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == Actor && x.RequestKey == key);
        if (existing is not null) { await LinkedChild(t, existing.PlayerId); if (existing.RequestHash != requestHash) throw new FacilityRejected("معرف الطلب مرتبط بزيارة مختلفة."); return View(existing,true); }
        await PlanCatalog.LockAsync(db,t.AcademyId,body.PlanId);
        var p = await Prepare(new(body.PlayerId,body.BookingId,body.PlanId),t);
        if (p.Hash != body.QuoteHash) throw new FacilityRejected("تغير السعر أو الموعد؛ راجع المبلغ من جديد.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"day-pass-order:{t.AcademyId}:{body.PlayerId}:{body.BookingId}"},0))");
        if (await db.DayPassTickets.AnyAsync(x=>x.PlayerId==body.PlayerId&&x.ResourceBookingId==body.BookingId&&x.Status!=DayPassTicketStatus.Rejected))
            throw new FacilityRejected("لهذا الطفل طلب زيارة قائم في الموعد نفسه؛ راجع قائمته قبل إنشاء طلب آخر.");
        if (!await db.AcademyTransferSettings.AnyAsync(s => s.AcademyId == t.AcademyId && s.IsActive && (s.InstaPayAddress != null && s.InstaPayAddress != "" || s.WalletNumber != null && s.WalletNumber != ""))) throw new FacilityRejected("لم تضبط الأكاديمية وسيلة التحويل بعد؛ تواصل مع الإدارة.",422);
        var row = new DayPassTicket { Id=Guid.NewGuid(),AcademyId=t.AcademyId,BranchId=p.Booking.BranchId,ResourceBookingId=p.Booking.Id,PlayerId=body.PlayerId,SubscriptionPlanId=p.Plan.Id,PlanRevision=p.Plan.Revision,PlanName=p.Plan.ArabicName,Amount=p.Plan.Price,Currency=p.Plan.Currency,RequestedByUserId=Actor,RequestKey=key,RequestHash=requestHash,CreatedAtUtc=time.GetUtcNow(),UpdatedAtUtc=time.GetUtcNow() };
        db.DayPassTickets.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return View(row,false);
    }
    private static object View(DayPassTicket row,bool replay=false) => new { row.Id,row.PlayerId,row.ResourceBookingId,row.Status,row.PlanName,amount=Money(row.Amount),row.Currency,row.ProofAssetId,row.ReviewReason,row.ReviewedAtUtc,replay };
    public async Task<object> Mine()
    {
        var t=await Guardian(); await Feature(t.AcademyId);
        return await db.DayPassTickets.AsNoTracking().Where(x=>x.AcademyId==t.AcademyId&&x.RequestedByUserId==Actor&&db.GuardianPlayerLinks.Any(l=>l.PlayerId==x.PlayerId&&l.Guardian.UserId==Actor&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive))
            .OrderByDescending(x=>x.CreatedAtUtc).Take(100).Select(x=>new { x.Id,x.PlayerId,x.ResourceBookingId,x.Status,x.PlanName,amount=x.Amount.ToString("0.00",CultureInfo.InvariantCulture),x.Currency,x.ProofAssetId,x.ReviewReason,x.ReviewedAtUtc }).ToArrayAsync();
    }
    public async Task<object> Proof(Guid id,DayPassProof body)
    {
        var t=await Guardian(); await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"day-pass-review:{t.AcademyId}:{id}"},0))");
        var row=await db.DayPassTickets.SingleOrDefaultAsync(x=>x.Id==id&&x.RequestedByUserId==Actor)??throw new FacilityRejected("الطلب غير متاح.",404);
        await LinkedChild(t, row.PlayerId);
        if (!await db.PrivateAssets.AnyAsync(a=>a.Id==body.AssetId&&a.AcademyId==t.AcademyId&&a.IsActive&&a.Purpose=="Proof"&&a.UploadedByUserId==Actor)) throw new FacilityRejected("الإثبات غير متاح.",404);
        if(row.Status==DayPassTicketStatus.UnderReview&&row.ProofAssetId==body.AssetId)return View(row,true);
        if(row.Status!=DayPassTicketStatus.PendingProof)throw new FacilityRejected("الطلب قيد المراجعة أو حُسم بالفعل.");
        row.ProofAssetId=body.AssetId;row.Status=DayPassTicketStatus.UnderReview;row.UpdatedAtUtc=time.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return View(row);
    }
    private async Task<TenantMembership> StaffReview()
    {
        var (t,_) = await facilities.Authority("Edit");
        if(t.Role==AcademyRole.AcademyAdmin)
        {
            var member=await db.AcademyMemberships.AsNoTracking().SingleAsync(m=>m.Id==t.MembershipId);
            if(member.UseActionGrants)
            {
                var grants=await db.StaffActionGrants.Where(g=>g.MembershipId==member.Id&&g.IsActive&&(g.Module=="finance"||g.Module=="payments")).ToArrayAsync();
                if(!grants.Any(g=>g.Module=="finance"&&g.Add&&g.Read)||!grants.Any(g=>g.Module=="payments"&&g.Edit&&g.Read))throw new FacilityRejected("مراجعة تحصيل الزيارة تتطلب تفويض المالية والمدفوعات.",403);
            }
            else if(!await db.StaffGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.Permission==AcademyPermissions.CollectionRead&&g.IsActive&&g.Allowed)
                ||!await db.StaffGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.Permission==AcademyPermissions.PaymentRead&&g.IsActive&&g.Allowed))throw new FacilityRejected("مراجعة الزيارة تتطلب تفويض التحصيل والمدفوعات.",403);
        }
        return t;
    }
    public async Task<object> Queue()
    {
        var (t,_) = await facilities.Authority("Read");
        if(t.Role==AcademyRole.AcademyAdmin)
        {
            var member=await db.AcademyMemberships.AsNoTracking().SingleAsync(m=>m.Id==t.MembershipId);
            var allowed=member.UseActionGrants
                ? await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.IsActive&&g.Module=="payments"&&g.Read)
                : await db.StaffGrants.AnyAsync(g=>g.MembershipId==member.Id&&g.Permission==AcademyPermissions.PaymentRead&&g.IsActive&&g.Allowed);
            if(!allowed) throw new FacilityRejected("عرض إثباتات الزيارة يتطلب صلاحية المدفوعات.",403);
        }
        return await db.DayPassTickets.AsNoTracking().Where(x=>x.AcademyId==t.AcademyId&&x.Status==DayPassTicketStatus.UnderReview).OrderBy(x=>x.CreatedAtUtc).Take(100)
            .Select(x=>new { x.Id,x.PlayerId,player=db.Players.Where(p=>p.Id==x.PlayerId).Select(p=>p.ArabicName).FirstOrDefault(),x.ResourceBookingId,x.BranchId,x.PlanName,amount=x.Amount.ToString("0.00",CultureInfo.InvariantCulture),x.Currency,x.ProofAssetId,x.CreatedAtUtc }).ToArrayAsync();
    }
    public async Task<object> Review(Guid id,DayPassReview body)
    {
        var t=await StaffReview(); if(body.Reason?.Length>500||!body.Accept&&string.IsNullOrWhiteSpace(body.Reason))throw new FacilityRejected("اكتب سبب الرفض أو اختصر الملاحظة.",422);
        await using var tx=await db.Database.BeginTransactionAsync();
        // Keep the same resource-first lock order as booking cancellation.
        var initial=await db.DayPassTickets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id)??throw new FacilityRejected("التذكرة غير متاحة.",404);
        var resourceId=await db.ResourceBookings.Where(b=>b.Id==initial.ResourceBookingId).Select(b=>b.ResourceId).SingleAsync();
        var resource=await facilities.Lock(resourceId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"day-pass-review:{t.AcademyId}:{id}"},0))");
        var row=await db.DayPassTickets.SingleOrDefaultAsync(x=>x.Id==id)??throw new FacilityRejected("التذكرة غير متاحة.",404);
        if(row.Status is DayPassTicketStatus.Confirmed or DayPassTicketStatus.Rejected)
        {
            if ((row.Status==DayPassTicketStatus.Confirmed)!=body.Accept) throw new FacilityRejected("حُسم الطلب بقرار مختلف؛ أعد تحميل القائمة.");
            return View(row,true);
        }
        if(row.Status!=DayPassTicketStatus.UnderReview||!row.ProofAssetId.HasValue)throw new FacilityRejected("يلزم إثبات دفع قبل مراجعة الطلب.");
        if(!await db.PrivateAssets.AnyAsync(a=>a.Id==row.ProofAssetId&&a.AcademyId==t.AcademyId&&a.IsActive&&a.Purpose=="Proof"))throw new FacilityRejected("ملف الإثبات لم يعد متاحًا؛ أوقف المراجعة.");
        if(body.Accept)
        {
            // The resource lock also serializes booking cancellation and another ticket review.
            var booking=await db.ResourceBookings.SingleAsync(b=>b.Id==row.ResourceBookingId);
            if(booking.State!=ResourceBookingState.Reserved||booking.ServiceKind!=nameof(AcademyServiceKind.DayPass)||booking.Date<await Today(t.AcademyId)||!resource.IsActive||resource.ApprovedAtUtc==null||!resource.ServiceKinds.Contains(nameof(AcademyServiceKind.DayPass))
                ||await db.ResourceClosures.AnyAsync(c=>c.ResourceId==resource.Id&&c.IsActive&&c.FromDate<=booking.Date&&c.ToDate>=booking.Date&&c.StartTime<booking.EndTime&&booking.StartTime<c.EndTime)
                ||await db.DayPassTickets.CountAsync(x=>x.ResourceBookingId==booking.Id&&x.Status==DayPassTicketStatus.Confirmed)>=booking.ReservedSeats)throw new FacilityRejected("الموعد أو السعة لم تعد متاحة؛ لا تعتمد التحويل قبل تسوية العميل.");
            row.Status=DayPassTicketStatus.Confirmed;
        }
        else row.Status=DayPassTicketStatus.Rejected;
        row.ReviewedByUserId=Actor;row.ReviewedAtUtc=row.UpdatedAtUtc=time.GetUtcNow();row.ReviewReason=body.Reason?.Trim();
        db.AcademyNotices.Add(new AcademyNotice{Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=row.RequestedByUserId,Message=body.Accept?"تم اعتماد تذكرة الزيارة اليومية. اعرضها عند الدخول بعد التحقق من الهوية.":"لم يُقبل إثبات الزيارة اليومية: "+row.ReviewReason,CreatedAtUtc=time.GetUtcNow(),UpdatedAtUtc=time.GetUtcNow()});
        await db.SaveChangesAsync();await tx.CommitAsync();return View(row);
    }
}
