using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.Api.Operations;

// A payment capability, never a child-profile or guardian-link capability.
public sealed class ScopedRenewalReferences(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("Academy.RenewForAnother.v1").ToTimeLimitedDataProtector();
    public string Create(Guid academy, Guid payer, Guid enrollment) => "SR1." + protector.Protect(JsonSerializer.Serialize(new Scope(academy, payer, enrollment)), TimeSpan.FromMinutes(10));
    public Guid? Resolve(string reference, Guid academy, Guid payer)
    {
        if (!reference.StartsWith("SR1.", StringComparison.Ordinal) || reference.Length > 1200) return null;
        try
        {
            var scope = JsonSerializer.Deserialize<Scope>(protector.Unprotect(reference[4..], out _));
            return scope is not null && scope.Academy == academy && scope.Payer == payer ? scope.Enrollment : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return null; }
    }
    private sealed record Scope(Guid Academy, Guid Payer, Guid Enrollment);
}
