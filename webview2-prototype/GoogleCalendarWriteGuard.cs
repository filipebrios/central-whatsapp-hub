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

    internal static void DemandTestCalendar(string? displayName, string? calendarId, bool authorized)
    {
        // Um calendário secundário criado no Google Calendar tem ID @group.calendar.google.com.
        // Exigimos esse identificador para impedir que renomear a agenda principal
        // como "MODUX - TESTES" libere gravações nela.
        if (!authorized || !IsTestCalendar(displayName) ||
            calendarId is null ||
            !calendarId.EndsWith("@group.calendar.google.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Gravação Google bloqueada nesta etapa. Selecione a agenda isolada " +
                "'MODUX - TESTES'. Os agendamentos reais da São Bento estão protegidos.");
    }
}
