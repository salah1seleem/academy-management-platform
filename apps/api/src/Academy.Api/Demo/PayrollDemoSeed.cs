using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Called only within FootballDemoSeed's validated Demo-only tenant transaction.
internal static class PayrollDemoSeed
{
    internal static async Task AddOnce(FoundationDbContext db, CancellationToken ct)
    {
        var academy = FootballDemoSeed.AcademyId;
        var marker = FootballDemoSeed.Id("payroll-demo/upgrade-marker");
        if (!await db.PayrollReasons.AnyAsync(x => x.AcademyId == academy && x.Name == "سبب آخر", ct))
        {
            db.PayrollReasons.Add(new PayrollReason { Id = FootballDemoSeed.Id("payroll-demo/reason/other"), AcademyId = academy, Name = "سبب آخر", CreatedAtUtc = DateTimeOffset.Parse("2026-10-08T06:00:00Z"), UpdatedAtUtc = DateTimeOffset.Parse("2026-10-08T06:00:00Z") });
            await db.SaveChangesAsync(ct);
        }
        if (await db.PayrollReasons.AnyAsync(x => x.AcademyId == academy && x.Id == marker, ct)) return;
        var time = DateTimeOffset.Parse("2026-10-08T06:00:00Z"); var date = new DateOnly(2026, 10, 8);
        var owner = FootballDemoSeed.Id("user/owner");
        Guid Id(string key) => FootballDemoSeed.Id("payroll-demo/" + key);
        string[] jobs = ["مدرب كرة قدم", "مدرب كرة قدم", "مدرب كرة قدم", "مدرب كرة قدم", "إداري", "عامل تجهيز ملاعب", "أخصائي علاج طبيعي"];
        string[] names = ["محمود طارق سليمان", "كريم وائل منصور", "حسام شريف عادل", "عمرو أيمن فوزي", "إداري أكاديمية العرض", "عامل عرض تجريبي", "أخصائي عرض تجريبي"];
        decimal[] salaries = [6500, 7000, 7500, 8000, 7000, 3500, 9000];
        for (var i = 0; i < names.Length; i++)
        {
            var member = i < 4 ? FootballDemoSeed.Id($"member/coach{i + 1}") : i == 4 ? (Guid?)FootballDemoSeed.Id("member/admin") : null;
            var phone = i < 4 ? FootballDemoSeed.Phone(20 + i) : i == 4 ? FootballDemoSeed.Phone(11) : FootballDemoSeed.Phone(910 + i);
            // Existing manual employee records are preserved rather than linked/overwritten silently.
            if (await db.Employees.AnyAsync(x => x.AcademyId == academy && (x.Phone == phone || member.HasValue && x.MembershipId == member), ct)) continue;
            db.Employees.Add(new Employee { Id = Id($"employee/{i}"), AcademyId = academy, Name = names[i], Phone = phone, JobTitle = jobs[i], MembershipId = member, BranchId = FootballDemoSeed.Id($"branch/{i % 3}"), MonthlySalary = salaries[i], Currency = "EGP", ContractStart = i == 6 ? new(2026, 10, 8) : new(2026, 9, 1), ContractEnd = i == 6 ? new(2026, 10, 25) : null, CreatedAtUtc = time, UpdatedAtUtc = time });
        }
        string[] reasons = ["ساعة إضافية", "حضور مباراة", "يوم إضافي"];
        var reasonIds = new Guid[reasons.Length];
        for (var i = 0; i < reasons.Length; i++)
        {
            var name = reasons[i];
            var existing = await db.PayrollReasons.SingleOrDefaultAsync(x => x.AcademyId == academy && x.Name == name, ct);
            reasonIds[i] = existing?.Id ?? Id($"reason/{i}");
            if (existing is null) db.PayrollReasons.Add(new PayrollReason { Id = reasonIds[i], AcademyId = academy, Name = name, CreatedAtUtc = time, UpdatedAtUtc = time });
        }
        db.PayrollReasons.Add(new PayrollReason { Id = marker, AcademyId = academy, Name = "تهيئة سيناريوهات الرواتب — ديمو", IsActive = false, CreatedAtUtc = time, UpdatedAtUtc = time });
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < 3; i++)
        {
            var employeeId = Id($"employee/{i}");
            if (!await db.Employees.AnyAsync(x => x.AcademyId == academy && x.Id == employeeId, ct)) continue;
            var status = i == 0 ? "Approved" : i == 1 ? "Pending" : "Rejected";
            var row = new PayrollAdditionRequest { Id = Id($"request/{i}"), AcademyId = academy, EmployeeId = Id($"employee/{i}"), ReasonId = reasonIds[i], ReasonName = reasons[i], Amount = i == 0 ? 300 : 0, Currency = "EGP", Status = status, RequestedByUserId = FootballDemoSeed.Id($"user/coach{i + 1}"), ReviewedByUserId = i == 1 ? null : owner, ReviewedAtUtc = i == 1 ? null : time, DueDate = i == 0 ? date : null, ReviewNote = i == 2 ? "مثال رفض: اليوم الإضافي غير معتمد" : null, IdempotencyKey = $"demo-request-{i}", CreatedAtUtc = time, UpdatedAtUtc = time };
            db.PayrollAdditionRequests.Add(row);
            if (i == 0) db.PayrollAdjustments.Add(new PayrollAdjustment { Id = Id("addition"), AcademyId = academy, EmployeeId = row.EmployeeId, Kind = "Addition", AdditionRequestId = row.Id, Amount = row.Amount, Currency = "EGP", DueDate = date, Reason = row.ReasonName, PerformedByUserId = owner, IdempotencyKey = "demo-approved-addition", CreatedAtUtc = time, UpdatedAtUtc = time });
        }
        var firstEmployee = Id("employee/0"); var worker = Id("employee/5");
        if (await db.Employees.AnyAsync(x => x.AcademyId == academy && x.Id == firstEmployee, ct)) db.PayrollAdjustments.Add(new PayrollAdjustment { Id = Id("deduction"), AcademyId = academy, EmployeeId = firstEmployee, Kind = "Deduction", Amount = 100, Currency = "EGP", DueDate = date, Reason = "خصم تأخير — مثال اصطناعي", PerformedByUserId = owner, IdempotencyKey = "demo-manual-deduction", CreatedAtUtc = time, UpdatedAtUtc = time });
        if (await db.Employees.AnyAsync(x => x.AcademyId == academy && x.Id == worker, ct)) db.PayrollSettlements.Add(new PayrollSettlement { Id = Id("paid-month"), AcademyId = academy, EmployeeId = worker, Month = new(2026, 10, 1), MonthlySalary = 3500, ContractDays = 31, BaseDue = 3500, NetDue = 3500, Currency = "EGP", PaidAtUtc = time, PaidByUserId = owner, IdempotencyKey = "demo-paid-worker-month", CreatedAtUtc = time, UpdatedAtUtc = time });
        await db.SaveChangesAsync(ct);
    }
}
