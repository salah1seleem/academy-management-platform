using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

// Named operational grants use the existing StaffGrant store and access audit.
// They only narrow module CRUD, never grant a role, resource scope or entitlement.
public static class StaffWorkflowAccess
{
    public sealed record Action(string Key,string Label,string Module,string Operation);
    public static readonly Action[] Actions = [
        new("workflow.players.read","عرض اللاعبين وأولياء الأمور","players","Read"),
        new("workflow.players.create","إضافة لاعب وتسجيل اشتراكه","players","Add"),
        new("workflow.players.edit","تعديل بيانات اللاعبين","players","Edit"),
        new("workflow.subscriptions.renew","تجديد الاشتراكات","subscriptions","Write"),
        new("workflow.attendance.players","تسجيل حضور اللاعبين","attendance","Edit"),
        new("workflow.attendance.staff","تسجيل حضور المدربين والموظفين","attendance","Edit"),
        new("workflow.players.place","تسكين اللاعبين بالمجموعات","players","Edit"),
        new("workflow.groups.manage","إدارة المجموعات والحصص","academy","Write"),
        new("workflow.payments.read","عرض المدفوعات والتحصيلات","payments","Read"),
        new("workflow.payments.collect","تسجيل التحصيل","payments","Write"),
        new("workflow.expenses.record","تسجيل المصروفات","finance","Write"),
        new("workflow.reports.financial","عرض التقارير المالية","finance","Read"),
        new("workflow.followups.update","متابعة الطلبات المسندة","followups","Edit"),
        new("workflow.news.manage","إدارة الأخبار ونشرها","news","Write")
    ];
    public static bool Eligible(Action action,IEnumerable<StaffActionValue> grants)
    {
        var g=grants.SingleOrDefault(g=>g.Module==action.Module);
        return g?.Read==true&&(action.Operation=="Read"||action.Operation=="Add"&&g.Add||action.Operation=="Edit"&&g.Edit||action.Operation=="Write"&&g.Add&&g.Edit);
    }
    public static string[] Required(string path,string method)
    {
        path=path.TrimEnd('/');var read=method is "GET" or "HEAD";
        if(Academy.Api.Operations.StaffAttendanceExceptions.IsOwnPath(path)||Academy.Api.Operations.StaffPasskeys.IsOwnPath(path))return [];
        if(path.StartsWith("/api/v1/reports/financial",StringComparison.Ordinal)||path.StartsWith("/api/v1/reports/operating-statement",StringComparison.Ordinal))return ["workflow.reports.financial"];
        if(read&&(path.StartsWith("/api/v1/subscriptions/collections",StringComparison.Ordinal)||path.StartsWith("/api/v1/subscriptions/payments",StringComparison.Ordinal)||path.StartsWith("/api/v1/manage/payments",StringComparison.Ordinal)||path.StartsWith("/api/v1/manage/subscriptions/payments",StringComparison.Ordinal)))return ["workflow.payments.read"];
        if(read&&path.StartsWith("/api/v1/people/",StringComparison.Ordinal))return ["workflow.players.read"];
        if(read)return [];
        if(path.EndsWith("/quote",StringComparison.Ordinal)||path.EndsWith("/preview",StringComparison.Ordinal))return [];
        if(path=="/api/v1/people/registrations")return ["workflow.players.create","workflow.payments.collect"];
        if(path.StartsWith("/api/v1/people/players/",StringComparison.Ordinal)&&path.Contains("/placement",StringComparison.Ordinal))return ["workflow.players.place"];
        if(path.StartsWith("/api/v1/people/enrollments/",StringComparison.Ordinal)&&(path.Contains("/placement",StringComparison.Ordinal)||path.Contains("/group",StringComparison.Ordinal)||path.Contains("/session-transfer",StringComparison.Ordinal)))return ["workflow.players.place"];
        if(path.StartsWith("/api/v1/manage/groups/",StringComparison.Ordinal)&&path.Contains("/placement",StringComparison.Ordinal))return ["workflow.players.place"];
        if(path.StartsWith("/api/v1/people/players/",StringComparison.Ordinal)&&method is "PUT" or "PATCH")return ["workflow.players.edit"];
        if(path.StartsWith("/api/v1/manage/staff-attendance",StringComparison.Ordinal)||path.StartsWith("/api/v1/attendance/sessions/",StringComparison.Ordinal)&&path.EndsWith("/staff",StringComparison.Ordinal))return ["workflow.attendance.staff"];
        if(path.StartsWith("/api/v1/attendance/sessions/",StringComparison.Ordinal)&&path.EndsWith("/players",StringComparison.Ordinal))return ["workflow.attendance.players"];
        if(path.StartsWith("/api/v1/finance/operating-expenses",StringComparison.Ordinal))return ["workflow.expenses.record"];
        if(path.StartsWith("/api/v1/my-followups/",StringComparison.Ordinal))return ["workflow.followups.update"];
        if(path.StartsWith("/api/v1/manage/news",StringComparison.Ordinal))return ["workflow.news.manage"];
        if(path.StartsWith("/api/v1/manage/payments/",StringComparison.Ordinal)&&(path.EndsWith("/collect",StringComparison.Ordinal)||path.EndsWith("/review",StringComparison.Ordinal)))return ["workflow.payments.collect"];
        if(path.StartsWith("/api/v1/finance/",StringComparison.Ordinal)&&(path.EndsWith("/family-collections",StringComparison.Ordinal)||path.Contains("/cross-branch/families/collect",StringComparison.Ordinal)))return ["workflow.subscriptions.renew","workflow.payments.collect"];
        if(path.StartsWith("/api/v1/finance/",StringComparison.Ordinal)&&(path.EndsWith("/collections",StringComparison.Ordinal)||path.EndsWith("/cross-branch/collect",StringComparison.Ordinal)))return ["workflow.payments.collect"];
        if(path.StartsWith("/api/v1/manage/structure/groups",StringComparison.Ordinal)||path.StartsWith("/api/v1/manage/groups",StringComparison.Ordinal)||path.StartsWith("/api/v1/manage/structure/sessions",StringComparison.Ordinal)||path.StartsWith("/api/v1/attendance/sessions",StringComparison.Ordinal))return ["workflow.groups.manage"];
        if(path.Contains("family-renewals",StringComparison.Ordinal)&&(path.Contains("collect",StringComparison.Ordinal)||path.EndsWith("/review",StringComparison.Ordinal)))return ["workflow.subscriptions.renew","workflow.payments.collect"];
        if(path.Contains("admin-renewals",StringComparison.Ordinal)||path.StartsWith("/api/v1/manage/family-renewals",StringComparison.Ordinal)||path.StartsWith("/api/v1/subscriptions/payments/",StringComparison.Ordinal)&&path.EndsWith("/retry",StringComparison.Ordinal))return ["workflow.subscriptions.renew"];
        if(path.Contains("cash-collect",StringComparison.Ordinal)||path.Contains("cash-collection",StringComparison.Ordinal)||path.Contains("cash-renew",StringComparison.Ordinal))return ["workflow.payments.collect"];
        return [];
    }
    public static async Task<bool> Allows(FoundationDbContext db,Guid membership,string path,string method,CancellationToken token=default)
    {
        var required=Required(path,method);if(required.Length==0)return true;
        // No explicit leaf rows means the existing module grants remain unchanged.
        // Once saved, each leaf is persisted, including explicit denial.
        var explicitRows=await db.StaffGrants.AsNoTracking().Where(g=>g.MembershipId==membership&&g.IsActive&&required.Contains(g.Permission)).ToArrayAsync(token);
        if(explicitRows.Any(g=>!g.Allowed))return false;
        var modules=await db.StaffActionGrants.AsNoTracking().Where(g=>g.MembershipId==membership&&g.IsActive).Select(g=>new StaffActionValue(g.Module,g.Read,g.Add,g.Edit,g.Delete)).ToArrayAsync(token);
        return Actions.Where(a=>explicitRows.Any(r=>r.Permission==a.Key)).All(a=>Eligible(a,modules));
    }
    public static WorkflowGrantRequirement[] Requirements(string path,string method)=>Actions.Where(a=>Required(path,method).Contains(a.Key)).Select(a=>new WorkflowGrantRequirement(a.Key,a.Module,a.Operation)).ToArray();
}
