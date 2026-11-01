using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class NoticeText
{
    // An edited template changes future messages only; published notices remain snapshots.
    public static async Task<string> Render(FoundationDbContext db,Guid academy,string key,string fallback,string child="",string outcome="",string reason="",string guardian="",int? count=null,DateOnly? date=null,string amount="")
    {
        var template=await db.NoticeTemplates.Where(t=>t.AcademyId==academy&&t.Key==key).Select(t=>t.Message).SingleOrDefaultAsync();
        return RenderValues(template??fallback,child,outcome,reason,guardian,count,date,amount);
    }
    public static string RenderValues(string template,string child="",string outcome="",string reason="",string guardian="",int? count=null,DateOnly? date=null,string amount="")
    {
        var values=new Dictionary<string,string>{["amount"]=amount,["القيمة"]=amount,["children"]=child,["اسم_اللاعب"]=child,["أسماء_الأبناء"]=child,["team"]=child,["اسم_الفريق"]=child,
            ["outcome"]=outcome,["النتيجة"]=outcome,["reason"]=reason,["السبب"]=reason,
            ["guardian"]=guardian,["اسم_ولي_الأمر"]=guardian,
            ["count"]=count?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",["عدد_المرات"]=count?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",
            ["date"]=date?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"",["التاريخ"]=date?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??""};
        // One substitution pass: a player's name is data, never a second template.
        return System.Text.RegularExpressions.Regex.Replace(template,@"\{([^{}]+)\}",m=>values.TryGetValue(m.Groups[1].Value,out var v)?v:m.Value);
    }

    public static string[] Variables(string key)
    {
        string[] child=["children","اسم_اللاعب","أسماء_الأبناء"];
        string[] guardian=["guardian","اسم_ولي_الأمر"];
        return key switch{
            "Overdue" or "Renewal" => [..child,..guardian,"amount","القيمة"],
            "Enrollment" or "SingleRenewal" or "FamilyRenewal" or "Star" => [..child,..guardian],
            "DaysAdded" or "DaysDeducted" or "SessionsAdded" => [..child,..guardian,"count","عدد_المرات","date","التاريخ"],
            "RecordedAbsence" or "Expiring" or "Expired" => [..child,..guardian,"date","التاريخ"],
            "Absence" => [..child,"count","عدد_المرات"],
            "Evaluation" => child,
            "Match" => [..child,"team","اسم_الفريق"],
            _ => []};
    }
    // Keep older stored aliases valid, but offer Arabic names with the event's
    // actual context. Match notifications contain a team, not an invented child.
    public static string[] EditorVariables(string key) => key=="Match"?["اسم_الفريق"]:
        Variables(key).Where(v=>v.Any(c=>c>127)).ToArray();
    public static bool ValidVariables(string key,string message) => System.Text.RegularExpressions.Regex.Matches(message,@"\{([^{}]+)\}").All(m=>Variables(key).Contains(m.Groups[1].Value));

    // Called inside the proof transaction. A repeated upload updates the proof,
    // not a second notification. Only owners receive this financial queue link.
    public static async Task PaymentProof(FoundationDbContext db,Guid academy,Guid request,string kind,DateTimeOffset now,bool productOrder=false)
    {
        var owners=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.IsActive&&m.Role==AcademyRole.AcademyOwner&&m.User.IsActive).Select(m=>m.UserId).Distinct().ToArrayAsync();
        foreach(var user in owners){
            var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"proof-notice/v1/{academy}/{request}/{user}"))[..16]);
            if(db.AcademyNotices.Local.Any(n=>n.Id==id)||await db.AcademyNotices.AnyAsync(n=>n.Id==id))continue;
            db.AcademyNotices.Add(new(){EventKey="Payment",Id=id,AcademyId=academy,UserId=user,AudienceRole="AcademyOwner",TargetKind=productOrder?"ProductReview":"PaymentQueue",TargetId=request,
                Message=await Render(db,academy,"Payment","وصل إثبات دفع جديد؛ راجع الطلب والإيصال قبل القبول.")+"\n"+kind,CreatedAtUtc=now,UpdatedAtUtc=now});
        }
    }
}
