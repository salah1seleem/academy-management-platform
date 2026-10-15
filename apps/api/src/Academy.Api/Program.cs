using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Platform;
using Academy.Api.Mobile;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Api.Slice6;
using Academy.Api.Slice7;
using Academy.Api.Slice8;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK");
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings__Default must be provided through environment configuration.");

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.Configure<InternalTestPaymentOptions>(builder.Configuration.GetSection(InternalTestPaymentOptions.SectionName));
builder.Services.AddDbContext<FoundationDbContext>(options => options.UseNpgsql(connectionString,
    npgsql => npgsql.MigrationsAssembly(typeof(FoundationDbContext).Assembly.FullName)));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }).AddSignInManager().AddEntityFrameworkStores<FoundationDbContext>().AddDefaultTokenProviders();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Academy.Client";
        options.DefaultChallengeScheme = "Academy.Client";
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    }).AddIdentityCookies();
builder.Services.AddAuthentication()
    .AddCookie(PlatformIdentity.Scheme)
    .AddPolicyScheme("Academy.Client", "Cookie or native session", options =>
        options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("Authorization")
            ? MobileAuthenticationHandler.SchemeName : IdentityConstants.ApplicationScheme)
    .AddScheme<AuthenticationSchemeOptions, MobileAuthenticationHandler>(MobileAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new { title = "محاولات كثيرة، يرجى الانتظار ثم المحاولة مجددًا", status = 429 }, cancellationToken);
    options.AddPolicy("mobile-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("player-card", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{context.User.FindFirstValue("academy_id")}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("renewal-search", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{context.User.FindFirstValue("academy_id")}:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("guardian-feedback", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{context.User.FindFirstValue("academy_id")}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("device-check-in", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{context.User.FindFirstValue("academy_id")}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("import", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
    options.OnRefreshingPrincipal = context =>
    {
        var academyClaim = context.CurrentPrincipal?.FindFirst("academy_id");
        if (academyClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity)
            identity.AddClaim(academyClaim);
        var membershipClaim = context.CurrentPrincipal?.FindFirst("membership_id");
        if (membershipClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity memberIdentity)
            memberIdentity.AddClaim(membershipClaim);
        var draftClaim = context.CurrentPrincipal?.FindFirst("attendance_session");
        if (draftClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity draftIdentity)
            draftIdentity.AddClaim(draftClaim);
        return Task.CompletedTask;
    };
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<AttendanceDraftGuard>();
builder.Services.AddSingleton<ScopedRenewalReferences>();
builder.Services.AddSingleton<DatabaseTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>, IdentityCookieOptions>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<AttendanceCorrectionService>();
builder.Services.AddScoped<AcademyConfigurationService>();
builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PlatformAccessHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PlatformIdentity.Policy, policy => policy.AddAuthenticationSchemes(PlatformIdentity.Scheme).RequireAuthenticatedUser().AddRequirements(new PlatformAccessRequirement()));
    options.AddPolicy("mobile-session", policy => policy.AddAuthenticationSchemes(MobileAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
    foreach (var permission in PayrollSensitiveAccess.Permissions)
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantPermissionRequirement(permission)));
    options.AddPolicy(AcademyPermissions.AcademySettingsManage, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantPermissionRequirement(AcademyPermissions.AcademySettingsManage)));
    options.AddPolicy(AcademyPermissions.TenantAccess, policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new TenantPermissionRequirement(AcademyPermissions.TenantAccess)));
    options.AddPolicy(AcademyPermissions.StaffProvision, policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new TenantPermissionRequirement(AcademyPermissions.StaffProvision)));
    foreach (var permission in new[] { AcademyPermissions.StructureManage, AcademyPermissions.PeopleManage, AcademyPermissions.GuardianChildrenRead, AcademyPermissions.CoachGroupsRead, AcademyPermissions.SubscriptionPlanManage, AcademyPermissions.SubscriptionRead, AcademyPermissions.SubscriptionRenewManage, AcademyPermissions.SubscriptionAdjustmentManage, AcademyPermissions.SubscriptionDiscountManage, AcademyPermissions.GuardianOwnRenewal, AcademyPermissions.PaymentRead, AcademyPermissions.CollectionRead, AcademyPermissions.AttendanceRead, AcademyPermissions.AttendanceManage, AcademyPermissions.GuardianAttendanceRead, AcademyPermissions.EvaluationRead, AcademyPermissions.EvaluationManage, AcademyPermissions.EvaluationCriteriaManage, AcademyPermissions.GuardianEvaluationRead, AcademyPermissions.EnrollmentRequestRead, AcademyPermissions.EnrollmentRequestManage, AcademyPermissions.GuardianEnrollmentRequestCreate, AcademyPermissions.SportCatalogRead, AcademyPermissions.SportCatalogManage, AcademyPermissions.NutritionRead, AcademyPermissions.NutritionManage, AcademyPermissions.MedicalRecordRead, AcademyPermissions.MedicalRecordManage, AcademyPermissions.PlayerMediaRead, AcademyPermissions.PlayerMediaManage, AcademyPermissions.ReportsFinancialRead, AcademyPermissions.ReportsAttendanceRead, AcademyPermissions.ReportsExport, AcademyPermissions.OwnerDashboardRead, AcademyPermissions.PayrollRead, AcademyPermissions.PayrollManage })
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantPermissionRequirement(permission)));
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "academy.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Demo")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddScoped<CsrfFilter>();
builder.Services.AddScoped<ISubscriptionClock, SubscriptionClock>();
builder.Services.AddScoped<IPaymentGateway, InternalTestPaymentGateway>();
builder.Services.AddScoped<PaymentProcessor>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<CashRegisterService>();
builder.Services.AddScoped<CashPaymentReferences>();
builder.Services.AddScoped<CrossBranchCashPayments>();
builder.Services.AddScoped<SettlementComparisonService>();
builder.Services.AddScoped<FamilyRenewalQuoteService>();
builder.Services.AddScoped<FreeTrainingService>();
builder.Services.AddScoped<ReceivableCreditService>();
builder.Services.AddScoped<Academy.Api.Facilities.DayPassService>();
builder.Services.AddScoped<FamilyRequestService>();
builder.Services.AddScoped<DiscountRules>();
builder.Services.AddScoped<RenewalCreationService>();
builder.Services.AddScoped<SubscriptionAdjustmentService>();
builder.Services.AddScoped<SubscriptionDiscountService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<PlayerCardService>();
builder.Services.AddScoped<Academy.Api.Finance.OperatingExpenseService>();
builder.Services.AddScoped<Academy.Api.Facilities.ObservationService>();
builder.Services.AddScoped<Academy.Api.Facilities.VisitService>();
builder.Services.AddScoped<TrainingSessionGenerator>();
builder.Services.AddScoped<Academy.Api.Facilities.ResourceTimetableService>();
builder.Services.AddScoped<GroupPlacementService>();
builder.Services.AddScoped<CoachOverrideService>();
builder.Services.AddScoped<GroupCreationService>();
builder.Services.AddScoped<Academy.Api.Facilities.ResourceSessionCoordinator>();
builder.Services.AddScoped<Academy.Api.Facilities.FacilityService>();
builder.Services.AddAuthorization(options => {
    foreach (var p in new[] { AcademyPermissions.FacilitiesRead, AcademyPermissions.FacilitiesManage }) options.AddPolicy(p, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantPermissionRequirement(p)));
});
builder.Services.AddScoped<Academy.Api.Importing.ImportCommitService>();
if (builder.Configuration.GetValue("Imports:WorkerEnabled", true)) builder.Services.AddHostedService<Academy.Api.Importing.ImportWorker>();
builder.Services.AddScoped<IEvaluationReportCalculator, EvaluationReportCalculator>();
builder.Services.AddHealthChecks().AddDbContextCheck<FoundationDbContext>("postgresql", tags: ["ready"]);

var app = builder.Build();
DemoSeed.ValidateEnvironment(app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
InternalTestPaymentGateway.ValidateEnvironment(app.Environment, app.Services.GetRequiredService<IOptions<InternalTestPaymentOptions>>().Value);
app.UseExceptionHandler();
app.UseAuthentication();
app.Use(async (context, next) => {
    if (context.User.Identity?.IsAuthenticated == true) context.Response.Headers.CacheControl = "private, no-store";
    await next(context);
});
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<RequestIsolationMiddleware>();
app.UseMiddleware<AcademyFeatureGateMiddleware>();
app.MapMobileAuth();
app.MapPlatformIdentity();
app.MapPlatformAcademies();
app.MapStaffAccess();
app.MapStaffRoleTemplates();
app.MapStaffAccessContexts();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync });

var api = app.MapGroup("/api/v1");
api.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken, headerName = "X-CSRF-TOKEN" });
});

api.MapPost("/auth/login", async (LoginRequest request, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, FoundationDbContext db, HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated == true) return Program.ActiveSessionConflict();
    var normalized = users.NormalizeEmail(request.Email.Trim());
    var user = await users.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
    var passwordResult = user is null ? null : await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (user is null || !user.IsActive || passwordResult is null || !passwordResult.Succeeded)
        return Results.Problem(statusCode: 401, title: "تعذر تسجيل الدخول", detail: "بيانات الدخول غير صحيحة.");
    var membership = await db.AcademyMemberships.AsNoTracking()
        .Where(x => x.UserId == user.Id && x.IsActive && x.Academy.IsActive && (x.PrimaryMembershipId == null || x.PrimaryMembership!.IsActive && x.PrimaryMembership.PrimaryMembershipId == null && x.PrimaryMembership.Role == AcademyRole.AcademyAdmin) &&
            (x.Role == AcademyRole.AcademyOwner || x.Role == AcademyRole.AcademyAdmin))
        .OrderBy(x => x.PrimaryMembershipId != null).ThenBy(x => x.CreatedAtUtc).FirstOrDefaultAsync();
    if (membership is null)
        return Results.Problem(statusCode: 403, title: "الدخول غير متاح", detail: "لوحة الإدارة متاحة للمالك والإداري فقط.");
    var requiresContextSelection = await db.AcademyMemberships.CountAsync(x => x.UserId == user.Id && x.IsActive && x.Academy.IsActive && (x.Role == AcademyRole.AcademyOwner || x.Role == AcademyRole.AcademyAdmin) && (x.PrimaryMembershipId == null || x.PrimaryMembership!.IsActive && x.PrimaryMembership.PrimaryMembershipId == null && x.PrimaryMembership.Role == AcademyRole.AcademyAdmin)) > 1;
    await Program.SignInAsync(context, user, membership.AcademyId, membership.Id, contextPending: requiresContextSelection);
    return Results.Ok(new { authenticated = true, requiresContextSelection });
}).AddEndpointFilter<CsrfFilter>();

api.MapPost("/auth/guardian/otp/request", async (GuardianOtpRequest request, FoundationDbContext db,
    IOptions<DemoOptions> options, IHostEnvironment environment, TimeProvider clock) =>
{
    var phone = EgyptPhoneNormalizer.Normalize(request.PhoneNumber);
    if (phone is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = ["رقم الهاتف غير صالح."] });
    DemoSeed.ValidateEnvironment(environment, options.Value);
    if (!environment.IsEnvironment("Demo") || !options.Value.FixedOtpEnabled)
        return Results.Problem(statusCode: 501, title: "خدمة OTP الإنتاجية غير منفذة بعد");
    var challenge = new GuardianOtpChallenge
    {
        Id = Guid.NewGuid(), PhoneNumberNormalized = phone, CreatedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddMinutes(5)
    };
    db.GuardianOtpChallenges.Add(challenge);
    await db.SaveChangesAsync();
    return Results.Accepted(value: new { challengeId = challenge.Id, demo = true });
}).AddEndpointFilter<CsrfFilter>();

api.MapPost("/auth/guardian/otp/verify", async (GuardianOtpVerifyRequest request, FoundationDbContext db,
    UserManager<ApplicationUser> users, IOptions<DemoOptions> options,
    IHostEnvironment environment, TimeProvider clock, HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated == true) return Program.ActiveSessionConflict();
    DemoSeed.ValidateEnvironment(environment, options.Value);
    if (!environment.IsEnvironment("Demo") || !options.Value.FixedOtpEnabled)
        return Results.Problem(statusCode: 501, title: "خدمة OTP الإنتاجية غير منفذة بعد");
    var phone = EgyptPhoneNormalizer.Normalize(request.PhoneNumber);
    var challenge = await db.GuardianOtpChallenges.SingleOrDefaultAsync(x => x.Id == request.ChallengeId);
    if (phone is null || challenge is null || challenge.PhoneNumberNormalized != phone || challenge.ConsumedAtUtc is not null || challenge.FailedAttempts >= 5 ||
        challenge.ExpiresAtUtc <= clock.GetUtcNow() || request.Code != options.Value.FixedOtp)
    {
        if (challenge is not null) { challenge.FailedAttempts++; await db.SaveChangesAsync(); }
        return Results.Problem(statusCode: 401, title: "تعذر التحقق", detail: "رمز التحقق غير صحيح أو منتهي.");
    }
    var user = await users.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone && x.IsActive);
    var membership = user is null ? null : await db.AcademyMemberships.AsNoTracking()
        .FirstOrDefaultAsync(x => x.UserId == user.Id && x.IsActive && x.Role == AcademyRole.Guardian && x.Academy.IsActive);
    if (user is null || membership is null) return Results.Problem(statusCode: 401, title: "تعذر التحقق", detail: "رمز التحقق غير صحيح أو منتهي.");
    challenge.ConsumedAtUtc = clock.GetUtcNow();
    await db.SaveChangesAsync();
    await Program.SignInAsync(context, user, membership.AcademyId, membership.Id);
    return Results.Ok(new { authenticated = true });
}).AddEndpointFilter<CsrfFilter>();

api.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    return Results.NoContent();
}).RequireAuthorization().AddEndpointFilter<CsrfFilter>();

api.MapGet("/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, CurrentTenant tenant, FoundationDbContext db) =>
{
    var user = await users.GetUserAsync(principal);
    var current = await tenant.ResolveAsync();
    return user is null || current is null ? Results.Unauthorized() : Results.Ok(new
    {
        userId = user.Id, user.DisplayName, academyId = current.AcademyId, academyName = current.AcademyName, role = current.Role.ToString(), membershipId = current.MembershipId,
        contextName = await db.AcademyMemberships.Where(m => m.Id == current.MembershipId).Select(m => m.AccessContextName).SingleAsync()
    });
}).RequireAuthorization(AcademyPermissions.TenantAccess);

api.MapGet("/my-academies", async (ClaimsPrincipal principal, FoundationDbContext db, HttpResponse response) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var items = await db.AcademyMemberships.AsNoTracking()
        .Where(x => x.UserId == userId && x.User.IsActive && x.IsActive && x.Academy.IsActive && (x.PrimaryMembershipId == null || x.PrimaryMembership!.IsActive && x.PrimaryMembership.PrimaryMembershipId == null && x.PrimaryMembership.Role == AcademyRole.AcademyAdmin))
        .OrderBy(x => x.Academy.ArabicName)
        .Select(x => new { membershipId = x.Id, academyId = x.AcademyId, academyName = x.Academy.ArabicName, role = x.Role.ToString(), contextName = x.AccessContextName, x.AllBranches, x.AllSports,
            branchNames = db.Branches.Where(b => db.MembershipBranchAccesses.Any(a => a.MembershipId == x.Id && a.BranchId == b.Id && a.IsActive)).Select(b => b.ArabicName).ToArray(),
            sportNames = db.Sports.Where(s => db.MembershipSportAccesses.Any(a => a.MembershipId == x.Id && a.SportId == s.Id && a.IsActive)).Select(s => s.ArabicName).ToArray() }).ToListAsync();
    response.Headers.CacheControl = "no-store";
    return Results.Ok(items);
}).RequireAuthorization();

api.MapPost("/session/academy", async (SelectAcademyRequest request, ClaimsPrincipal principal,
    UserManager<ApplicationUser> users, FoundationDbContext db, HttpContext context) =>
{
    if (principal.Identity?.AuthenticationType == MobileAuthenticationHandler.SchemeName) return Results.Forbid();
    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var memberships = await db.AcademyMemberships.AsNoTracking()
        .Where(x => x.UserId == userId && x.AcademyId == request.AcademyId && x.User.IsActive && x.IsActive && x.Academy.IsActive && (x.PrimaryMembershipId == null || x.PrimaryMembership!.IsActive && x.PrimaryMembership.PrimaryMembershipId == null && x.PrimaryMembership.Role == AcademyRole.AcademyAdmin)).ToListAsync();
    var selectedId = principal.FindFirstValue("membership_id");
    var membership = request.MembershipId.HasValue ? memberships.SingleOrDefault(x => x.Id == request.MembershipId)
        : memberships.FirstOrDefault(x => x.Id.ToString() == selectedId) ?? (memberships.Count == 1 ? memberships[0] : null);
    if (membership is null) return Results.Forbid();
    // A web staff session cannot use this endpoint to become a coach or guardian.
    var oldRole = Guid.TryParse(selectedId, out var oldId) ? await db.AcademyMemberships.Where(x => x.Id == oldId && x.UserId == userId).Select(x => (AcademyRole?)x.Role).SingleOrDefaultAsync() : null;
    if (principal.FindFirstValue("web_staff_context_pending") == "true" || oldRole is AcademyRole.AcademyAdmin or AcademyRole.AcademyOwner)
    { if (membership.Role is not (AcademyRole.AcademyAdmin or AcademyRole.AcademyOwner)) return Results.Forbid(); }
    else if (oldRole != membership.Role) return Results.Forbid();
    var user = await users.FindByIdAsync(userId.ToString());
    if (user is null || !user.IsActive) return Results.Unauthorized();
    // Same authenticated account only. Revoking and signing in in one request
    // causes CookieAuthenticationHandler to renew the now-revoked session key.
    var authenticated = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
    if (!authenticated.Succeeded || authenticated.Properties?.ExpiresUtc <= context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()) return Results.Unauthorized();
    await Program.SignInAsync(context, user, request.AcademyId, membership.Id, authenticated.Properties);
    return Results.NoContent();
}).RequireAuthorization().AddEndpointFilter<CsrfFilter>();

api.MapGet("/tenant/probe/{academyId:guid}", async (Guid academyId, CurrentTenant tenant) =>
{
    var current = await tenant.ResolveAsync();
    return current is not null && current.AcademyId == academyId
        ? Results.Ok(new { academyId = current.AcademyId, isolated = true }) : Results.Forbid();
}).RequireAuthorization(AcademyPermissions.TenantAccess);

api.MapPost("/staff", async (CreateStaffRequest request, CurrentTenant tenant, UserManager<ApplicationUser> users,
    FoundationDbContext db, TimeProvider clock) =>
{
    var current = await tenant.ResolveAsync();
    if (current is null) return Results.Forbid();
    if (request.Role is not (AcademyRole.AcademyAdmin or AcademyRole.Coach))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["يمكن إنشاء حساب إداري أو مدرب فقط."] });
    if (current.Role == AcademyRole.AcademyAdmin && request.Role != AcademyRole.Coach) return Results.Forbid();
    await using var transaction = await db.Database.BeginTransactionAsync();
    var user = await users.FindByEmailAsync(request.Email);
    if (user is not null && !user.IsActive) return Results.Conflict(new { message = "الحساب موجود لكنه غير مفعل." });
    if (user is null)
    {
        user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email, EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(), IsActive = true, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
        };
        var created = await users.CreateAsync(user, request.TemporaryPassword);
        if (!created.Succeeded)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = created.Errors.Select(x => x.Description).ToArray() });
    }
    if (await db.AcademyMemberships.AnyAsync(x => x.AcademyId == current.AcademyId && x.UserId == user.Id && x.Role == request.Role))
        return Results.Conflict(new { message = "العضوية موجودة بالفعل." });
    db.AcademyMemberships.Add(new AcademyMembership
    {
        Id = Guid.NewGuid(), AcademyId = current.AcademyId, UserId = user.Id, Role = request.Role,
        IsActive = true, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
    });
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Created($"/api/v1/staff/{user.Id}", new { user.Id, academyId = current.AcademyId });
}).RequireAuthorization(AcademyPermissions.StaffProvision).AddEndpointFilter<CsrfFilter>();

app.MapSlice2Endpoints();
app.MapCoachProvisioning();
app.MapSlice2DashboardEndpoints();
app.MapSlice3Endpoints();
app.MapSlice4Endpoints();
app.MapAttendanceCorrections();
app.MapPlayerCards();
app.MapSlice5Endpoints();
app.MapSlice6Endpoints();
app.MapSlice7Endpoints();
app.MapSlice8ReportingEndpoints();
app.MapSubscriptionAdjustmentEndpoints();
app.MapSubscriptionDiscountEndpoints();
app.MapAssetEndpoints();
app.MapDiscountRuleEndpoints();
app.MapGroupOperations();
app.MapUnassignedEnrollments();
app.MapGroupPlacement();
app.MapManualPayments();
app.MapExperience();
app.MapMatches();
app.MapTournamentPayments();
app.MapProductSales();
app.MapNews();
app.MapStaffManagement();
app.MapDailyStaffAttendance();
app.MapStaffDeviceCheckIn();
app.MapGuardianFeedback();
app.MapPayroll();
app.MapCoachAgreements();
app.MapPrivateServices();
app.MapPrivateCompensation();
app.MapSalaryHistory();
app.MapPayrollStatements();
app.MapPayrollAccount();
app.MapPayrollAdvances();
app.MapVersionedConfiguration();
app.MapSportSettings();
app.MapReceivables();
app.MapReceivableCredit();
app.MapOperatingExpenses();
app.MapCashRegisters();
app.MapProviderQuarantine();
app.MapSettlementComparisons();
app.MapFamilyRenewalQuotes();
app.MapFreeTraining();
app.MapFamilyRenewalRequests();
app.MapRenewalOpportunities();
Academy.Api.Importing.ImportEndpoints.MapImportPreview(app);
Academy.Api.Facilities.FacilityEndpoints.MapFacilities(app);
Academy.Api.Facilities.VisitEndpoints.MapVisits(app);
Academy.Api.Facilities.DayPassEndpoints.MapDayPass(app);
app.MapCoachOverrides();

if (args.Contains("--reset-football-demo"))
{
    await using var resetScope = app.Services.CreateAsyncScope();
    await FootballDemoSeed.RunAsync(resetScope.ServiceProvider, true);
    return;
}
if (app.Environment.IsEnvironment("Demo"))
{
    await DemoSeed.SeedAsync(app.Services);
    if (app.Services.GetRequiredService<IOptions<DemoOptions>>().Value.SeedEnabled)
    {
        await using var configSeed = app.Services.CreateAsyncScope();
        var db = configSeed.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ids = await db.Academies.Where(x => x.Id == DemoSeed.NogoomAcademyId || x.Id == DemoSeed.FutureAcademyId || x.Id == FootballDemoSeed.AcademyId).Select(x => x.Id).ToListAsync();
        await AcademyConfigurationService.SeedDemo(db, ids);
        if (app.Configuration.GetValue<bool>("Demo:CoachAgreementSeedEnabled"))
            await CoachAgreementDemoSeed.Seed(db, app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
        if (app.Configuration.GetValue<bool>("Demo:PrivateServiceSeedEnabled"))
            await PrivateServiceDemoSeed.Seed(db, app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
        if (app.Configuration.GetValue<bool>("Demo:SalaryHistorySeedEnabled"))
            await SalaryHistoryDemoSeed.Seed(db,app.Environment,app.Services.GetRequiredService<IOptions<DemoOptions>>().Value,app.Services.GetRequiredService<TimeProvider>());
        if(app.Configuration.GetValue<bool>("Demo:PayrollStatementSeedEnabled"))
            await PayrollStatementDemoSeed.Seed(db,app.Environment,app.Services.GetRequiredService<IOptions<DemoOptions>>().Value,app.Services.GetRequiredService<TimeProvider>());
        if(app.Configuration.GetValue<bool>("Demo:PayrollAccountSeedEnabled"))
            await PayrollAccountDemoSeed.Seed(db,app.Environment,app.Services.GetRequiredService<IOptions<DemoOptions>>().Value,app.Services.GetRequiredService<TimeProvider>());
        if(app.Configuration.GetValue<bool>("Demo:PayrollAdvanceSeedEnabled"))
            await PayrollAdvanceDemoSeed.Seed(db,app.Environment,app.Services.GetRequiredService<IOptions<DemoOptions>>().Value,app.Services.GetRequiredService<TimeProvider>());
        if (app.Services.GetRequiredService<IOptions<DemoOptions>>().Value.SeedProfile == "LegacyRegression")
        {
            if (app.Configuration.GetValue<bool>("Demo:SportScopeSeedEnabled")) await SportScopeDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
            if (app.Configuration.GetValue<bool>("Demo:AccessContextSeedEnabled"))
            {
                await SportScopeDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
                await AccessContextDemoSeed.Seed(db, app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
            }
            if (app.Configuration.GetValue<bool>("Demo:FamilyRenewalSeedEnabled")) await FamilyRenewalDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
            if (app.Configuration.GetValue<bool>("Demo:FreeTrainingSeedEnabled")) await FreeTrainingDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
            await ReceivableDemoSeed.SeedAsync(db);
            if (app.Configuration.GetValue<bool>("Demo:ReceivableCreditSeedEnabled")) await ReceivableCreditDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:CrossBranchCashSeedEnabled")) await CrossBranchCashDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:CrossBranchFamilySeedEnabled")) await CrossBranchFamilyDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
            if (app.Configuration.GetValue<bool>("Demo:CollectionFundsSeedEnabled")) await CollectionFundsDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:SettlementComparisonsSeedEnabled")) await SettlementComparisonDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:ProviderQuarantineSeedEnabled")) await ProviderQuarantineDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:CashRegistersSeedEnabled"))
            {
                await CashRegisterDemoSeed.Seed(db);
                await CashPendingPaymentDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<Academy.Api.Operations.DiscountRules>());
                await CashRegistrationDemoSeed.Seed(db);
                await CashProductDemoSeed.Seed(db);
            }
            await PlanPublicationDemoSeed.SeedAsync(db);
            await DiscountPricingDemoSeed.SeedAsync(db);
            await RenewalOpportunityDemoSeed.SeedAsync(db);
            if (app.Configuration.GetValue<bool>("Demo:FacilitiesSeedEnabled")) await Academy.Api.Facilities.FacilityDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:DayPassSeedEnabled")) {
                await Academy.Api.Facilities.FacilityDemoSeed.Seed(db);
                await Academy.Api.Facilities.DayPassDemoSeed.Seed(db, app.Services.GetRequiredService<TimeProvider>());
            }
            if (app.Configuration.GetValue<bool>("Demo:ResourceTimetableSeedEnabled")) await Academy.Api.Facilities.ResourceTimetableDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:UnassignedEnrollmentSeedEnabled")) await UnassignedEnrollmentDemoSeed.Seed(db);
            if (app.Configuration.GetValue<bool>("Demo:ObservationSeedEnabled")) {
                await Academy.Api.Facilities.FacilityDemoSeed.Seed(db);
                await GroupPlacementDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await Academy.Api.Facilities.ObservationDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>(), app.Services.GetRequiredService<TimeProvider>());
            }
            if (app.Configuration.GetValue<bool>("Demo:VisitSeedEnabled") || app.Configuration.GetValue<bool>("Demo:CoachOverrideSeedEnabled")) {
                await Academy.Api.Facilities.FacilityDemoSeed.Seed(db);
                await Academy.Api.Operations.GroupPlacementDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await Academy.Api.Facilities.VisitDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), configSeed.ServiceProvider.GetRequiredService<CollectionService>(), app.Services.GetRequiredService<TimeProvider>());
            }
            if (app.Configuration.GetValue<bool>("Demo:CoachOverrideSeedEnabled")) await CoachOverrideDemoSeed.Seed(db,configSeed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),app.Services.GetRequiredService<TimeProvider>());
            if (app.Configuration.GetValue<bool>("Demo:PastAttendanceSeedEnabled")) {
                await AttendanceStatesDemoSeed.Seed(db,configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await PastAttendanceDemoSeed.Seed(db,app.Services.GetRequiredService<TimeProvider>());
            }
            if (app.Configuration.GetValue<bool>("Demo:AttendanceDraftSeedEnabled")) {
                await GroupPlacementDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await AttendanceStatesDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await AttendanceDraftDemoSeed.Seed(db, app.Services.GetRequiredService<TimeProvider>());
            }
            if (app.Configuration.GetValue<bool>("Demo:GroupPlacementSeedEnabled")) { await GroupPlacementDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>()); await TrainingFormatDemoSeed.Seed(db); await GroupTransferDemoSeed.Seed(db); await CompensationDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>()); await GroupCreationDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>()); await AttendanceStatesDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>()); }
            if (app.Configuration.GetValue<bool>("Demo:GroupPlacementSeedEnabled")) await PlacementRecommendationDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
            if (app.Configuration.GetValue<bool>("Demo:OperatingExpenseSeedEnabled")) {
                await GroupPlacementDemoSeed.Seed(db, configSeed.ServiceProvider.GetRequiredService<CollectionService>());
                await OperatingExpenseDemoSeed.Seed(db, app.Services.GetRequiredService<TimeProvider>().GetUtcNow());
            }
        }
        await PlatformIdentity.SeedDemo(app.Services);
    }
}
app.Run();

public sealed record LoginRequest(string Email, string Password);
public sealed record GuardianOtpRequest(string PhoneNumber);
public sealed record GuardianOtpVerifyRequest(Guid ChallengeId, string PhoneNumber, string Code);
public sealed record SelectAcademyRequest(Guid AcademyId, Guid? MembershipId = null);
public sealed record CreateStaffRequest(string Email, string DisplayName, string TemporaryPassword, AcademyRole Role);

public partial class Program
{
    public static IResult ActiveSessionConflict() => Results.Problem(statusCode:409, title:"توجد جلسة دخول نشطة",detail:"سجّل الخروج أولًا قبل الدخول بحساب آخر.");
    public static async Task SignInAsync(HttpContext context, ApplicationUser user, Guid academyId, Guid membershipId, AuthenticationProperties? current = null, bool contextPending = false)
    {
        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName ?? user.Id.ToString()));
        identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp ?? string.Empty));
        identity.AddClaim(new Claim("academy_id", academyId.ToString()));
        if (contextPending) identity.AddClaim(new Claim("web_staff_context_pending", "true"));
        else identity.AddClaim(new Claim("membership_id", membershipId.ToString()));
        identity.AddClaim(new Claim("attendance_session", current is null ? Guid.NewGuid().ToString("N") : context.User.FindFirstValue("attendance_session") ?? Guid.NewGuid().ToString("N")));
        var principal = new ClaimsPrincipal(identity);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = false, AllowRefresh = true, IssuedUtc = current?.IssuedUtc ?? now, ExpiresUtc = current?.ExpiresUtc ?? now.AddMinutes(30)
        });
    }
}
