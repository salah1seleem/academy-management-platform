using System.Text.RegularExpressions;

namespace Academy.Api.Auth;

public static partial class EgyptPhoneNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = string.Concat(value.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠')
            : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c));
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or '-' or '(' or ')') && !char.IsWhiteSpace(c))) return null;
        var digits = NonDigits().Replace(value, string.Empty);
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.StartsWith('0')) digits = $"20{digits[1..]}";
        if (digits.Length != 12 || !digits.StartsWith("201", StringComparison.Ordinal)) return null;
        return $"+{digits}";
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
}
