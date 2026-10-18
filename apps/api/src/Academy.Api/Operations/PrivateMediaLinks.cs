using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.Api.Operations;

// A link is not a bearer authorization: Download rechecks the live resource policy first.
public sealed record MediaLinkContext(Guid Academy,Guid Asset,Guid User,Guid Membership,string Role,int ContextRevision,string Authentication);
public sealed class PrivateMediaLinks(IDataProtectionProvider protection,TimeProvider clock)
{
    private readonly IDataProtector protector=protection.CreateProtector("Academity.PrivateMedia.Access.v1");
    public static readonly TimeSpan Lifetime=TimeSpan.FromMinutes(2);
    private sealed record Ticket(MediaLinkContext Context,DateTimeOffset IssuedAtUtc,DateTimeOffset ExpiresAtUtc);
    public string Issue(MediaLinkContext scope)
    {
        var now=clock.GetUtcNow();
        return "ML1."+protector.Protect(JsonSerializer.Serialize(new Ticket(scope,now,now+Lifetime)));
    }
    public bool Allows(string token,MediaLinkContext scope)
    {
        if(token.Length>4096||!token.StartsWith("ML1.",StringComparison.Ordinal))return false;
        try
        {
            var ticket=JsonSerializer.Deserialize<Ticket>(protector.Unprotect(token[4..]));
            var now=clock.GetUtcNow();
            return ticket!=null&&ticket.Context==scope&&ticket.IssuedAtUtc<=now&&now<ticket.ExpiresAtUtc&&ticket.ExpiresAtUtc-ticket.IssuedAtUtc==Lifetime;
        }
        catch(Exception e)when(e is CryptographicException or FormatException or JsonException){return false;}
    }
}
