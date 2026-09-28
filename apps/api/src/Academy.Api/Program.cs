using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice2;
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
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings__Default must be provided through environment configuration.");

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
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
    }).AddSignInManager().AddEntityFrameworkStores<FoundationDbContext>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    }).AddIdentityCookies();
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
    options.OnRefreshingPrincipal = context =>
    {
        var academyClaim = context.CurrentPrincipal?.FindFirst("academy_id");
        if (academyClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity)
            identity.AddClaim(academyClaim);
        return Task.CompletedTask;
    };
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<DatabaseTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>, IdentityCookieOptions>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AcademyPermissions.TenantAccess, policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new TenantPermissionRequirement(AcademyPermissions.TenantAccess)));
    options.AddPolicy(AcademyPermissions.StaffProvision, policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new TenantPermissionRequirement(AcademyPermissions.StaffProvision)));
    foreach (var permission in new[] { AcademyPermissions.StructureManage, AcademyPermissions.PeopleManage, AcademyPermissions.GuardianChildrenRead, AcademyPermissions.CoachGroupsRead })
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
builder.Services.AddHealthChecks().AddDbContextCheck<FoundationDbContext>("postgresql", tags: ["ready"]);

var app = builder.Build();
DemoSeed.ValidateEnvironment(app.Environment, app.Services.GetRequiredService<IOptions<DemoOptions>>().Value);
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

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
    var normalized = users.NormalizeEmail(request.Email.Trim());
    var user = await users.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
    var passwordResult = user is null ? null : await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (user is null || !user.IsActive || passwordResult is null || !passwordResult.Succeeded)
        return Results.Problem(statusCode: 401, title: "تعذر تسجيل الدخول", detail: "بيانات الدخول غير صحيحة.");
    var membership = await db.AcademyMemberships.AsNoTracking()
        .Where(x => x.UserId == user.Id && x.IsActive && x.Academy.IsActive)
        .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync();
    if (membership is null) return Results.Problem(statusCode: 403, title: "الحساب غير متاح");
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    await Program.SignInAsync(context, user, membership.AcademyId);
    return Results.Ok(new { authenticated = true });
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
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    await Program.SignInAsync(context, user, membership.AcademyId);
    return Results.Ok(new { authenticated = true });
}).AddEndpointFilter<CsrfFilter>();

api.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    return Results.NoContent();
}).RequireAuthorization().AddEndpointFilter<CsrfFilter>();

api.MapGet("/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, CurrentTenant tenant) =>
{
    var user = await users.GetUserAsync(principal);
    var current = await tenant.ResolveAsync();
    return user is null || current is null ? Results.Unauthorized() : Results.Ok(new
    {
        userId = user.Id, user.DisplayName, academyId = current.AcademyId, academyName = current.AcademyName, role = current.Role.ToString()
    });
}).RequireAuthorization(AcademyPermissions.TenantAccess);

api.MapGet("/my-academies", async (ClaimsPrincipal principal, FoundationDbContext db) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var items = await db.AcademyMemberships.AsNoTracking()
        .Where(x => x.UserId == userId && x.IsActive && x.Academy.IsActive)
        .OrderBy(x => x.Academy.ArabicName)
        .Select(x => new { academyId = x.AcademyId, academyName = x.Academy.ArabicName, role = x.Role.ToString() }).ToListAsync();
    return Results.Ok(items);
}).RequireAuthorization();

api.MapPost("/session/academy", async (SelectAcademyRequest request, ClaimsPrincipal principal,
    UserManager<ApplicationUser> users, FoundationDbContext db, HttpContext context) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var ownsMembership = await db.AcademyMemberships.AsNoTracking()
        .AnyAsync(x => x.UserId == userId && x.AcademyId == request.AcademyId && x.IsActive && x.Academy.IsActive);
    if (!ownsMembership) return Results.Forbid();
    var user = await users.FindByIdAsync(userId.ToString());
    if (user is null || !user.IsActive) return Results.Unauthorized();
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    await Program.SignInAsync(context, user, request.AcademyId);
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
    if (await db.AcademyMemberships.AnyAsync(x => x.AcademyId == current.AcademyId && x.UserId == user.Id))
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

if (app.Environment.IsEnvironment("Demo")) await DemoSeed.SeedAsync(app.Services);
app.Run();

public sealed record LoginRequest(string Email, string Password);
public sealed record GuardianOtpRequest(string PhoneNumber);
public sealed record GuardianOtpVerifyRequest(Guid ChallengeId, string PhoneNumber, string Code);
public sealed record SelectAcademyRequest(Guid AcademyId);
public sealed record CreateStaffRequest(string Email, string DisplayName, string TemporaryPassword, AcademyRole Role);

public partial class Program
{
    public static async Task SignInAsync(HttpContext context, ApplicationUser user, Guid academyId)
    {
        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName ?? user.Id.ToString()));
        identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp ?? string.Empty));
        identity.AddClaim(new Claim("academy_id", academyId.ToString()));
        var principal = new ClaimsPrincipal(identity);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = false, AllowRefresh = true, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });
    }
}
