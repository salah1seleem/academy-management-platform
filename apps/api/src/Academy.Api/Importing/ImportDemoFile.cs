using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Slice2;
using Academy.Api.Slice3;

namespace Academy.Api.Importing;

// Downloads synthetic examples only. It neither resets nor imports any records.
public static class ImportDemoFile
{
    public static ImportWorkbook? Create(ImportReferences refs, ISubscriptionClock clock)
    {
        if (refs.AcademyId != DemoSeed.NogoomAcademyId && refs.AcademyId != FootballDemoSeed.AcademyId) return null;
        var primaryId = refs.AcademyId == DemoSeed.NogoomAcademyId ? Slice2DemoSeed.FootballGroupId : FootballDemoSeed.Id("group/0");
        var primary = refs.Find("group", primaryId.ToString()); if (primary is null) return null;
        var birthYear = primary.MinYear ?? primary.MaxYear ?? 2016;
        var groups = new[] { primary }.Concat(refs.Values.Where(g => g.Kind == "group" && g.Id == Slice2DemoSeed.SwimmingGroupId && g.MinYear <= birthYear && g.MaxYear >= birthYear)).ToArray();
        static IReadOnlyList<ImportCell> Row(params string[] v) => v.Select(x => new ImportCell(x, true)).ToArray();
        var template = refs.Template(clock.Today); var rows = new List<IReadOnlyList<ImportCell>> { template.Sheet("Enrollments").Rows[0] };
        var month = new DateOnly(clock.Today.Year, clock.Today.Month, 1); var end = month.AddMonths(1).AddDays(-1);
        for (var i = 0; i < 2 + (groups.Length > 1 ? 1 : 0); i++)
        {
            var g = groups[i == 2 ? 1 : 0]; var child = i == 2 ? 0 : i;
            var planId = refs.AcademyId == DemoSeed.NogoomAcademyId ? g.Sport == Slice2DemoSeed.SwimmingId ? Slice3DemoSeed.SwimmingMonthlyId : Slice3DemoSeed.FootballMonthlyId : FootballDemoSeed.Id("plan/month");
            var plan = refs.Values.SingleOrDefault(p => p.Kind == "product" && p.Id == planId && p.Sport == g.Sport && (p.AllBranches || p.Branches?.Contains(g.Branch!.Value) == true)); if (plan is null) return null;
            rows.Add(Row("DEMO-P" + child, "DEMO-E" + i, "DEMO-FAMILY", child == 0 ? "آدم مثال الاستيراد" : "ليلى مثال الاستيراد", "ولي أمر مثال الاستيراد", "01088888201", birthYear + "-01-01", g.Branch!.Value.ToString(), g.Sport!.Value.ToString(), "", g.Id.ToString(), plan.Id.ToString(), month.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), plan.PlanType == Academy.Infrastructure.Subscriptions.SubscriptionPlanType.Duration ? "" : "8", plan.PlanType == Academy.Infrastructure.Subscriptions.SubscriptionPlanType.Duration ? "" : "5", "agreed_net", "", "", "540.00", refs.Currency, "existing", "مثال Demo اصطناعي — ليس سجل عميل أو إثبات دفع"));
        }
        return new(template.Sheets.Select(s => s.Name switch {
            "Enrollments" => s with { Rows = rows },
            "Metadata" => s with { Rows = s.Rows.Select(r => r[0].Value == "SourceSystem" ? Row("SourceSystem", "demo-historical-v2") : r).ToArray() },
            "OpenBalances" => s with { Rows = [s.Rows[0], Row("DEMO-E0", "500.00", refs.Currency, clock.Today.ToString("yyyy-MM-dd"), "DEMO-OPEN-500", "سند اعتماد اصطناعي للعرض فقط")] },
            _ => s }).ToArray());
    }
}
