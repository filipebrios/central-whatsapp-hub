namespace CentralWhatsApp.WebView2;

// Never infer the sending account from the currently active or first account.
// Only an explicit, still-existing Criatta binding can be preselected.
internal static class ComposeAccountSelection
{
    public static string? Resolve(string? preferredId, IEnumerable<string> accountIds)
        => !string.IsNullOrWhiteSpace(preferredId) && accountIds.Contains(preferredId, StringComparer.Ordinal)
            ? preferredId : null;
}
