using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal static class AgendaViewFilter
{
    internal static bool Matches(IReadOnlyList<string> values, AgendaLocalSnapshot? local,
        GoogleEventSnapshot? google, string category, string query, int state, int origin, string? service)
    {
        var hasLocal = local is not null;
        var hasGoogle = google is not null;
        var ical = !hasLocal && !hasGoogle && values[4] == "Google • leitura";
        if (origin switch { 1 => !hasLocal, 2 => !hasGoogle, 3 => !ical,
            4 => !(hasLocal && hasGoogle), _ => false }) return false;
        if (service is not null && !(local?.ServiceIds.Contains(service, StringComparer.Ordinal) ?? false) && !(google?.ServiceTypeIds.Contains(service, StringComparer.Ordinal) ?? false) && category != service)
            return false;
        if (state switch
        {
            1 => !(local?.Status is "agendado" or "revisao" || category == "confirmacao"),
            2 => local?.Status != "agendado", 3 => local?.Status != "confirmado",
            4 => local?.Status != "concluido",
            5 => !(local?.Status is "cancelado" or "cancelar" or "reagendar" || category == "cancelado"),
            6 => !(values[4].Contains("conferir", StringComparison.OrdinalIgnoreCase) ||
                values[4].Contains("pendente", StringComparison.OrdinalIgnoreCase) ||
                local?.Status == "revisao"), _ => false
        }) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;
        var text = string.Join(" ", values) + " " + local?.Technician + " " + local?.Responsible;
        return CultureInfo.GetCultureInfo("pt-BR").CompareInfo.IndexOf(text, query.Trim(),
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }
}

internal static class AgendaExport
{
    internal static string Csv(IEnumerable<IReadOnlyList<string>> rows)
    {
        static string Cell(string value)
        {
            // CSV não pode transformar nome/telefone importado em fórmula no Excel.
            var first = value.TrimStart(' ', '\t', '\r', '\n').FirstOrDefault();
            if (first is '=' or '+' or '-' or '@') value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var result = new StringBuilder();
        result.AppendLine("Data;Hora;Cliente;Serviço;Situação;Telefone;Endereço");
        foreach (var row in rows) result.AppendLine(string.Join(";", row.Select(Cell)));
        return result.ToString();
    }
}

/// <summary>Checkpoints locais cifrados do período carregado; nunca salva tokens ou URL privada iCal.</summary>
internal static class AgendaLocalBackup
{
    internal static void Save(string company, string user, DateOnly first, DateOnly last,
        IReadOnlyList<AgendaLocalSnapshot> appointments, IReadOnlyList<AgendaCalendarLink> links,
        bool partialGoogle)
    {
        if (!Guid.TryParse(company, out _) || !Guid.TryParse(user, out _))
            throw new InvalidOperationException("Identidade indisponível para o backup local.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CentralWhatsApp", "WebView2Prototype", "agenda-backups", company, user);
        Directory.CreateDirectory(folder);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, company, user, saved_at = DateTimeOffset.UtcNow,
            first = first.ToString("yyyy-MM-dd"), last = last.ToString("yyyy-MM-dd"),
            partial_google = partialGoogle, appointments,
            links = links.Where(link => appointments.Any(a => a.Id == link.AppointmentId)).ToArray()
        });
        var encrypted = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
        var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd}-{first:yyyyMMdd}-{last:yyyyMMdd}.dpapi");
        File.WriteAllBytes(file + ".tmp", encrypted);
        File.Move(file + ".tmp", file, true);
        // Um arquivo por dia/período, mantendo no máximo os 60 checkpoints mais recentes.
        foreach (var old in Directory.EnumerateFiles(folder, "*.dpapi").OrderByDescending(Path.GetFileName).Skip(60))
            File.Delete(old);
    }
}
