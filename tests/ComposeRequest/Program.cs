using CentralWhatsApp.WebView2;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var text = "Olá, João!\nPedido #12 & link: https://example.test/?a=1&b=2";
var link = "modux://compose?source=criatta&phone=5535992576972&text=" + Uri.EscapeDataString(text);
var request = ComposeRequest.Parse(link);
Check(request?.Phone == "5535992576972" && request.Text == text, "Unicode and reserved character round trip");
Check(request!.WhatsAppUrl.StartsWith("https://web.whatsapp.com/send?phone=5535992576972&text="), "Fixed WhatsApp destination");
foreach (var invalid in new[] {
    link.Replace("modux:", "https:"), link.Replace("compose?", "execute?"),
    link + "&text=duplicate", link + "&extra=field", link + "#fragment",
    link.Replace("source=criatta", "source=other"), link.Replace("5535992576972", "123"),
    "modux://compose?source=criatta&phone=5535992576972&text=%00",
    "modux://compose?source=criatta&phone=5535992576972&text=" + new string('x', 1801),
    new string('x', 8001), "modux://user@compose?source=criatta&phone=5535992576972&text=Hello",
    "modux://compose?source=criatta&phone=5535992576972&text=" })
    Check(ComposeRequest.Parse(invalid) is null, "Rejected malformed or unsafe input: " + invalid[..Math.Min(80, invalid.Length)]);
Console.WriteLine("MODUX protocol checks passed.");

var ids = new[] { "company", "criatta" };
Check(ComposeAccountSelection.Resolve(null, ids) is null, "First use requires explicit account selection");
Check(ComposeAccountSelection.Resolve("", ids) is null, "Empty preference does not select first account");
Check(ComposeAccountSelection.Resolve("removed", ids) is null, "Removed account requires a new selection");
Check(ComposeAccountSelection.Resolve("criatta", ids) == "criatta", "Explicit Criatta binding is preserved");
Check(ComposeAccountSelection.Resolve("criatta", ids.Reverse()) == "criatta", "Account reordering does not change binding");
Console.WriteLine("MODUX account selection checks passed.");
