using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace Academy.Api.Slice3;

public sealed record FamilyRequestWrite(IReadOnlyList<FamilyRenewalChoice>? Lines, string QuoteHash, string IdempotencyKey);
public sealed record FamilyProofWrite(Guid AssetId, int Revision);
public sealed record FamilyCancelWrite(int Revision);
public sealed record FamilyPlacement(Guid LineId, Guid GroupId);
public sealed record FamilyReviewWrite(bool Accept, int Revision, string? Reason, IReadOnlyList<FamilyPlacement>? Placements);

public static class FamilyRequestEndpoints
{
    public static void MapFamilyRenewalRequests(this WebApplication app)
    {
        var guardian = app.MapGroup("/api/v1/guardian/subscriptions/family/requests").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        guardian.MapGet("/", async (FamilyRequestService s) => await Run(s.List));
        guardian.MapGet("/{id:guid}", async (Guid id, FamilyRequestService s) => await Run(() => s.Detail(id, false)));
        guardian.MapPost("/", async (FamilyRequestWrite body, FamilyRequestService s) => await Run(() => s.Create(body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("renewal-search");
        guardian.MapPost("/{id:guid}/proof", async (Guid id, FamilyProofWrite body, FamilyRequestService s) => await Run(() => s.Proof(id, body))).AddEndpointFilter<CsrfFilter>();
        guardian.MapPost("/{id:guid}/cancel", async (Guid id, FamilyCancelWrite body, FamilyRequestService s) => await Run(() => s.Cancel(id, body.Revision))).AddEndpointFilter<CsrfFilter>();
        var staff = app.MapGroup("/api/v1/manage/family-renewals");
        staff.MapGet("/{id:guid}", async (Guid id, FamilyRequestService s) => await Run(() => s.Detail(id, true))).RequireAuthorization(AcademyPermissions.PaymentRead);
        staff.MapPost("/{id:guid}/review", async (Guid id, FamilyReviewWrite body, FamilyRequestService s) => await Run(() => s.Review(id, body))).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { return Results.Ok(await work()); }
        catch (CashRejected e) { return Results.Problem(title: e.Message, statusCode: e.Status); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(title: "تغير الطلب. حدّث الصفحة ثم أعد المحاولة.", statusCode: 409); }
        catch (Exception e) when (e is PostgresException { SqlState: "40001" or "40P01" or "23505" } || e.InnerException is PostgresException { SqlState: "40001" or "40P01" or "23505" }) { return Results.Problem(title: "تغير الطلب أثناء الحفظ. أعد المحاولة بنفس التفاصيل؛ لن يتكرر التحصيل.", statusCode: 409); }
    }
}

public sealed class FamilyRequestService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, FamilyRenewalQuoteService quotes, ISubscriptionClock clock, TimeProvider realClock, CollectionService collections, GroupPlacementService placement, IAuthorizationService authorization)
{
    private async Task<(TenantMembership Tenant, Guid User)> Context(bool staff)
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (!(staff ? t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin : t.Role == AcademyRole.Guardian) || !Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) throw new CashRejected("ليست لديك صلاحية لهذا الطلب.", 403);
        return (t, user);
    }
    private async Task<FamilyRenewalRequest> Find(Guid academy, Guid user, Guid id, bool staff)
    {
        var request = await db.FamilyRenewalRequests.Include(r => r.Guardian).Include(r => r.Lines).ThenInclude(l => l.SportEnrollment).ThenInclude(e => e.Player)
            .Include(r => r.Lines).ThenInclude(l => l.SportEnrollment).ThenInclude(e => e.Sport)
            .Include(r => r.Lines).ThenInclude(l => l.SportEnrollment).ThenInclude(e => e.Branch)
            .SingleOrDefaultAsync(r => r.AcademyId == academy && r.Id == id && (staff || r.RequestedByUserId == user));
        if (request is null || request.Lines.Count != request.LineCount || !staff && !await TrustedFamily(request)) throw new CashRejected("طلب الأسرة غير متاح.", 404);
        return request;
    }
    private async Task<bool> TrustedFamily(FamilyRenewalRequest r)
    {
        var players = r.Lines.Select(l => l.PlayerId).Distinct().ToArray();
        return r.Guardian.IsActive && r.Guardian.UserId == r.RequestedByUserId && await db.GuardianPlayerLinks.CountAsync(l => l.AcademyId == r.AcademyId && l.GuardianId == r.GuardianId && l.IsActive && l.Player.IsActive && players.Contains(l.PlayerId)) == players.Length;
    }
    private Task Lock(Guid academy, Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"family-renewal:{academy}:{id}"}, 0))");
    private async Task LockEnrollments(Guid academy, IEnumerable<Guid> ids)
    {
        foreach (var id in ids.Distinct().Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"renewal-enrollment:{academy:N}:{id:N}"}, 0))");
    }
    private static string Money(decimal value) => CashRegisterService.Money(value);
    public async Task<object> List()
    {
        var (t, user) = await Context(false);
        // Names and line details are returned only after current guardian-link verification.
        var rows = await db.FamilyRenewalRequests.Where(r => r.AcademyId == t.AcademyId && r.RequestedByUserId == user && r.Guardian.IsActive && r.Guardian.UserId == user && !r.Lines.Any(l => !db.GuardianPlayerLinks.Any(g => g.AcademyId == t.AcademyId && g.GuardianId == r.GuardianId && g.PlayerId == l.PlayerId && g.IsActive && g.Player.IsActive))).OrderByDescending(r => r.CreatedAtUtc).Take(100).Select(r => new { r.Id, r.LineCount, r.Currency, r.TotalAmount, r.Status, r.CreatedAtUtc }).ToListAsync();
        return new { items = rows.Select(r => new { r.Id, r.LineCount, r.Currency, amount = Money(r.TotalAmount), status = r.Status.ToString(), r.CreatedAtUtc }).ToArray() };
    }
    public async Task<object> Detail(Guid id, bool staff)
    {
        var (t, user) = await Context(staff); return await View(await Find(t.AcademyId, user, id, staff), staff);
    }
    private async Task<object> View(FamilyRenewalRequest r, bool staff)
    {
        var canReview = false;
        if (staff && (await authorization.AuthorizeAsync(accessor.HttpContext!.User, AcademyPermissions.SubscriptionRenewManage)).Succeeded)
        {
            var t = (await tenant.ResolveAsync())!;
            canReview = t.Role == AcademyRole.AcademyOwner || !await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UseActionGrants) || await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "payments" && g.Read && g.Edit);
        }
        var day = await AttendanceCalendar.TodayAsync(r.AcademyId, db, realClock);
        var ids = r.Lines.Select(l => l.SportEnrollmentId).ToArray();
        var assigned = await db.SportEnrollments.Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, group = FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, day) }).ToDictionaryAsync(e => e.Id, e => e.group);
        var branchIds = r.Lines.Select(l => l.BranchId).Distinct().ToArray(); var sports = r.Lines.Select(l => l.SportEnrollment.SportId).Distinct().ToArray();
        var groups = staff ? await db.TrainingGroups.AsNoTracking().Include(g => g.AgeCategory).Where(g => g.AcademyId == r.AcademyId && g.IsActive && g.Branch.IsActive && g.Sport.IsActive && g.AgeCategory.IsActive && branchIds.Contains(g.BranchId) && sports.Contains(g.SportId)).OrderBy(g => g.ArabicName).ToListAsync() : [];
        var paymentIds = r.Lines.Where(l => l.ResultPaymentId.HasValue).Select(l => l.ResultPaymentId!.Value).ToArray();
        var receipts = await db.Receipts.Where(x => x.AcademyId == r.AcademyId && paymentIds.Contains(x.Collection.PaymentRequestId)).Select(x => new { x.Id, x.Collection.PaymentRequestId }).ToDictionaryAsync(x => x.PaymentRequestId, x => x.Id);
        return new { r.Id, r.Revision, canReview, status = r.Status.ToString(), r.LineCount, r.Currency, originalAmount = Money(r.OriginalAmount), discountAmount = Money(r.DiscountAmount), totalAmount = Money(r.TotalAmount), r.CreatedAtUtc, r.DecidedAtUtc, r.DecisionReason, proofUrl = r.ProofAssetId.HasValue ? $"/api/v1/assets/{r.ProofAssetId}" : null,
            items = r.Lines.OrderBy(l => l.Id).Select(l => new { l.Id, l.SportEnrollmentId, player = l.SportEnrollment.Player.ArabicName, sport = l.SportEnrollment.Sport.ArabicName, branch = l.SportEnrollment.Branch.ArabicName, plan = JsonSerializer.Deserialize<PlanTerms>(l.PlanTermsJson)!.Name, originalAmount = Money(l.OriginalAmount), discountAmount = Money(l.DiscountAmount),discountReason=JsonSerializer.Deserialize<RenewalFinancialSnapshot>(l.FinancialSnapshotJson)?.DiscountReason, finalAmount = Money(l.FinalAmount), l.Currency, groupId = assigned[l.SportEnrollmentId], receiptId = l.ResultPaymentId.HasValue && receipts.TryGetValue(l.ResultPaymentId.Value, out var receipt) ? (Guid?)receipt : null,
                groups = groups.Where(g => g.BranchId == l.BranchId && g.SportId == l.SportEnrollment.SportId && g.RegistrationFormat == l.SportEnrollment.RegistrationFormat && g.AgeCategory.MinimumBirthYear <= l.SportEnrollment.Player.DateOfBirth.Year && g.AgeCategory.MaximumBirthYear >= l.SportEnrollment.Player.DateOfBirth.Year).Select(g => new { g.Id, name = g.ArabicName }).ToArray() }).ToArray() };
    }
    public async Task<object> Create(FamilyRequestWrite body)
    {
        var (t, user) = await Context(false);
        if (body.Lines is not { Count: > 0 and <= 20 } choices || choices.Any(l => l is null || l.SportEnrollmentId == Guid.Empty || l.SubscriptionPlanId == Guid.Empty) || choices.Select(l => l.SportEnrollmentId).Distinct().Count() != choices.Count || body.QuoteHash?.Length != 64 || body.IdempotencyKey is not { Length: >= 16 and <= 80 } key || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new CashRejected("راجع تسجيلات الأسرة والمراجعة ومفتاح الطلب قبل المتابعة.", 422);
        var requestHash = StatementComparisonRules.Hash(new { body.QuoteHash, lines = choices.OrderBy(l => l.SportEnrollmentId).ToArray() });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"family-create:{t.AcademyId}:{user}:{key}"}, 0))");
        var existing = await db.FamilyRenewalRequests.Where(r => r.AcademyId == t.AcademyId && r.RequestedByUserId == user && r.IdempotencyKey == key).Select(r => new { r.Id, r.RequestHash }).SingleOrDefaultAsync();
        if (existing is not null) { if (existing.RequestHash != requestHash) throw new CashRejected("مفتاح الطلب مستخدم باختيارات مختلفة.", 409); return await View(await Find(t.AcademyId, user, existing.Id, false), false); }
        await LockEnrollments(t.AcademyId, choices.Select(c => c.SportEnrollmentId));
        foreach (var plan in choices.Select(c => c.SubscriptionPlanId).Distinct().Order()) await PlanCatalog.LockAsync(db, t.AcademyId, plan);
        var prepared = await quotes.Prepare(new(choices));
        if (prepared.Hash != body.QuoteHash) throw new CashRejected("تغير السعر أو أهلية التجديد. راجع الإجمالي مجددًا قبل إنشاء الطلب.", 409);
        if (prepared.Prices.Values.Any(p => p.FinalAmount <= 0)) throw new CashRejected("أحد التسجيلات معفى بالكامل. تواصل مع الإدارة لاعتماد الإعفاء؛ لا ترسل تحويلًا بقيمة صفر أو ترفعه كتحصيل.", 422);
        if (!await db.AcademyTransferSettings.AnyAsync(s => s.AcademyId == t.AcademyId && s.IsActive && (s.InstaPayAddress != null && s.InstaPayAddress != "" || s.WalletNumber != null && s.WalletNumber != ""))) throw new CashRejected("لم تضبط الأكاديمية حساب التحويل أو المحفظة. تواصل مع الإدارة قبل الدفع.", 422);
        var enrollmentIds = choices.Select(c => c.SportEnrollmentId).ToArray(); await NoOpenRequests(t.AcademyId, enrollmentIds);
        var now = clock.UtcNow;
        var request = new FamilyRenewalRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = prepared.Guardian, RequestedByUserId = user, IdempotencyKey = key, RequestHash = requestHash, QuoteHash = prepared.Hash, LineCount = choices.Count, OriginalAmount = prepared.Prices.Values.Sum(p => p.OriginalAmount), DiscountAmount = prepared.Prices.Values.Sum(p => p.DiscountAmount), TotalAmount = prepared.Prices.Values.Sum(p => p.FinalAmount), Currency = prepared.Lines[0].Plan.Currency, CreatedAtUtc = now, UpdatedAtUtc = now };
        foreach (var line in prepared.Lines) { var price = prepared.Prices[line.EnrollmentId]; request.Lines.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, RequestId = request.Id, SportEnrollmentId = line.EnrollmentId, SubscriptionPlanId = line.Plan.Id, BranchId = line.BranchId, PlayerId = line.PlayerId, OriginalAmount = price.OriginalAmount, DiscountAmount = price.DiscountAmount, FinalAmount = price.FinalAmount, Currency = line.Plan.Currency, FinancialSnapshotJson = JsonSerializer.Serialize(price), PlanTermsJson = JsonSerializer.Serialize(PlanTerms.From(line.Plan)), CreatedAtUtc = now, UpdatedAtUtc = now }); }
        var vipRule=prepared.Prices.Values.Where(p=>p.DiscountRuleId.HasValue).Select(p=>p.DiscountRuleId!.Value).Distinct().ToArray();
        foreach(var rule in vipRule){var use=await VipRewards.Reserve(db,t.AcademyId,rule,"Family",request.Id,user,clock.Today,clock.UtcNow,guardian:prepared.Guardian);if(use.HasValue){if(request.VipRewardUseId.HasValue)throw new VipRewardConflict("لا تجمع مكافأتين في طلب واحد.");request.VipRewardUseId=use;}}
        db.FamilyRenewalRequests.Add(request); await db.SaveChangesAsync(); await tx.CommitAsync();
        return await View(await Find(t.AcademyId, user, request.Id, false), false);
    }
    private async Task NoOpenRequests(Guid academy, Guid[] ids, Guid? except = null)
    {
        if (await db.FamilyRenewalLines.IgnoreQueryFilters().AnyAsync(l => l.AcademyId == academy && ids.Contains(l.SportEnrollmentId) && l.RequestId != except && l.Request.Status == FamilyRenewalStatus.Pending) || await db.RenewalRequests.IgnoreQueryFilters().AnyAsync(r => r.AcademyId == academy && ids.Contains(r.SportEnrollmentId) && (r.Status == RenewalRequestStatus.PendingPayment || r.Status == RenewalRequestStatus.PaymentInProgress) && db.PaymentRequests.IgnoreQueryFilters().Any(p => p.AcademyId == academy && p.Id == r.PaymentRequestId && (p.Status == PaymentRequestStatus.Created || p.Status == PaymentRequestStatus.Pending)))) throw new CashRejected("أحد التسجيلات له طلب دفع مفتوح. أكمل الطلب أو ألغِه قبل تجديده مرة أخرى.", 409);
    }
    public async Task<object> Proof(Guid id, FamilyProofWrite body)
    {
        var (t, user) = await Context(false); await using var tx = await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId, id); var r = await Find(t.AcademyId, user, id, false);
        if (r.Status != FamilyRenewalStatus.Pending) throw new CashRejected("الطلب لم يعد ينتظر إثباتًا.", 409);
        if (r.ProofAssetId == body.AssetId) return await View(r, false);
        if (r.Revision != body.Revision || r.ProofAssetId.HasValue) throw new CashRejected("وصل إثبات لهذا الطلب بالفعل. حدّث الصفحة لمراجعته.", 409);
        if (!await db.PrivateAssets.AnyAsync(a => a.AcademyId == t.AcademyId && a.Id == body.AssetId && a.IsActive && a.Purpose == "Proof" && a.UploadedByUserId == user)) throw new CashRejected("الإثبات غير متاح لهذا الحساب.", 404);
        r.ProofAssetId = body.AssetId; r.Revision++; r.UpdatedAtUtc = clock.UtcNow;
        await Academy.Api.Operations.NoticeText.PaymentProof(db,t.AcademyId,id,"تجديد اشتراكات الأسرة",clock.UtcNow);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(r, false);
    }
    public async Task<object> Cancel(Guid id, int revision)
    {
        var (t, user) = await Context(false); await using var tx = await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId, id); var r = await Find(t.AcademyId, user, id, false);
        if (r.Status == FamilyRenewalStatus.Cancelled) return await View(r, false);
        if (r.Status != FamilyRenewalStatus.Pending || r.Revision != revision) throw new CashRejected("تغير الطلب؛ حدّث الصفحة قبل الإلغاء.", 409);
        r.Status = FamilyRenewalStatus.Cancelled; r.DecidedByUserId = user; r.DecidedAtUtc = clock.UtcNow; r.Revision++; r.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(r, false);
    }
    public async Task<object> Review(Guid id, FamilyReviewWrite body)
    {
        var (t, user) = await Context(true); if (body.Reason?.Length > 500) throw new CashRejected("الملاحظة لا تتجاوز500 حرف.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(t.AcademyId, id); var r = await Find(t.AcademyId, user, id, true);
        var desired = body.Accept ? FamilyRenewalStatus.Approved : FamilyRenewalStatus.Rejected;
        if (r.Status == desired) return await View(r, true);
        if (r.Status != FamilyRenewalStatus.Pending || r.Revision != body.Revision) throw new CashRejected("تغير الطلب. راجع أحدث نسخة قبل اتخاذ القرار.", 409);
        if (body.Accept)
        {
            if (!r.ProofAssetId.HasValue || !await TrustedFamily(r)) throw new CashRejected("راجع إثبات الدفع وروابط الأسرة قبل قبول الطلب كاملًا.", 409);
            if (body.Placements is not { } positions || positions.Count != r.LineCount || positions.Any(p => p is null) || positions.Select(p => p.LineId).Distinct().Count() != r.LineCount || positions.Any(p => !r.Lines.Any(l => l.Id == p.LineId))) throw new CashRejected("اختر مجموعة مناسبة لكل تسجيل في الطلب.", 422);
            await LockEnrollments(t.AcademyId, r.Lines.Select(l => l.SportEnrollmentId)); await NoOpenRequests(t.AcademyId, r.Lines.Select(l => l.SportEnrollmentId).ToArray(), r.Id);
            foreach (var groupId in positions.Select(p => p.GroupId).Distinct().Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{groupId}"}, 0))");
            var day = await AttendanceCalendar.TodayAsync(t.AcademyId, db, realClock);
            foreach (var line in r.Lines.OrderBy(l => l.SportEnrollmentId))
            {
                var e = line.SportEnrollment; var groupId = positions.Single(p => p.LineId == line.Id).GroupId;
                if (!e.IsActive || e.Status != Academy.Infrastructure.People.EnrollmentStatus.Active || !e.Player.IsActive || !e.Branch.IsActive || !e.Sport.IsActive || e.BranchId != line.BranchId || e.PlayerId != line.PlayerId) throw new CashRejected("تغير أحد تسجيلات الأسرة. لا يمكن قبول جزء من الطلب.", 409);
                var assigned = await db.SportEnrollments.Where(x => x.Id == e.Id).Select(x => FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, day)).SingleAsync();
                if (assigned.HasValue && assigned != groupId) throw new CashRejected("قبول الدفع لا ينقل اللاعب إلى مجموعة أخرى. استخدم النقل المؤرخ أولًا.", 409);
                var group = await db.TrainingGroups.Include(g => g.AgeCategory).SingleOrDefaultAsync(g => g.AcademyId == t.AcademyId && g.Id == groupId && g.IsActive && g.Branch.IsActive && g.Sport.IsActive && g.AgeCategory.IsActive);
                var terms = JsonSerializer.Deserialize<PlanTerms>(line.PlanTermsJson)!;
                if (group is null || group.BranchId != line.BranchId || group.SportId != e.SportId || group.RegistrationFormat != e.RegistrationFormat || !PlanCatalog.FormatMatches(e.RegistrationFormat, terms.Format) || group.AgeCategory.MinimumBirthYear > e.Player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < e.Player.DateOfBirth.Year) throw new CashRejected("إحدى المجموعات غير مناسبة للفرع أو الرياضة أو نوع التسجيل أو العمر.", 422);
                if (!assigned.HasValue && !await DatedGroupRoster.CanAdd(db, group.Id, day, group.Capacity)) throw new CashRejected("إحدى المجموعات مكتملة العدد. لا يتم قبول الطلب جزئيًا.", 409);
                var special = group.RegistrationFormat != TrainingRegistrationFormat.Group && e.TrainingGroupId is null;
                if (special) { try { group = await placement.PrepareSpecialAdmission(group.Id); } catch (Academy.Api.Facilities.FacilityRejected error) { throw new CashRejected(error.Message, error.Status); } }
                else if (!e.TrainingGroupId.HasValue) e.TrainingGroupId = group.Id;
                e.UpdatedAtUtc = clock.UtcNow;
                line.ResultPaymentId = await collections.CreateCashAsync(t.AcademyId, user, e.Id, line.SubscriptionPlanId, $"family-{r.Id:N}-{line.Id:N}", "Online", requestedBy: r.RequestedByUserId, fixedSnapshot: JsonSerializer.Deserialize<RenewalFinancialSnapshot>(line.FinancialSnapshotJson), fixedTerms: terms, familyRequestId: r.Id, fixedCurrency: line.Currency);
                line.UpdatedAtUtc = clock.UtcNow;
                if (special) { try { await placement.FinishSpecialAdmission(group, e); } catch (Academy.Api.Facilities.FacilityRejected error) { throw new CashRejected(error.Message, error.Status); } }
            }
        }
        r.Status = desired; r.DecidedByUserId = user; r.DecidedAtUtc = clock.UtcNow; r.DecisionReason = body.Reason?.Trim(); r.Revision++; r.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = r.RequestedByUserId, AudienceRole="Guardian",TargetKind="FamilyRenewal",TargetId=r.Id, Message = body.Accept ? "تم قبول إثبات الأسرة وتجديد كل التسجيلات بنجاح. راجع إيصالات أبنائك ومواعيدهم." : $"لم يُقبل طلب تجديد الأسرة. {r.DecisionReason ?? "تواصل مع إدارة الأكاديمية للتوضيح."}", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(r, true);
    }
}
