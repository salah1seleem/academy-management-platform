using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.Api.Finance;

internal sealed record CashPaymentActor(TenantMembership Tenant,Guid User,Guid Session,Guid CollectingBranch);
internal sealed record VerifiedCashPayment(Guid Academy,Guid User,Guid Membership,Guid Session,Guid Enrollment,DateTimeOffset ExpiresAt)
{
    internal void Check(CashPaymentActor actor,Guid enrollment,bool fresh,DateTimeOffset now)
    {
        if(Academy!=actor.Tenant.AcademyId||User!=actor.User||Membership!=actor.Tenant.MembershipId||Session!=actor.Session||Enrollment!=enrollment)
            throw new CashRejected("مرجع الدفع غير متاح لهذه العضوية أو الوردية.",403);
        if(fresh&&ExpiresAt<=now)throw new CashRejected("انتهت مهلة البحث. ابحث عن التسجيل وراجع المبلغ من جديد.",409);
    }
}

public sealed class CashPaymentReferences(IDataProtectionProvider provider,TimeProvider time)
{
    private readonly IDataProtector protector=provider.CreateProtector("Academy.CrossBranchCashPayment.v1");
    internal string Create(CashPaymentActor actor,Guid enrollment)=>"CP1."+protector.Protect(JsonSerializer.Serialize(new VerifiedCashPayment(actor.Tenant.AcademyId,actor.User,actor.Tenant.MembershipId,actor.Session,enrollment,time.GetUtcNow().AddMinutes(10))));
    internal VerifiedCashPayment Resolve(string reference,CashPaymentActor actor,bool fresh)
    {
        if(string.IsNullOrEmpty(reference)||reference.Length>1600||!reference.StartsWith("CP1.",StringComparison.Ordinal))throw new CashRejected("مرجع الدفع غير صالح. أعد البحث عن التسجيل.",422);
        try
        {
            var scope=JsonSerializer.Deserialize<VerifiedCashPayment>(protector.Unprotect(reference[4..]))??throw new CryptographicException();
            scope.Check(actor,scope.Enrollment,false,time.GetUtcNow());
            if(fresh&&scope.ExpiresAt<=time.GetUtcNow())throw new CashRejected("انتهت مهلة البحث. ابحث عن التسجيل وراجع المبلغ من جديد.",409);
            return scope;
        }
        catch(Exception e) when(e is CryptographicException or JsonException or FormatException){throw new CashRejected("مرجع الدفع غير صالح. أعد البحث عن التسجيل.",422);}
    }
}
