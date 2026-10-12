using Academy.Api.Slice3;

namespace Academy.UnitTests;

public class ProviderEventIdentityTests
{
    private static ProviderEventEnvelope Valid => new(Guid.NewGuid(), Guid.NewGuid(), "reference", "event", "succeeded", 100.25m, "EGP", new string('A', 64));
    [Fact] public void Identical_facts_hash_equally_without_secret_signature() { var e = Valid; Assert.True(ProviderEventIdentity.ValidShape(e)); Assert.Equal(ProviderEventIdentity.Hash(e), ProviderEventIdentity.Hash(e with { Signature = new string('B', 64), Amount = 100.2500m })); }
    [Fact] public void Hash_binds_every_financial_and_tenant_fact_without_rounding() {
        var e = Valid;
        foreach (var changed in new[] { e with { AcademyId = Guid.NewGuid() }, e with { PaymentRequestId = Guid.NewGuid() }, e with { ProviderReference = "other" }, e with { ProviderEventId = "other" }, e with { Status = "failed" }, e with { Amount = 100.251m }, e with { Currency = "USD" } }) Assert.NotEqual(ProviderEventIdentity.Hash(e), ProviderEventIdentity.Hash(changed));
    }
    [Fact] public void Malformed_or_rounded_money_cannot_reach_signature_verification_or_storage() {
        var e = Valid;
        foreach (var invalid in new[] { e with { AcademyId = Guid.Empty }, e with { PaymentRequestId = Guid.Empty }, e with { Amount = -1 }, e with { Amount = .001m }, e with { Amount = 1000000000m }, e with { Currency = "egp" }, e with { Currency = null! }, e with { Signature = null! }, e with { ProviderEventId = "" }, e with { ProviderReference = new string('x', 101) }, e with { Status = new string('x', 51) }, e with { ProviderReference = "a|b" }, e with { ProviderEventId = "event\n" } }) Assert.False(ProviderEventIdentity.ValidShape(invalid));
    }
}
