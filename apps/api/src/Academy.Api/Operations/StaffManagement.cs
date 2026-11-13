using System.ComponentModel.DataAnnotations;
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
        app.MapGet("/api/v1/my-permissions", async (HttpContext context, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization) => { context.Response.Headers.CacheControl = "no-store"; var allowed = new List<string>(); foreach (var p in Permissions.Concat([AcademyPermissions.FacilitiesRead, AcademyPermissions.FacilitiesManage, AcademyPermissions.FollowUpRead, AcademyPermissions.FollowUpManage]).Concat(PayrollSensitiveAccess.Permissions)) if ((await authorization.AuthorizeAsync(context.User, p)).Succeeded) allowed.Add(p); return Results.Ok(allowed); }).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var members = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.PrimaryMembershipId == null && (x.Role == AcademyRole.AcademyAdmin || x.Role == AcademyRole.Coach)).OrderBy(x => x.User.DisplayName).Select(x => new { x.Id, name = x.User.DisplayName, x.User.Email, x.User.PhoneNumber, role = x.Role.ToString(), x.JobTitle, x.IsActive, grants = db.StaffGrants.Where(g => g.AcademyId == t.AcademyId && g.MembershipId == x.Id).Select(g => new { g.Permission, g.Allowed }).ToList() }).ToListAsync();
        return Results.Ok(new { permissions = Permissions, members });
    }
    private static async Task<IResult> Create(StaffWrite body, CurrentTenant tenant, FoundationDbContext db, UserManager<ApplicationUser> users, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var phone = EgyptPhoneNormalizer.NormalizeForNewMobile(body.Phone);
        if (phone is null)
            return Results.UnprocessableEntity(new { message = "اكتب رقم موبايل مصريًا يبدأ بـ010 أو 011 أو 012 أو 015، أو استخدم رمز مصر ‎+20." });
        var name = body.Name?.Trim();
        var job = body.JobTitle?.Trim();
        if (body.Role is not (AcademyRole.AcademyAdmin or AcademyRole.Coach) || string.IsNullOrEmpty(name) || name.Length > 100 || job?.Length > 100)
            return Results.UnprocessableEntity(new { message = "راجع الاسم والمسمى الوظيفي والدور." });
        var email = string.IsNullOrWhiteSpace(body.Email) ? $"staff.{Guid.NewGuid():N}@accounts.invalid" : body.Email.Trim();
        if (email.Length > 200 || !new EmailAddressAttribute().IsValid(email))
            return Results.UnprocessableEntity(new { message = "اكتب بريدًا إلكترونيًا صالحًا." });

        static IResult Duplicate() => Results.Conflict(new { message = "رقم الهاتف أو البريد الإلكتروني مستخدم بالفعل." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await users.FindByEmailAsync(email) is not null || await db.Users.AnyAsync(x => x.PhoneNumber == phone)) return Duplicate();
        var password = string.IsNullOrEmpty(body.TemporaryPassword)
            ? "A!a9" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            : body.TemporaryPassword;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, PhoneNumber = phone, DisplayName = name, IsActive = true, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        try
        {
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                return result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                    ? Duplicate()
                    : Results.UnprocessableEntity(new { message = "استخدم كلمة مرور من 12 خانة على الأقل، فيها أحرف كبيرة وصغيرة ورقم ورمز." });
            var member = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = user.Id, Role = body.Role, JobTitle = string.IsNullOrEmpty(job) ? null : job, IsActive = true, AllBranches = body.Role != AcademyRole.AcademyAdmin, UseActionGrants = body.Role == AcademyRole.AcademyAdmin, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
            db.AcademyMemberships.Add(member);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { member.Id });
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "IX_Users_PhoneNumber" or "EmailIndex" or "UserNameIndex" })
        {
            return Duplicate();
        }
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
        var changed = false;
        foreach (var input in body.Grants)
        {
            var row = rows.SingleOrDefault(x => x.Permission == input.Permission);
            if (row is not null && row.IsActive && row.Allowed == input.Allowed) continue;
            if (row is null)
            {
                row = new StaffGrant { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, Permission = input.Permission, CreatedAtUtc = clock.UtcNow };
                db.StaffGrants.Add(row);
            }
            row.Allowed = input.Allowed;
            row.IsActive = true;
            row.UpdatedAtUtc = clock.UtcNow;
            changed = true;
        }
        if (changed)
        {
            member.ContextRevision++;
            member.UpdatedAtUtc = clock.UtcNow;
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }
}
public sealed record StaffWrite(string Name, string? Email, string Phone, string? TemporaryPassword, AcademyRole Role, string? JobTitle);
public sealed record GrantValue(string Permission, bool Allowed);
public sealed record GrantWrite(GrantValue[] Grants);
