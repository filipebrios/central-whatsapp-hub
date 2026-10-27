using System.Text.RegularExpressions;

namespace CentralWhatsApp.WebView2;

internal static class ContactPhone
{
    internal static string? Normalize(string value)
    {
        var digits = string.Concat(value.Where(c => c is >= '0' and <= '9'));
        if (digits.Length is 10 or 11) digits = "55" + digits;
        return Regex.IsMatch(digits, "^55[0-9]{10,11}$") ? digits : null;
    }
}
