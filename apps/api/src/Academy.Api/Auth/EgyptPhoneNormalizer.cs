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
        var match = MobileNumber().Match(Separators().Replace(value, string.Empty));
        return match.Success ? $"+20{match.Groups[1].Value}" : null;
    }

    public static string? NormalizeForNewMobile(string? value)
    {
        var normalized = Normalize(value);
        return normalized is not null && normalized[4] is '0' or '1' or '2' or '5' ? normalized : null;
    }

    [GeneratedRegex(@"[\s()\-]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\A(?:(?:\+|00)?200?|0)(1[0-9]{9})\z")]
    private static partial Regex MobileNumber();
}
