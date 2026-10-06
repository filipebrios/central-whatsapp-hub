using System.Text.RegularExpressions;

namespace CentralWhatsApp.WebView2;

// An external link can only prepare a draft, never send or execute arbitrary URLs.
internal sealed record ComposeRequest(string Phone, string Text)
{
    public const int MaxUriLength = 8000;
    public const int MaxTextLength = 1800;

    public static ComposeRequest? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxUriLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != "modux" || uri.Host != "compose" ||
            (uri.AbsolutePath != "" && uri.AbsolutePath != "/") ||
            uri.UserInfo != "" || !uri.IsDefaultPort || uri.Fragment != "") return null;
        try
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2 || !fields.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                    return null;
            }
            if (fields.Count != 3 || fields.GetValueOrDefault("source") != "criatta" ||
                !fields.TryGetValue("phone", out var phone) || !Regex.IsMatch(phone, @"^55[0-9]{10,11}$") ||
                !fields.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text) ||
                text.Length > MaxTextLength || text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                return null;
            return new ComposeRequest(phone, text);
        }
        catch (UriFormatException) { return null; }
    }

    public string WhatsAppUrl => "https://web.whatsapp.com/send?phone=" + Phone + "&text=" + Uri.EscapeDataString(Text);
}
