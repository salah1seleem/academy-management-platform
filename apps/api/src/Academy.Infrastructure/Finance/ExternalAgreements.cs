using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Finance;

public enum ExternalAgreementKind { ExternalProgram=1, PoolRental=2 }

// A counterparty may also be an employee. This document is NOT payroll and is
// never deducted from salary, approved additions, advances or private commission.
public sealed class ExternalAgreement : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid? CounterpartyEmployeeId { get; set; }
    public required string CounterpartyName { get; set; }
    public required string Title { get; set; }
    public ExternalAgreementKind Kind { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public DateOnly DueOn { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public string? Note { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
}

public sealed class ExternalAgreementBooking : TenantEntity
{
    public Guid AgreementId { get; set; }
    public Guid ResourceBookingId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
}

public sealed class ExternalAgreementReceipt : TenantEntity
{
    public Guid AgreementId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public required string ReceiptNumber { get; set; }
    public bool CashPostingRequired { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public ExternalAgreement Agreement { get; set; } = null!;
}

public sealed class CashExternalReceiptPosting : TenantEntity
{
    public Guid ReceiptId { get; set; }
    public Guid SessionId { get; set; }
    public Guid BranchId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid ActorUserId { get; set; }
    public ExternalAgreementReceipt Receipt { get; set; } = null!;
}
