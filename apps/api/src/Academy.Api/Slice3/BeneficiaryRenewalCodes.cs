using System.Security.Cryptography;
using System.Text;

namespace Academy.Api.Slice3;

public static class BeneficiaryRenewalCodes
{
    public static string Generate() => $"RNW-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))));
    public static string Hint(string value)
    {
        var normalized = Normalize(value);
        return normalized.Length <= 6 ? normalized : normalized[^6..];
    }
}
