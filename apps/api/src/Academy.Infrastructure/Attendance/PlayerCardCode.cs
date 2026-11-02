using System.Security.Cryptography;
using System.Text;

namespace Academy.Infrastructure.Attendance;
public static class PlayerCardCode
{
    public const string Prefix = "academity:player:v1:";
    public static string Create() => Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool IsWellFormed(string? value) => value is not null && value.Length == Prefix.Length + 64 && value.StartsWith(Prefix, StringComparison.Ordinal) && value[Prefix.Length..].All(Uri.IsHexDigit);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
