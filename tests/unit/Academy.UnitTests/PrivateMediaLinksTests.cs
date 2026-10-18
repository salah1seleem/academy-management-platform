using Academy.Api.Operations;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.UnitTests;
public sealed class PrivateMediaLinksTests
{
    private sealed class Clock:TimeProvider{public DateTimeOffset Now=new(2026,10,18,10,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
    private static MediaLinkContext Scope()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Guardian",1,"web");
    [Fact] public void Ticket_expires_at_exactly_two_minutes_and_a_clock_reversal_fails_closed()
    {
        var clock=new Clock();var links=new PrivateMediaLinks(new EphemeralDataProtectionProvider(),clock);var scope=Scope();var token=links.Issue(scope);
        Assert.True(links.Allows(token,scope));clock.Now=clock.Now.AddSeconds(119);Assert.True(links.Allows(token,scope));clock.Now=clock.Now.AddSeconds(1);Assert.False(links.Allows(token,scope));clock.Now=clock.Now.AddMinutes(-3);Assert.False(links.Allows(token,scope));
    }
    [Fact] public void Ticket_is_not_transferable_between_tenant_asset_user_membership_role_context_or_mobile_session()
    {
        var links=new PrivateMediaLinks(new EphemeralDataProtectionProvider(),new Clock());var scope=Scope();var token=links.Issue(scope);
        foreach(var other in new[]{scope with{Academy=Guid.NewGuid()},scope with{Asset=Guid.NewGuid()},scope with{User=Guid.NewGuid()},scope with{Membership=Guid.NewGuid()},scope with{Role="AcademyOwner"},scope with{ContextRevision=2},scope with{Authentication="another-mobile-session"}})Assert.False(links.Allows(token,other));
        Assert.True(links.Allows(token,scope));
    }
    [Theory][InlineData("")][InlineData("ML1.invalid")][InlineData("not-a-media-link")]
    public void Invalid_tokens_never_raise_a_user_visible_technical_exception(string token)=>Assert.False(new PrivateMediaLinks(new EphemeralDataProtectionProvider(),new Clock()).Allows(token,Scope()));
    [Fact] public void Oversized_tampered_or_differently_protected_tokens_are_rejected()
    {
        var protection=new EphemeralDataProtectionProvider();var links=new PrivateMediaLinks(protection,new Clock());var scope=Scope();var token=links.Issue(scope);var index=token.Length/2;var tampered=token[..index]+(token[index]=='a'?'b':'a')+token[(index+1)..];
        Assert.False(links.Allows(tampered,scope));Assert.False(links.Allows("ML1."+new string('a',4097),scope));Assert.False(links.Allows("ML1."+protection.CreateProtector("other-purpose").Protect("{}"),scope));
    }
}
