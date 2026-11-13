using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Platform;

public static class PlatformSetup
{
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapPlatformSetup(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/platform/academies").RequireAuthorization(PlatformIdentity.Policy);
        api.MapGet("/directory", Directory);
        api.MapGet("/{id:guid}/setup", Detail);
        api.MapPut("/{id:guid}/subscription", SaveSubscription).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/setup", Save).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/structure", Structure).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/prepare-operation", PrepareOperation).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/configuration", Configure).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/state", State).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/{id:guid}/logo", async (Guid id, FoundationDbContext db, HttpResponse response) => {
            var x = await db.AcademyExperiences.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == id);
            response.Headers.CacheControl = "private, no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return x?.Logo is null ? Results.NotFound() : Results.File(x.Logo, x.LogoContentType!);
        });
    }
    private static async Task<IResult> Directory(FoundationDbContext db, string? search, string? state, string? commercial, string? currency, string? period, string? sort, string? direction, int page = 1, int pageSize = 15)
    {
        if (page < 1 || pageSize is < 1 or > 50 || search?.Length > 120 || state is not (null or "" or "Draft" or "Active" or "Suspended") || commercial is not (null or "" or "Unspecified" or "Trial" or "Subscribed" or "Expired") || sort is not (null or "" or "name" or "players" or "branches" or "sports" or "fee") || direction is not (null or "" or "asc" or "desc")) return Results.UnprocessableEntity(new { message = "راجع البحث والفلاتر وحجم الصفحة." });
        if (sort == "fee" && (currency is not ("EGP" or "USD" or "SAR" or "AED") || period is not ("Monthly" or "Yearly"))) return Results.UnprocessableEntity(new { message = "اختر عملة وفترة فوترة واحدة لترتيب رسوم المنصة بصورة عادلة." });
        var query = from a in db.Academies.AsNoTracking()
                    join profile in db.AcademyPlatformProfiles on a.Id equals profile.AcademyId into profiles
                    from p in profiles.DefaultIfEmpty()
                    select new {
                        a.Id, a.Slug, a.ArabicName, a.CreatedAtUtc,
                        displayName = db.AcademyExperiences.Where(x => x.AcademyId == a.Id).Select(x => x.DisplayName).FirstOrDefault() ?? a.ArabicName,
                        hasLogo = db.AcademyExperiences.Any(x => x.AcademyId == a.Id && x.Logo != null),
                        ownerName = db.AcademyMemberships.Where(m => m.AcademyId == a.Id && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive).OrderBy(m => m.CreatedAtUtc).Select(m => m.User.DisplayName).FirstOrDefault(),
                        ownerPhone = db.AcademyMemberships.Where(m => m.AcademyId == a.Id && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive).OrderBy(m => m.CreatedAtUtc).Select(m => m.User.PhoneNumber).FirstOrDefault(),
                        location = p == null ? "" : p.Location,
                        state = a.IsActive ? "Active" : p != null && p.OperationalState == "Suspended" ? "Suspended" : "Draft",
                        commercialState = p == null ? "Unspecified" : p.CommercialState,
                        fee = p == null ? (decimal?)null : p.PlatformFee,
                        feeCurrency = p == null ? "EGP" : p.FeeCurrency,
                        billingPeriod = p == null ? "Monthly" : p.BillingPeriod,
                        subscriptionTo = p == null ? (DateOnly?)null : p.SubscriptionTo,
                        branches = db.Branches.Count(b => b.AcademyId == a.Id && b.IsActive),
                        sports = db.Sports.Count(s => s.AcademyId == a.Id && s.IsActive),
                        players = db.Players.Where(x => x.AcademyId == a.Id).Select(x => x.Id).Distinct().Count()
                    };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totals = new { all = await query.CountAsync(), active = await query.CountAsync(x => x.state == "Active"), preparing = await query.CountAsync(x => x.state == "Draft"), suspended = await query.CountAsync(x => x.state == "Suspended"), reminders = await query.CountAsync(x => x.subscriptionTo != null && x.subscriptionTo <= today.AddDays(14)) };
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); var phone = EgyptPhoneNormalizer.Normalize(value); query = query.Where(x => x.displayName.Contains(value) || x.Slug.Contains(value) || x.ownerName != null && x.ownerName.Contains(value) || x.ownerPhone != null && (x.ownerPhone.Contains(value) || phone != null && x.ownerPhone == phone)); }
        if (!string.IsNullOrEmpty(state)) query = query.Where(x => x.state == state);
        if (!string.IsNullOrEmpty(commercial)) query = query.Where(x => x.commercialState == commercial);
        if (!string.IsNullOrEmpty(currency)) query = query.Where(x => x.feeCurrency == currency);
        if (!string.IsNullOrEmpty(period)) query = query.Where(x => x.billingPeriod == period);
        var count = await query.CountAsync(); var desc = direction == "desc";
        var ordered = sort switch {
            "players" => desc ? query.OrderByDescending(x => x.players) : query.OrderBy(x => x.players),
            "branches" => desc ? query.OrderByDescending(x => x.branches) : query.OrderBy(x => x.branches),
            "sports" => desc ? query.OrderByDescending(x => x.sports) : query.OrderBy(x => x.sports),
            "fee" => desc ? query.OrderByDescending(x => x.fee) : query.OrderBy(x => x.fee),
            _ => desc ? query.OrderByDescending(x => x.displayName) : query.OrderBy(x => x.displayName)
        };
        return Results.Ok(new { totals, count, page, pageSize, items = await ordered.ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() });
    }
    private static async Task<AcademyPlatformProfile> Profile(FoundationDbContext db, Guid id)
    {
        var p = await db.AcademyPlatformProfiles.SingleOrDefaultAsync(p => p.AcademyId == id);
        if (p is not null) return p;
        p = new() { AcademyId = id, EditVersion = 0, OperationalState = await db.Academies.Where(a => a.Id == id).Select(a => a.IsActive ? "Active" : "Draft").SingleAsync() };
        db.AcademyPlatformProfiles.Add(p); return p;
    }
    public static async Task<string[]> Readiness(FoundationDbContext db, Guid id, AcademyConfigurationService config)
    {
        var errors = new List<string>();
        var a = await db.Academies.SingleAsync(a => a.Id == id);
        if (string.IsNullOrWhiteSpace(a.ArabicName)) errors.Add("أكمل اسم الأكاديمية.");
        if (!await db.AcademyExperiences.AnyAsync(x => x.AcademyId == id && x.Logo != null)) errors.Add("ارفع شعار الأكاديمية الأساسي من تبويب الهوية.");
        if (!await db.AcademyPlatformProfiles.AnyAsync(p => p.AcademyId == id && p.Location != "" && p.ContactPhone != "")) errors.Add("أكمل الموقع ورقم تواصل الأكاديمية.");
        var owners = await db.AcademyMemberships.Where(m => m.AcademyId == id && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive).Select(m => m.User.PhoneNumber).ToArrayAsync();
        if (owners.Length == 0 || owners.Any(p => EgyptPhoneNormalizer.Normalize(p) != p || p == null)) errors.Add("أكمل رقم هاتف صحيحًا وفريدًا لكل مالك نشط.");
        if (!await db.Branches.AnyAsync(x => x.AcademyId == id && x.IsActive)) errors.Add("أضف فرعًا فعليًا باسم واضح.");
        if (!await db.Sports.AnyAsync(x => x.AcademyId == id && x.IsActive)) errors.Add("أضف رياضة فعلية واحدة على الأقل.");
        if (await config.Active(id) is null) errors.Add("أكمل الفروع والرياضات واحفظها لتهيئة تشغيل الأكاديمية.");
        if (await db.AcademyImportJobs.AnyAsync(x => x.AcademyId == id && (x.State == Academy.Infrastructure.People.ImportJobState.Queued || x.State == Academy.Infrastructure.People.ImportJobState.Running || x.State == Academy.Infrastructure.People.ImportJobState.QueuedV3 || x.State == Academy.Infrastructure.People.ImportJobState.RunningV3))) errors.Add("انتظر اكتمال دفعات الاستيراد قبل التفعيل.");
        return errors.ToArray();
    }
    private static async Task<IResult> Detail(Guid id, FoundationDbContext db, AcademyConfigurationService config, HttpResponse response)
    {
        var a = await db.Academies.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id); if (a is null) return Results.NotFound();
        response.Headers.CacheControl = "private, no-store";
        var profile = await db.AcademyPlatformProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == id) ?? new() { AcademyId = id, OperationalState = a.IsActive ? "Active" : "Draft", EditVersion = 0 };
        return Results.Ok(new { a.Id, a.ArabicName, a.Slug, a.TimeZone, a.DefaultCurrency, a.IsActive, profile,
            timeZoneLockReason = await TimeZoneLockReason(db, id, a.IsActive),
            branding = await db.AcademyExperiences.Where(x => x.AcademyId == id).Select(x => new { x.DisplayName, x.PrimaryColor, x.SecondaryColor, hasLogo = x.Logo != null }).SingleOrDefaultAsync(),
            branches = await db.Branches.Where(x => x.AcademyId == id && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName, x.Address }).ToArrayAsync(),
            sports = await db.Sports.Where(x => x.AcademyId == id && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName, kind = x.Kind.ToString() }).ToArrayAsync(),
            users = await db.AcademyMemberships.Where(m => m.AcademyId == id && m.IsActive).Select(m => new { m.Id, m.User.DisplayName, m.User.PhoneNumber, role = m.Role.ToString(), m.AllBranches, m.AllSports }).ToArrayAsync(),
            readiness = await Readiness(db, id, config),
            configuration = await config.Active(id),
            audit = await db.PlatformAudits.Where(x => x.TargetAcademyId == id).OrderByDescending(x => x.AtUtc).Take(100).Select(x => new { x.Id, x.Action, x.AtUtc }).ToArrayAsync()
        });
    }
    private static void Audit(FoundationDbContext db, Guid id, Guid actor, string action, object details, TimeProvider clock) => db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), TargetAcademyId = id, ActorUserId = actor, Action = action, Details = JsonSerializer.Serialize(details), AtUtc = clock.GetUtcNow() });
    private static Task<int> Lock(FoundationDbContext db, Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + id}, 0))");
    private static async Task<string?> TimeZoneLockReason(FoundationDbContext db, Guid id, bool active)
    {
        if (active) return "المنطقة الزمنية ثابتة بعد تفعيل الأكاديمية.";
        if (await db.TrainingSessions.AnyAsync(s => s.AcademyId == id)) return "المنطقة الزمنية ثابتة بعد تسجيل حصص فعلية.";
        if (await db.ImportedHistoricalPayments.IgnoreQueryFilters().AnyAsync(p => p.AcademyId == id))
            return "المنطقة الزمنية ثابتة بعد ترحيل تحصيلات سابقة حتى يطابق يوم القبض التاريخ الوارد في الشيت.";
        return null;
    }
    public sealed record SetupWrite(int Version, string Name, string Location, string ContactPhone, string OwnerPhone, string TimeZone, string CommercialState, decimal? PlatformFee, string FeeCurrency, string BillingPeriod, DateOnly? SubscriptionFrom, DateOnly? SubscriptionTo);
    private static async Task<IResult> Save(Guid id, SetupWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock, AcademyConfigurationService service)
    {
        var contact = EgyptPhoneNormalizer.Normalize(body.ContactPhone); var phone = EgyptPhoneNormalizer.Normalize(body.OwnerPhone);
        var zone = body.TimeZone?.Trim() ?? "";
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) return Results.UnprocessableEntity(new { message = "أدخل منطقة زمنية صحيحة، مثل Africa/Cairo." });
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200 || string.IsNullOrWhiteSpace(body.Location) || body.Location.Length > 240 || contact is null || phone is null || body.CommercialState is not ("Unspecified" or "Trial" or "Subscribed" or "Expired") || body.PlatformFee is < 0 or > 1_000_000_000 || body.PlatformFee.HasValue && decimal.Round(body.PlatformFee.Value,2) != body.PlatformFee || body.FeeCurrency is not ("EGP" or "USD" or "SAR" or "AED") || body.BillingPeriod is not ("Monthly" or "Yearly") || body.SubscriptionTo.HasValue && (!body.SubscriptionFrom.HasValue || body.SubscriptionTo < body.SubscriptionFrom)) return Results.UnprocessableEntity(new { message = "راجع الاسم والموقع والهاتف وشروط الاشتراك." });
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db,id);
        var a = await db.Academies.SingleOrDefaultAsync(a => a.Id == id); if (a is null) return Results.NotFound();
        var p = await Profile(db,id); if (body.Version != (db.Entry(p).State == EntityState.Added ? 0 : p.EditVersion)) return Results.Conflict(new { message = "تغيرت البيانات؛ حدّث الصفحة قبل الحفظ." });
        var owners = await db.AcademyMemberships.Where(m => m.AcademyId == id && m.Role == AcademyRole.AcademyOwner && m.IsActive).Select(m => m.User).Distinct().ToArrayAsync();
        if (owners.Length != 1) return Results.Conflict(new { message = "يلزم مراجعة مالك الأكاديمية الأساسي قبل تغيير الهاتف." });
        var owner = owners[0];
        if (await db.Users.AnyAsync(u => u.Id != owner.Id && u.PhoneNumber == phone)) return Results.Conflict(new { message = "الهاتف مرتبط بهوية قائمة. راجع العضوية دون دمج الحسابين أو تغيير بياناتهما." });
        var zoneChanged = a.TimeZone != zone;
        if (zoneChanged && await TimeZoneLockReason(db, id, a.IsActive) is { } reason)
            return Results.Conflict(new { code = "academy_time_zone_locked", message = reason, timeZone = a.TimeZone });
        var before = new { a.ArabicName, a.TimeZone, p.Location, p.CommercialState, p.PlatformFee, p.FeeCurrency, p.BillingPeriod };
        a.ArabicName = body.Name.Trim(); a.TimeZone = zone; a.UpdatedAtUtc = clock.GetUtcNow();
        var phoneChanged = owner.PhoneNumber != phone;
        if (phoneChanged) owner.PhoneNumberConfirmed = false;
        owner.PhoneNumber = phone; owner.UpdatedAtUtc = clock.GetUtcNow();
        p.Location = body.Location.Trim(); p.ContactPhone = contact; p.CommercialState = body.CommercialState; p.PlatformFee = body.PlatformFee; p.FeeCurrency = body.FeeCurrency; p.BillingPeriod = body.BillingPeriod; p.SubscriptionFrom = body.SubscriptionFrom; p.SubscriptionTo = body.SubscriptionTo; p.EditVersion++;
        var experience = await db.AcademyExperiences.SingleOrDefaultAsync(x => x.AcademyId == id); if (experience is not null) experience.DisplayName = a.ArabicName;
        var actorId = Actor(actor);
        Audit(db,id,actorId,"academy.setup",new { before, after = new { a.ArabicName,a.TimeZone,p.Location,p.CommercialState,p.PlatformFee,p.FeeCurrency,p.BillingPeriod }, ownerPhoneChanged = phoneChanged },clock);
        await db.SaveChangesAsync();
        if (zoneChanged)
        {
            var active = await db.AcademyConfigurationVersions.SingleOrDefaultAsync(x => x.AcademyId == id && x.State == ConfigurationState.Active);
            if (active is not null)
            {
                var settings = AcademyRuntimeSettings.Read(active.Payload) with { TimeZone = zone };
                var (revision, errors) = await PublishConfiguration(id, actorId, settings, active, service, clock);
                if (errors.Length > 0) return Results.UnprocessableEntity(new { message = "تعذر تغيير المنطقة الزمنية في إعدادات التشغيل.", errors });
                Audit(db, id, actorId, "academy.configuration", new { revision, reason = "academy-time-zone" }, clock);
                await db.SaveChangesAsync();
            }
        }
        await tx.CommitAsync(); return Results.Ok(new { version = p.EditVersion });
    }
    public sealed record SubscriptionWrite(int Version, string CommercialState, decimal? PlatformFee, string FeeCurrency, string BillingPeriod, DateOnly? SubscriptionFrom, DateOnly? SubscriptionTo);
    private static async Task<IResult> SaveSubscription(Guid id, SubscriptionWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        if (body.CommercialState is not ("Unspecified" or "Trial" or "Subscribed" or "Expired")) return Results.UnprocessableEntity(new { message = "اختر حالة اشتراك صحيحة." });
        if (body.PlatformFee is < 0 or > 1_000_000_000 || body.PlatformFee.HasValue && decimal.Round(body.PlatformFee.Value, 2) != body.PlatformFee) return Results.UnprocessableEntity(new { message = "أدخل رسوم المنصة غير سالبة وبمنزلتين عشريتين على الأكثر." });
        if (body.FeeCurrency is not ("EGP" or "USD" or "SAR" or "AED") || body.BillingPeriod is not ("Monthly" or "Yearly")) return Results.UnprocessableEntity(new { message = "اختر عملة وفترة فوترة صحيحتين." });
        if (body.SubscriptionTo.HasValue && (!body.SubscriptionFrom.HasValue || body.SubscriptionTo < body.SubscriptionFrom)) return Results.UnprocessableEntity(new { message = "أدخل بداية الاشتراك على أن تكون النهاية بعدها أو في اليوم نفسه." });
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, id);
        if (!await db.Academies.AnyAsync(a => a.Id == id)) return Results.NotFound();
        var p = await Profile(db, id);
        if (body.Version != (db.Entry(p).State == EntityState.Added ? 0 : p.EditVersion)) return Results.Conflict(new { message = "تغيرت البيانات؛ أعد فتح الأكاديمية قبل الحفظ." });
        var before = new { p.CommercialState, p.PlatformFee, p.FeeCurrency, p.BillingPeriod, p.SubscriptionFrom, p.SubscriptionTo };
        p.CommercialState = body.CommercialState; p.PlatformFee = body.PlatformFee; p.FeeCurrency = body.FeeCurrency; p.BillingPeriod = body.BillingPeriod; p.SubscriptionFrom = body.SubscriptionFrom; p.SubscriptionTo = body.SubscriptionTo; p.EditVersion++;
        Audit(db, id, Actor(actor), "academy.subscription", new { before, after = body }, clock);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { version = p.EditVersion });
    }
    public sealed record StructureItem(Guid Id, string Name, string? Kind, string? Address);
    public sealed record StructureWrite(StructureItem[] Branches, StructureItem[] Sports);
    private static async Task<IResult> Structure(Guid id, StructureWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock, AcademyConfigurationService service)
    {
        if (body.Branches is null || body.Sports is null || body.Branches.Length > 100 || body.Sports.Length > 50 || body.Branches.Concat(body.Sports).Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 120 || x.Address?.Length > 240) || body.Sports.Any(x => x.Kind is not ("Football" or "Swimming" or "General")) || body.Branches.Select(x => x.Id).Distinct().Count() != body.Branches.Length || body.Sports.Select(x => x.Id).Distinct().Count() != body.Sports.Length) return Results.UnprocessableEntity(new { message = "أدخل أسماء الفروع والرياضات وأكوادًا فريدة." });
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db,id);
        if (!await db.Academies.AnyAsync(a => a.Id == id)) return Results.NotFound();
        var ids = body.Branches.Concat(body.Sports).Select(x => x.Id).ToArray();
        if (await db.Branches.AnyAsync(b => ids.Contains(b.Id) && b.AcademyId != id) || await db.Sports.AnyAsync(s => ids.Contains(s.Id) && s.AcademyId != id)) return Results.NotFound();
        var now = clock.GetUtcNow();
        foreach (var item in body.Branches) { var b = await db.Branches.SingleOrDefaultAsync(b => b.AcademyId == id && b.Id == item.Id); if (b is null) { b = new() { Id = item.Id, AcademyId = id, ArabicName = item.Name.Trim(), CreatedAtUtc = now, StaffCheckInEnabled = false }; db.Branches.Add(b); } b.ArabicName = item.Name.Trim(); b.Address = item.Address?.Trim(); b.UpdatedAtUtc = now; }
        foreach (var item in body.Sports) { var s = await db.Sports.SingleOrDefaultAsync(s => s.AcademyId == id && s.Id == item.Id); var kind = Enum.Parse<SportKind>(item.Kind!); if (s is not null && s.Kind != kind && await db.SportEnrollments.AnyAsync(e => e.AcademyId == id && e.SportId == s.Id)) return Results.Conflict(new { message = "للرياضة تسجيلات فعلية؛ لا تغيّر نوعها." }); if (s is null) { s = new() { Id = item.Id, AcademyId = id, ArabicName = item.Name.Trim(), CreatedAtUtc = now }; db.Sports.Add(s); } s.ArabicName = item.Name.Trim(); s.Kind = kind; s.UpdatedAtUtc = now; }
        Audit(db,id,Actor(actor),"academy.structure",new { branches = body.Branches.Select(x => x.Id), sports = body.Sports.Select(x => x.Id) },clock);
        await db.SaveChangesAsync();
        if (body.Branches.Length > 0 && body.Sports.Length > 0) {
            var errors = await InitializeOperation(id,Actor(actor),db,clock,service);
            if(errors.Length>0)return Results.UnprocessableEntity(new{message="تعذر إكمال إعداد تشغيل الأكاديمية. لم تُحفظ تغييرات جزئية.",errors});
        }
        await tx.CommitAsync(); return Results.NoContent();
    }
    // Explicit setup mutation; the following import preview remains read-only.
    private static async Task<IResult> PrepareOperation(Guid id, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock, AcademyConfigurationService service)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,id);
        if(!await db.Academies.AnyAsync(a=>a.Id==id))return Results.NotFound();
        var errors=await InitializeOperation(id,Actor(actor),db,clock,service);
        if(errors.Length>0)return Results.UnprocessableEntity(new{message="تعذر تجهيز تشغيل الأكاديمية للاستيراد.",errors});
        await tx.CommitAsync();return Results.NoContent();
    }
    private static async Task<string[]> InitializeOperation(Guid id, Guid actorId, FoundationDbContext db, TimeProvider clock, AcademyConfigurationService service)
    {
        // Never reactivate features explicitly disabled in an existing configuration.
        if(await service.Active(id) is not null)return [];
        var now=clock.GetUtcNow();
        var entitlementRows = await db.AcademyFeatureEntitlements.Where(x=>x.AcademyId==id).ToListAsync();
        var core = new[] { "training", "matches" }.ToHashSet();
        var before = entitlementRows.ToDictionary(x=>x.Feature,x=>x.Enabled && x.IsActive);
        foreach(var feature in core) {
            var entitlement = entitlementRows.SingleOrDefault(x=>x.Feature==feature);
            if(entitlement is null){entitlement=new() {Id=Guid.NewGuid(),AcademyId=id,Feature=feature,PlanCode="Trial",CreatedAtUtc=now};db.AcademyFeatureEntitlements.Add(entitlement);entitlementRows.Add(entitlement);}
            entitlement.Enabled=true;entitlement.IsActive=true;entitlement.UpdatedAtUtc=now;
        }
        await db.SaveChangesAsync();
        var academy = await db.Academies.SingleAsync(x=>x.Id==id);
        var features = AcademyFeatures.All.ToDictionary(f=>f,f=>entitlementRows.Any(x=>x.Feature==f && x.IsActive && x.Enabled));
        var draft = await service.CreateDraft(id,actorId,AcademyRuntimeSettings.Initial(academy.DefaultCurrency,academy.TimeZone) with {Features=features});
        var errors = await service.Validate(draft);
        if(errors.Length>0)return errors;
        await service.Transition(draft,ConfigurationState.Validated,actorId);draft.EffectiveAtUtc=now;
        await service.Transition(draft,ConfigurationState.Staged,actorId);await service.Transition(draft,ConfigurationState.Active,actorId);
        Audit(db,id,actorId,"academy.entitlements",new{before,after=features,reason="initial-core-setup"},clock);
        Audit(db,id,actorId,"academy.configuration",new{draft.Revision,features,reason="initial-operation"},clock);
        await db.SaveChangesAsync();
        return [];
    }
    public sealed record ConfigureWrite(Dictionary<string,bool> Features);
    private static async Task<IResult> Configure(Guid id, ConfigureWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock, AcademyConfigurationService service)
    {
        if (body.Features is null || body.Features.Count != AcademyFeatures.All.Length || AcademyFeatures.All.Any(f => !body.Features.ContainsKey(f))) return Results.UnprocessableEntity(new { message = "اختر قائمة خدمات كاملة وصحيحة." });
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db,id);
        var a = await db.Academies.SingleOrDefaultAsync(a => a.Id == id); if (a is null) return Results.NotFound();
        var old = await db.AcademyConfigurationVersions.SingleOrDefaultAsync(x => x.AcademyId == id && x.State == ConfigurationState.Active);
        var settings = (old is null ? AcademyRuntimeSettings.Initial(a.DefaultCurrency,a.TimeZone) : AcademyRuntimeSettings.Read(old.Payload)) with { Features = body.Features };
        var actorId = Actor(actor);
        var (revision, errors) = await PublishConfiguration(id, actorId, settings, old, service, clock);
        if (errors.Length > 0) return Results.UnprocessableEntity(new { message = "راجع تراخيص الخدمات قبل حفظ الإعدادات.", errors });
        Audit(db,id,actorId,"academy.configuration",new { revision, body.Features },clock); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { revision });
    }
    private static async Task<(int Revision, string[] Errors)> PublishConfiguration(Guid id, Guid actor, AcademyRuntimeSettings settings,
        AcademyConfigurationVersion? active, AcademyConfigurationService service, TimeProvider clock)
    {
        var draft = await service.CreateDraft(id, actor, settings);
        var errors = await service.Validate(draft);
        if (errors.Length > 0) return (0, errors);
        await service.Transition(draft, ConfigurationState.Validated, actor);
        draft.EffectiveAtUtc = clock.GetUtcNow();
        await service.Transition(draft, ConfigurationState.Staged, actor);
        if (active is not null) await service.Transition(active, ConfigurationState.Superseded, actor);
        await service.Transition(draft, ConfigurationState.Active, actor);
        return (draft.Revision, []);
    }
    public sealed record StateWrite(string Target, int Version);
    private static async Task<IResult> State(Guid id, StateWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock, AcademyConfigurationService config)
    {
        if (body.Target is not ("Active" or "Suspended")) return Results.UnprocessableEntity(new { message = "اختر التفعيل أو تعليق الوصول." });
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db,id);
        var a = await db.Academies.SingleOrDefaultAsync(a => a.Id == id); if (a is null) return Results.NotFound();
        var p = await Profile(db,id); if (body.Version != (db.Entry(p).State == EntityState.Added ? 0 : p.EditVersion)) return Results.Conflict(new { message = "تغيرت البيانات؛ حدّث الصفحة قبل تغيير الحالة." });
        if (body.Target == "Active" && p.OperationalState != "Suspended" && !a.IsActive) { var errors = await Readiness(db,id,config); if (errors.Length > 0) return Results.UnprocessableEntity(new { message = "الأكاديمية تحتاج إكمال التجهيز قبل التفعيل.", errors }); }
        var before = a.IsActive ? "Active" : p.OperationalState;
        a.IsActive = body.Target == "Active"; a.UpdatedAtUtc = clock.GetUtcNow(); p.OperationalState = body.Target; p.EditVersion++;
        Audit(db,id,Actor(actor),"academy.state",new { before, after = body.Target },clock);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { state = body.Target, version = p.EditVersion });
    }
}
