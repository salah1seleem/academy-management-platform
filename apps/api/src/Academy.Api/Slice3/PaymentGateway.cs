using System.Security.Cryptography;
using System.Text;
using Academy.Infrastructure.Subscriptions;
using Microsoft.Extensions.Options;

namespace Academy.Api.Slice3;

public sealed class InternalTestPaymentOptions
{
    public const string SectionName = "Payments:InternalTest";
    public bool Enabled { get; init; }
    public string SigningKey { get; init; } = string.Empty;
}

public sealed record CheckoutSession(string Provider, string ProviderReference, string CheckoutReference, IReadOnlyList<string> AvailableMethods);
public sealed record ProviderEventEnvelope(Guid AcademyId, Guid PaymentRequestId, string ProviderReference, string ProviderEventId, string Status, decimal Amount, string Currency, string Signature);

public interface IPaymentGateway
{
    string Name { get; }
    CheckoutSession CreateCheckout(Guid academyId, Guid paymentId);
    IReadOnlyList<string> GetAvailablePaymentMethods();
    ProviderEventEnvelope CreateTestEvent(PaymentRequest payment, PaymentEventOutcome outcome);
    bool VerifyEvent(ProviderEventEnvelope providerEvent);
}

public sealed class InternalTestPaymentGateway(IOptions<InternalTestPaymentOptions> options) : IPaymentGateway
{
    public const string ProviderName = "InternalTest";
    private readonly InternalTestPaymentOptions settings = options.Value;
    public string Name => ProviderName;

    public CheckoutSession CreateCheckout(Guid academyId, Guid paymentId)
    {
        EnsureEnabled();
        var providerReference = $"ITP-{paymentId:N}";
        var checkout = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        return new(Name, providerReference, checkout, GetAvailablePaymentMethods());
    }

    public IReadOnlyList<string> GetAvailablePaymentMethods() => ["الدفع الإلكتروني التجريبي"];

    public ProviderEventEnvelope CreateTestEvent(PaymentRequest payment, PaymentEventOutcome outcome)
    {
        EnsureEnabled();
        var status = outcome switch { PaymentEventOutcome.Success => "succeeded", PaymentEventOutcome.Failed => "failed", _ => "cancelled" };
        var eventId = $"evt-{Guid.NewGuid():N}";
        var unsigned = new ProviderEventEnvelope(payment.AcademyId, payment.Id, payment.ProviderReference, eventId, status, payment.Amount, payment.Currency, string.Empty);
        return unsigned with { Signature = Sign(unsigned) };
    }

    public bool VerifyEvent(ProviderEventEnvelope providerEvent)
    {
        EnsureEnabled();
        var expected = Sign(providerEvent with { Signature = string.Empty });
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(providerEvent.Signature));
    }

    private string Sign(ProviderEventEnvelope value)
    {
        var payload = string.Join('|', value.AcademyId, value.PaymentRequestId, value.ProviderReference, value.ProviderEventId, value.Status, value.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), value.Currency);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.SigningKey), Encoding.UTF8.GetBytes(payload)));
    }

    private void EnsureEnabled()
    {
        if (!settings.Enabled || settings.SigningKey.Length < 24) throw new InvalidOperationException("Internal Test Payment Gateway is disabled or not securely configured.");
    }

    public static void ValidateEnvironment(IHostEnvironment environment, InternalTestPaymentOptions options)
    {
        if (options.Enabled && !(environment.IsEnvironment("Demo") || environment.IsEnvironment("Testing")))
            throw new InvalidOperationException("Internal Test Payment Gateway may only be enabled in Demo or Testing.");
        if (options.Enabled && options.SigningKey.Length < 24)
            throw new InvalidOperationException("Payments:InternalTest:SigningKey must be at least 24 characters.");
    }
}
