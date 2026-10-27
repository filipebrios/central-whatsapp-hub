namespace CentralWhatsApp.WebView2;

/// <summary>
/// Trava temporária do piloto OAuth. Antes da liberação operacional da 1.9,
/// nenhum comando de escrita pode afetar a agenda real da São Bento.
/// A confirmação visual no diálogo NÃO substitui esta trava de camada de API.
/// </summary>
internal static class GoogleCalendarWriteGuard
{
    internal const string TestCalendarName = "MODUX - TESTES";

    internal static bool IsTestCalendar(string? displayName) =>
        string.Equals(displayName?.Trim(), TestCalendarName, StringComparison.OrdinalIgnoreCase);

    // Calendário secundário criado no Google Calendar, não a agenda principal.
    internal static bool IsWritableTestCalendar(string? displayName, string? calendarId, bool authorized) =>
        authorized && IsTestCalendar(displayName) &&
        calendarId is not null &&
        calendarId.EndsWith("@group.calendar.google.com", StringComparison.OrdinalIgnoreCase);

    // Aprovação operacional é vinculada ao ID concreto, nunca ao alias "primary".
    internal static bool IsExplicitOperationalCalendar(string? calendarId, string? approvedCalendarId, bool authorized) =>
        authorized && !string.IsNullOrWhiteSpace(calendarId) && calendarId.Length <= 1024 &&
        !string.Equals(calendarId, "primary", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(calendarId, approvedCalendarId, StringComparison.Ordinal);

    internal static void DemandTestCalendar(string? displayName, string? calendarId, bool authorized)
    {
        if (!IsWritableTestCalendar(displayName, calendarId, authorized))
            throw new InvalidOperationException(
                "Gravação Google bloqueada nesta etapa. Selecione a agenda isolada " +
                "'MODUX - TESTES'. Os agendamentos reais estão protegidos.");
    }
}

internal static class GoogleCalendarIdentity
{
    internal static string? FromPrivateIcalUrl(Uri url)
    {
        if (url.Scheme != "https" || url.Host != "calendar.google.com" || url.Port != 443 ||
            url.UserInfo.Length != 0) return null;
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "calendar" || parts[1] != "ical" ||
            !parts[3].StartsWith("private-", StringComparison.Ordinal) || parts[4] != "basic.ics") return null;
        return Uri.UnescapeDataString(parts[2]);
    }
}
