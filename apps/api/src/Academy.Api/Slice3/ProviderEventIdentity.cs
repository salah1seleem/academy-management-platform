using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Academy.Api.Slice3;

public static class ProviderEventIdentity
{
    public static bool ValidShape(ProviderEventEnvelope e) => e.AcademyId != Guid.Empty && e.PaymentRequestId != Guid.Empty &&
        Text(e.ProviderEventId, 100) && Text(e.ProviderReference, 100) && Text(e.Status, 50) &&
        e.Amount >= 0 && e.Amount <= 999999999m && decimal.Round(e.Amount, 2) == e.Amount &&
        e.Currency?.Length == 3 && e.Currency.All(c => c is >= 'A' and <= 'Z') && e.Signature?.Length == 64;
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Contains('|') && !value.Any(char.IsControl);

    // Delimited JSON, exact decimals and no signature: a retry is the same facts,
    // not merely the same provider event ID. Never hashes a rounded amount.
    public static string Hash(ProviderEventEnvelope e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { e.AcademyId, e.PaymentRequestId, e.ProviderReference, e.ProviderEventId, e.Status, amount = e.Amount.ToString("G29", CultureInfo.InvariantCulture), e.Currency }))));
}
