using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class StaffManagement
{
    public static readonly string[] Permissions = [AcademyPermissions.AcademySettingsManage, AcademyPermissions.StructureManage, AcademyPermissions.PeopleManage, AcademyPermissions.SubscriptionPlanManage, AcademyPermissions.SubscriptionRead, AcademyPermissions.SubscriptionRenewManage, AcademyPermissions.SubscriptionAdjustmentManage, AcademyPermissions.SubscriptionDiscountManage, AcademyPermissions.PaymentRead, AcademyPermissions.CollectionRead, AcademyPermissions.AttendanceRead, AcademyPermissions.AttendanceManage, AcademyPermissions.EvaluationRead, AcademyPermissions.EvaluationManage, AcademyPermissions.EvaluationCriteriaManage, AcademyPermissions.EnrollmentRequestRead, AcademyPermissions.EnrollmentRequestManage, AcademyPermissions.SportCatalogManage, AcademyPermissions.NutritionManage, AcademyPermissions.MedicalRecordRead, AcademyPermissions.MedicalRecordManage, AcademyPermissions.PlayerMediaRead, AcademyPermissions.PlayerMediaManage, AcademyPermissions.ReportsFinancialRead, AcademyPermissions.ReportsAttendanceRead, AcademyPermissions.ReportsExport, AcademyPermissions.OwnerDashboardRead, AcademyPermissions.StaffProvision, AcademyPermissions.PayrollRead, AcademyPermissions.PayrollManage];
    public static void MapStaffManagement(this WebApplication app)
    {
        var staff = app.MapGroup("/api/v1/manage/staff").RequireAuthorization(AcademyPermissions.TenantAccess);
        staff.MapGet("/", List);
        staff.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        staff.MapPut("/{id:guid}/grants", Grants).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/my-permissions", async (HttpContext context, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization) => { var allowed = new List<string>(); foreach (var p in Permissions.Concat([AcademyPermissions.FacilitiesRead, AcademyPermissions.FacilitiesManage])) if ((await authorization.AuthorizeAsync(context.User, p)).Succeeded) allowed.Add(p); return Results.Ok(allowed); }).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var members = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && (x.Role == AcademyRole.AcademyAdmin || x.Role == AcademyRole.Coach)).OrderBy(x => x.User.DisplayName).Select(x => new { x.Id, name = x.User.DisplayName, x.User.Email, x.User.PhoneNumber, role = x.Role.ToString(), x.JobTitle, x.IsActive, grants = db.StaffGrants.Where(g => g.AcademyId == t.AcademyId && g.MembershipId == x.Id).Select(g => new { g.Permission, g.Allowed }).ToList() }).ToListAsync();
        return Results.Ok(new { permissions = Permissions, members });
    }
    private static async Task<IResult> Create(StaffWrite body, CurrentTenant tenant, FoundationDbContext db, UserManager<ApplicationUser> users, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var phone = EgyptPhoneNormalizer.Normalize(body.Phone); if (body.Role is not (AcademyRole.AcademyAdmin or AcademyRole.Coach) || string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 100 || body.JobTitle?.Length > 100 || string.IsNullOrWhiteSpace(body.Email) || body.Email.Length > 200 || !body.Email.Contains('@') || string.IsNullOrWhiteSpace(body.TemporaryPassword) || phone is null) return Results.UnprocessableEntity(new { message = "أدخل الاسم والبريد والهاتف والدور بصورة صحيحة." });
        if (await users.FindByEmailAsync(body.Email) is not null || await db.Users.AnyAsync(x => x.PhoneNumber == phone)) return Results.Conflict(new { message = "البريد أو الهاتف مستخدم بالفعل. لا تُنشئ هوية مشتركة." });
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = body.Email.Trim(), Email = body.Email.Trim(), PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = body.Name.Trim(), IsActive = true, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        var result = await users.CreateAsync(user, body.TemporaryPassword); if (!result.Succeeded) return Results.UnprocessableEntity(new { message = "تعذر إنشاء الحساب. استخدم كلمة مرور قوية وبريدًا صحيحًا." });
        var member = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, Role = body.Role, JobTitle = body.JobTitle?.Trim(), IsActive = true, AllBranches = body.Role != AcademyRole.AcademyAdmin, UseActionGrants = body.Role == AcademyRole.AcademyAdmin, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; db.AcademyMemberships.Add(member); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { member.Id });
    }
    private static async Task<IResult> Grants(Guid id, GrantWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var member = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.AcademyAdmin); if (member is null) return Results.NotFound();
        if (body.Grants is null || body.Grants.Length > Permissions.Length || body.Grants.Select(x => x.Permission).Distinct().Count() != body.Grants.Length || body.Grants.Any(x => !Permissions.Contains(x.Permission))) return Results.UnprocessableEntity();
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{id}"}, 0))");
        await db.Entry(member).ReloadAsync();
        if (member.UseActionGrants) return Results.Conflict(new { message = "هذا الموظف يستخدم مصفوفة صلاحيات الوحدات والفروع؛ عدّل الصلاحيات من شاشة صلاحيات الموظف." });
        var rows = await db.StaffGrants.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == id).ToListAsync();
        foreach (var input in body.Grants) { var row = rows.SingleOrDefault(x => x.Permission == input.Permission); if (row is null) { row = new StaffGrant { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, Permission = input.Permission, CreatedAtUtc = clock.UtcNow }; db.StaffGrants.Add(row); } row.Allowed = input.Allowed; row.IsActive = true; row.UpdatedAtUtc = clock.UtcNow; }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }
}
public sealed record StaffWrite(string Name, string Email, string Phone, string TemporaryPassword, AcademyRole Role, string? JobTitle);
public sealed record GrantValue(string Permission, bool Allowed);
public sealed record GrantWrite(GrantValue[] Grants);
