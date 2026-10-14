using System.Security.Claims;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record OperatingExpenseWrite(Guid BranchId, OperatingExpenseScope Scope, Guid TargetId, string Category,
    DateOnly PeriodFrom, DateOnly PeriodTo, DateOnly? PaidOn,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Currency, string? Note);
public sealed record OperatingExpensePost(string QuoteHash);
public sealed class OperatingExpenseRejected(string message, int status = 409) : Exception(message) { public int Status { get; } = status; }

public static class OperatingExpenseEndpoints
{
    public static void MapOperatingExpenses(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/operating-expenses").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/options", async (OperatingExpenseService service) => await Run(service.Options));
        api.MapGet("/allocations", async (Guid? groupId, DateOnly? from, DateOnly? to, OperatingExpenseService service) => await Run(() => service.Allocations(groupId, from, to)));
        api.MapGet("/", async (DateOnly? from, DateOnly? to, int? page, Guid? branchId, string? status, bool? unpaid, OperatingExpenseService service) => await Run(() => service.List(from, to, page, branchId, status, unpaid)));
        api.MapGet("/{id:guid}", async (Guid id, OperatingExpenseService service) => await Run(() => service.Detail(id)));
        api.MapPost("/", async (OperatingExpenseWrite body, HttpRequest request, OperatingExpenseService service) => await Run(() => service.Create(body, request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/approve", async (Guid id, OperatingExpenseService service) => await Run(() => service.Approve(id))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/quote", async (Guid id, OperatingExpenseService service) => await Run(() => service.Quote(id))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/post", async (Guid id, OperatingExpensePost body, HttpRequest request, OperatingExpenseService service) => await Run(() => service.Post(id, body, request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { return Results.Ok(await work()); }
        catch (OperatingExpenseRejected e) { return Results.Problem(title: e.Message, statusCode: e.Status); }
    }
}

public sealed class OperatingExpenseService(FoundationDbContext db, CurrentTenant current, IHttpContextAccessor accessor, TimeProvider time)
{
    private static readonly HashSet<string> Categories = ["Facility", "Electricity", "Water", "Heating", "Maintenance", "Rent", "Supplies", "Administration", "Other"];
    private async Task<(TenantMembership Membership, Guid Actor)> Owner()
    {
        var t = await current.ResolveAsync() ?? throw new OperatingExpenseRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.AcademyOwner) throw new OperatingExpenseRejected("المصروفات التشغيلية متاحة للمالك فقط.", 403);
        var claim = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var actor) || !await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.UserId == actor && m.IsActive && m.Role == AcademyRole.AcademyOwner))
            throw new OperatingExpenseRejected("عضوية المالك غير متاحة.", 403);
        return (t, actor);
    }
    private static void Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 100) throw new OperatingExpenseRejected("معرف العملية مطلوب.", 422);
    }
    private async Task<OperatingExpense> Find(Guid academy, Guid id) => await db.OperatingExpenses.SingleOrDefaultAsync(x => x.AcademyId == academy && x.Id == id)
        ?? throw new OperatingExpenseRejected("المصروف غير متاح.", 404);

    public async Task<object> Options()
    {
        var (t, _) = await Owner();
        return new { currency = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.DefaultCurrency).SingleAsync(), categories = Categories.OrderBy(x => x).ToArray() };
    }

    public async Task<object> List(DateOnly? from, DateOnly? to, int? page, Guid? branchId = null, string? status = null, bool? unpaid = null)
    {
        var (t, _) = await Owner();
        if (page is < 1 or > 10000 || from > to) throw new OperatingExpenseRejected("راجع فترة التقرير والصفحة.", 422);
        if (branchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == branchId)) throw new OperatingExpenseRejected("الفرع غير متاح.", 404);
        if (status is not null && (!Enum.TryParse<OperatingExpenseStatus>(status, out var parsed) || !Enum.IsDefined(parsed))) throw new OperatingExpenseRejected("حالة المصروف غير صالحة.", 422);
        var query = db.OperatingExpenses.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (from.HasValue) query = query.Where(x => x.PeriodTo >= from.Value);
        if (to.HasValue) query = query.Where(x => x.PeriodFrom <= to.Value);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId);
        if (status is not null) { var selected = Enum.Parse<OperatingExpenseStatus>(status); query = query.Where(x => x.Status == selected); }
        if (unpaid == true) query = query.Where(x => x.PaidOn == null && (x.Status == OperatingExpenseStatus.Approved || x.Status == OperatingExpenseStatus.Posted));
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(((page ?? 1) - 1) * 50).Take(50)
            .Select(x => new { x.Id, x.BranchId, x.Scope, x.TrainingGroupId, x.ResourceId, x.Category, x.PeriodFrom, x.PeriodTo, x.PaidOn, x.Amount, x.Currency, x.Status, x.CreatedAtUtc }).ToArrayAsync();
        return new { count, page = page ?? 1, items = rows };
    }
    public async Task<object> Detail(Guid id)
    {
        var (t, _) = await Owner(); var row = await Find(t.AcademyId, id);
        var shares = await db.OperatingExpenseAllocations.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.ExpenseId == id)
            .OrderBy(x => x.ServiceKind).ThenBy(x => x.TrainingGroupId).Select(x => new { x.ServiceKind, x.TrainingGroupId, x.Weight, x.Amount, x.Currency }).ToArrayAsync();
        return new { expense = row, allocations = shares, allocationTotal = shares.Sum(x => x.Amount) };
    }
    public async Task<object> Allocations(Guid? groupId, DateOnly? from, DateOnly? to)
    {
        var (t, _) = await Owner();
        if (from > to) throw new OperatingExpenseRejected("راجع فترة التقرير.", 422);
        var query = from a in db.OperatingExpenseAllocations.AsNoTracking()
                    join e in db.OperatingExpenses.AsNoTracking() on a.ExpenseId equals e.Id
                    where a.AcademyId == t.AcademyId && e.AcademyId == t.AcademyId && e.Status == OperatingExpenseStatus.Posted
                    select new { a.ServiceKind, a.TrainingGroupId, a.Amount, a.Currency, a.Weight, a.ExpenseId, e.Category, e.BranchId, e.PeriodFrom, e.PeriodTo, expenseAmount = e.Amount };
        if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId.Value);
        if (from.HasValue) query = query.Where(x => x.PeriodTo >= from.Value);
        if (to.HasValue) query = query.Where(x => x.PeriodFrom <= to.Value);
        var count = await query.CountAsync(); var total = await query.SumAsync(x => x.Amount);
        var items = await query.OrderBy(x => x.PeriodFrom).ThenBy(x => x.ExpenseId).Take(500).ToArrayAsync();
        return new { count, items, total, truncated = count > items.Length };
    }
    public async Task<object> Create(OperatingExpenseWrite body, string key)
    {
        var (t, actor) = await Owner(); Key(key);
        if (body.BranchId == Guid.Empty || body.TargetId == Guid.Empty || !Categories.Contains(body.Category) || body.PeriodFrom == default || body.PeriodTo == default || body.PeriodFrom > body.PeriodTo || (body.PaidOn.HasValue && body.PaidOn.Value == default)
            || body.Amount <= 0 || body.Amount > 999999999m || decimal.Round(body.Amount, 2) != body.Amount || body.Currency is { Length: not 3 } || body.Note?.Trim().Length > 400
            || body.Scope is not (OperatingExpenseScope.Group or OperatingExpenseScope.Resource)) throw new OperatingExpenseRejected("راجع بيانات المصروف والفترة والمبلغ.", 422);
        var currency = await db.Academies.Where(a => a.Id == t.AcademyId && a.IsActive).Select(a => a.DefaultCurrency).SingleAsync();
        if (body.Currency is not null && !string.Equals(body.Currency, currency, StringComparison.Ordinal)) throw new OperatingExpenseRejected("عملة المصروف يجب أن تطابق عملة الأكاديمية في هذه المرحلة.", 422);
        if (!await db.Branches.AnyAsync(b => b.Id == body.BranchId && b.AcademyId == t.AcademyId)) throw new OperatingExpenseRejected("الفرع غير متاح.", 404);
        var targetValid = body.Scope == OperatingExpenseScope.Group
            ? await db.TrainingGroups.AnyAsync(g => g.Id == body.TargetId && g.AcademyId == t.AcademyId && g.BranchId == body.BranchId)
            : await db.FacilityResources.AnyAsync(r => r.Id == body.TargetId && r.AcademyId == t.AcademyId && r.BranchId == body.BranchId);
        if (!targetValid) throw new OperatingExpenseRejected("هدف المصروف لا يتبع هذا الفرع.", 404);
        var normalized = body with { Category = body.Category.Trim(), Currency = currency, Note = body.Note?.Trim() };
        var hash = StatementComparisonRules.Hash(normalized);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"operating-expense:{t.AcademyId}:{actor}:{key}"},0))");
        var prior = await db.OperatingExpenses.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.CreatedByUserId == actor && x.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new OperatingExpenseRejected("معرف العملية مستخدم بتفاصيل مختلفة."); return new { prior.Id, replay = true }; }
        var at = time.GetUtcNow();
        var row = new OperatingExpense { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = body.BranchId, Scope = body.Scope,
            TrainingGroupId = body.Scope == OperatingExpenseScope.Group ? body.TargetId : null, ResourceId = body.Scope == OperatingExpenseScope.Resource ? body.TargetId : null,
            Category = normalized.Category, PeriodFrom = body.PeriodFrom, PeriodTo = body.PeriodTo, PaidOn = body.PaidOn, Amount = body.Amount, Currency = currency,
            Note = normalized.Note, CreatedByUserId = actor, RequestKey = key, RequestHash = hash, CreatedAtUtc = at, UpdatedAtUtc = at };
        db.OperatingExpenses.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replay = false };
    }
    public async Task<object> Approve(Guid id)
    {
        var (t, actor) = await Owner(); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"OperatingExpenses\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE");
        var row = await Find(t.AcademyId, id);
        if (row.Status == OperatingExpenseStatus.Draft) { row.Status = OperatingExpenseStatus.Approved; row.ApprovedByUserId = actor; row.ApprovedAtUtc = row.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); }
        else if (row.Status != OperatingExpenseStatus.Approved) throw new OperatingExpenseRejected("لا يمكن اعتماد هذه الحالة.");
        await tx.CommitAsync(); return new { row.Id, row.Status };
    }
    private async Task<(IReadOnlyList<ExpenseShare> Shares, string Hash)> Prepare(OperatingExpense row)
    {
        if (row.Status != OperatingExpenseStatus.Approved) throw new OperatingExpenseRejected("اعتمد المصروف قبل الترحيل.");
        IReadOnlyList<ExpenseShare> shares;
        object? bookingInputs = null;
        if (row.Scope == OperatingExpenseScope.Group) shares = OperatingExpenseAllocation.Equal(row.Amount, [row.TrainingGroupId!.Value]);
        else
        {
            var bookings = await db.ResourceBookings.AsNoTracking().Where(b => b.AcademyId == row.AcademyId && b.ResourceId == row.ResourceId && b.BranchId == row.BranchId
                && b.Date >= row.PeriodFrom && b.Date <= row.PeriodTo && b.State == ResourceBookingState.Reserved)
                .Select(b => new { b.Id, b.TrainingGroupId, b.ServiceKind, b.Date, b.StartTime, b.EndTime, b.Version }).ToArrayAsync();
            if (bookings.Any(b => b.EndTime <= b.StartTime)) throw new OperatingExpenseRejected("بيانات مدة حجز المرفق غير صالحة للتوزيع.");
            bookingInputs = bookings.OrderBy(b => b.Id).ToArray();
            try { shares = OperatingExpenseAllocation.ByServiceMinutes(row.Amount, bookings.Select(b =>
                new ExpenseBooking(b.Id, b.ServiceKind, b.TrainingGroupId, (int)(b.EndTime.ToTimeSpan() - b.StartTime.ToTimeSpan()).TotalMinutes))); }
            catch (ArgumentException) { throw new OperatingExpenseRejected("بيانات حجز المرفق غير صالحة للتوزيع؛ راجع الخدمة والمدة.", 422); }
        }
        var hash = StatementComparisonRules.Hash(new { row.AcademyId, row.Id, row.Version, row.BranchId, row.Scope, row.ResourceId, row.TrainingGroupId,
            row.PeriodFrom, row.PeriodTo, row.Amount, row.Currency, bookingInputs, shares });
        return (shares, hash);
    }
    public async Task<object> Quote(Guid id)
    {
        var (t, _) = await Owner(); var row = await Find(t.AcademyId, id); var (shares, hash) = await Prepare(row);
        return new { quoteHash = hash, shares, total = shares.Sum(s => s.Amount), row.Currency,
            method = row.Scope == OperatingExpenseScope.Group ? "مجموعة مباشرة" : "دقائق الحجوزات بين الخدمات، ثم بالتساوي بين مجموعات التدريب",
            warning = shares.Count == 1 && shares[0].ServiceKind == "Unallocated" ? "لا توجد حجوزات مؤهلة؛ سيبقى المصروف غير موزع." : null };
    }
    public async Task<object> Post(Guid id, OperatingExpensePost body, string key)
    {
        var (t, actor) = await Owner(); Key(key);
        if (body.QuoteHash?.Length != 64) throw new OperatingExpenseRejected("راجع التوزيع قبل الترحيل.", 422);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"OperatingExpenses\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE");
        var row = await Find(t.AcademyId, id);
        if (row.Status == OperatingExpenseStatus.Posted)
        {
            if (row.PostRequestKey != key || row.PostQuoteHash != body.QuoteHash) throw new OperatingExpenseRejected("المصروف مرحّل مسبقًا؛ لا تكرر التكلفة.");
            return new { row.Id, replay = true };
        }
        if (row.Scope == OperatingExpenseScope.Resource)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-resource:{t.AcademyId}:{row.ResourceId}"},0))");
        var (shares, hash) = await Prepare(row);
        if (hash != body.QuoteHash) throw new OperatingExpenseRejected("تغيرت بيانات الحجز أو المصروف؛ راجع التوزيع مرة أخرى.");
        var at = time.GetUtcNow();
        foreach (var share in shares) db.OperatingExpenseAllocations.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ExpenseId = row.Id,
            TrainingGroupId = share.GroupId, ServiceKind = share.ServiceKind, Weight = share.Weight, Amount = share.Amount, Currency = row.Currency, CreatedAtUtc = at, UpdatedAtUtc = at });
        row.Status = OperatingExpenseStatus.Posted; row.PostedByUserId = actor; row.PostedAtUtc = row.UpdatedAtUtc = at; row.PostRequestKey = key; row.PostQuoteHash = hash;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replay = false };
    }
}
