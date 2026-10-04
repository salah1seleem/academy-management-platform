using System.ComponentModel.DataAnnotations;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Shared identity is never renamed, re-numbered or given a new password by another academy.
public static class CoachProvisioning
{
    public sealed record Request(string? DisplayName, string? PhoneNumber, string? Email, bool IsActive, Guid[]? GroupIds);

    public static void MapCoachProvisioning(this WebApplication app) => app.MapPost("/api/v1/manage/coaches", Create)
        .RequireAuthorization(AcademyPermissions.StaffProvision).AddEndpointFilter<CsrfFilter>();

    private static async Task<IResult> Create(Request request, CurrentTenant tenant, FoundationDbContext db,
        UserManager<ApplicationUser> users, TimeProvider clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var phone = EgyptPhoneNormalizer.Normalize(request.PhoneNumber);
        var name = request.DisplayName?.Trim(); var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email)) email = null;
        var groups = request.GroupIds?.Distinct().ToArray() ?? [];
        if (phone is null || string.IsNullOrWhiteSpace(name) || name.Length > 160 || groups.Length > 50 ||
            (email is not null && (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))))
            return Results.BadRequest(new { message = "راجع الاسم ورقم الهاتف المصري والبريد والمجموعات." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serializes normalized phone provisioning across academies and retries.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({phone}, 937))");
        if (await db.TrainingGroups.CountAsync(g => g.AcademyId == current.AcademyId && groups.Contains(g.Id) && g.IsActive) != groups.Length)
            return Results.BadRequest(new { message = "إحدى المجموعات غير متاحة في أكاديميتك." });
        var user = await db.Users.SingleOrDefaultAsync(u => u.PhoneNumber == phone);
        if (user is not null && (!user.IsActive || user.DisplayName != name ||
            (email is not null && users.NormalizeEmail(email) != user.NormalizedEmail)))
            return Results.Conflict(new { message = "تعذر ربط الهوية بهذه البيانات. راجع بيانات صاحب الحساب؛ لم يتغير أي حساب قائم." });
        if (user is null)
        {
            if (email is not null && await users.FindByEmailAsync(email) is not null)
                return Results.Conflict(new { message = "تعذر إنشاء الهوية بهذه البيانات؛ راجع البريد والهاتف." });
            user = new ApplicationUser { Id = Guid.NewGuid(), UserName = phone, Email = email, EmailConfirmed = false,
                PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = name, IsActive = true,
                CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
            var result = await users.CreateAsync(user);
            if (!result.Succeeded) return Results.Conflict(new { message = "تعذر إنشاء حساب المدرب؛ راجع البيانات." });
        }
        var membership = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.AcademyId == current.AcademyId && m.UserId == user.Id && m.Role == AcademyRole.Coach);
        if (membership is not null)
        {
            // A repeated create never changes an existing membership or assignment.
            var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyId == current.AcademyId && a.AcademyMembershipId == membership.Id && a.IsActive).Select(a => a.TrainingGroupId).ToArrayAsync();
            return membership.IsActive == request.IsActive && assigned.Order().SequenceEqual(groups.Order())
                ? Results.Ok(new { membershipId = membership.Id, alreadyExists = true })
                : Results.Conflict(new { message = "المدرب موجود بالفعل. استخدم إدارة الإسناد لتغيير مجموعاته." });
        }
        membership = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = current.AcademyId, UserId = user.Id,
            Role = AcademyRole.Coach, IsActive = request.IsActive, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.AcademyMemberships.Add(membership);
        foreach (var group in groups) db.StaffGroupAssignments.Add(new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = current.AcademyId,
            AcademyMembershipId = membership.Id, TrainingGroupId = group, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Results.Created($"/api/v1/manage/structure/coaches/{membership.Id}", new { membershipId = membership.Id });
    }
}
