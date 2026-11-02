using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.UnitTests;
public sealed class AttendanceDraftGuardTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private static ClaimsPrincipal Actor(string login = "login-a", string user = "user-a", bool mobile = false) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user), new(mobile ? "mobile_session_id" : "attendance_session", login)], "test"));
    [Fact]
    public void Draft_lease_is_scoped_to_login_actor_tenant_membership_role_and_occurrence()
    {
        var clock = new Clock(); var guard = new AttendanceDraftGuard(new EphemeralDataProtectionProvider(), clock);
        var tenant = new TenantMembership(Guid.NewGuid(), "اختبار", AcademyRole.AcademyAdmin, Guid.NewGuid()); var session = Guid.NewGuid();
        var lease = guard.Issue(tenant, Actor(), session);
        Assert.NotNull(lease.Token); Assert.True(guard.Valid(tenant, Actor(), session, lease.Token));
        Assert.False(guard.Valid(tenant, Actor("login-b"), session, lease.Token));
        Assert.False(guard.Valid(tenant, Actor(user: "user-b"), session, lease.Token));
        Assert.False(guard.Valid(tenant with { AcademyId = Guid.NewGuid() }, Actor(), session, lease.Token));
        Assert.False(guard.Valid(tenant with { MembershipId = Guid.NewGuid() }, Actor(), session, lease.Token));
        Assert.False(guard.Valid(tenant with { Role = AcademyRole.Coach }, Actor(), session, lease.Token));
        Assert.False(guard.Valid(tenant, Actor(), Guid.NewGuid(), lease.Token));
        Assert.False(guard.Valid(tenant, Actor(), session, lease.Token + "tampered"));
        Assert.False(guard.Valid(tenant, Actor(), session, new string('x',4097)));
        clock.Now = lease.Expires!.Value;
        Assert.False(guard.Valid(tenant, Actor(), session, lease.Token));
    }
    [Fact]
    public void Online_compatibility_does_not_issue_an_unbound_lease_and_native_session_is_supported()
    {
        var guard = new AttendanceDraftGuard(new EphemeralDataProtectionProvider(), TimeProvider.System);
        var tenant = new TenantMembership(Guid.NewGuid(), "اختبار", AcademyRole.Coach, Guid.NewGuid()); var session = Guid.NewGuid();
        var unbound = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"test")], "test"));
        Assert.Null(guard.Issue(tenant, unbound, session).Token); Assert.True(guard.Valid(tenant, unbound, session, null)); Assert.False(guard.Valid(tenant, unbound, session, ""));
        var lease = guard.Issue(tenant, Actor(mobile:true), session); Assert.NotNull(lease.Token); Assert.True(guard.Valid(tenant, Actor(mobile:true), session, lease.Token));
        Assert.NotEqual(lease.Scope, guard.Issue(tenant, Actor("new-mobile-session",mobile:true), session).Scope);
    }
}
