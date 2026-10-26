using System.Net;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal sealed record AgendaLocalSnapshot(string Id, int Revision, string Client,
    string Service, string[] ServiceIds, string Date, string Time, string Status,
    string Phone, string Address, int Duration = 60, string Notes = "",
    string Responsible = "", string Email = "", string Kind = "pf", string Technician = "")
{
    internal static AgendaLocalSnapshot Parse(JsonElement row)
    {
        static string Read(JsonElement e, string key) =>
            e.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ToString() : "";
        var time = Read(row, "time");
        return new(Read(row, "id"), row.GetProperty("revision").GetInt32(),
            Read(row, "client"), Read(row, "primary_service"),
            row.TryGetProperty("service_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!).ToArray() : [],
            Read(row, "date"), time.Length > 5 ? time[..5] : time,
            Read(row, "status"), Read(row, "phone"), Read(row, "address"),
            row.TryGetProperty("duration", out var duration) ? duration.GetInt32() : 60,
            Read(row, "notes"), Read(row, "responsible"), Read(row, "email"),
            Read(row, "kind"), Read(row, "technician"));
    }
    internal GoogleEventDraft AsGoogleDraft(string companyId)
    {
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day) ||
            !TimeOnly.TryParse(Time, System.Globalization.CultureInfo.InvariantCulture, out var clock))
            throw new InvalidOperationException("Informe data e horário válidos no atendimento.");
        var description = $"Serviços: {Service}\nResponsável: {Responsible}\nWhatsApp: {Phone}\nEquipe: {Technician}\n{Notes}";
        return new GoogleEventDraft(Service + " - " + Client, description, Address,
            day, clock, Duration, false).ForAppointment(Guid.Parse(companyId), Guid.Parse(Id));
    }
    public override string ToString() => $"{Date} {Time} • {Client} • {Service}";
}

internal sealed record AgendaCalendarLink(string CompanyId, string AppointmentId,
    string CalendarId, string EventId, string ETag, string SyncStatus = "vinculado")
{
    internal static AgendaCalendarLink Parse(JsonElement row) => new(
        row.GetProperty("company_id").GetString()!, row.GetProperty("appointment_id").GetString()!,
        row.GetProperty("calendar_id").GetString()!, row.GetProperty("event_id").GetString()!,
        row.TryGetProperty("event_etag", out var etag) && etag.ValueKind == JsonValueKind.String
            ? etag.GetString()! : "",
        row.TryGetProperty("sync_status", out var status) && status.ValueKind == JsonValueKind.String
            ? status.GetString()! : "vinculado");
}

internal sealed record AgendaUnifiedEvent(GoogleEventSnapshot? Google,
    AgendaLocalSnapshot? Local, AgendaCalendarLink? Link);

/// <summary>Associa somente identidades explícitas; título, telefone e horário nunca são chaves.</summary>
internal static class AgendaCalendarProjection
{
    internal static IReadOnlyList<AgendaUnifiedEvent> Merge(string companyId, string calendarId,
        IReadOnlyList<AgendaLocalSnapshot> locals, IReadOnlyList<GoogleEventSnapshot> google,
        IReadOnlyList<AgendaCalendarLink> links)
    {
        var byId = locals.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var scoped = links.Where(x => x.CompanyId == companyId && x.CalendarId == calendarId).ToArray();
        if (scoped.GroupBy(x => x.EventId, StringComparer.Ordinal).Any(g => g.Count() > 1) ||
            scoped.GroupBy(x => x.AppointmentId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Vínculos inconsistentes. Atualize antes de continuar.");
        var byEvent = scoped.ToDictionary(x => x.EventId, StringComparer.Ordinal);
        var byAppointment = scoped.ToDictionary(x => x.AppointmentId, StringComparer.Ordinal);
        var attached = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AgendaUnifiedEvent>();
        foreach (var item in google)
        {
            byEvent.TryGetValue(item.Id, out var link);
            AgendaLocalSnapshot? local = null;
            if (link is not null && byId.TryGetValue(link.AppointmentId, out local))
                attached.Add(local.Id);
            result.Add(new(item, local, link));
        }
        foreach (var item in locals)
            if (!attached.Contains(item.Id))
            {
                byAppointment.TryGetValue(item.Id, out var link);
                result.Add(new(null, item, link));
            }
        return result;
    }
}

/// <summary>Usa exclusivamente a sessão do operador e RLS; não recebe chave de serviço.</summary>
internal sealed class AgendaCalendarLinkStore(
    Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
    internal async Task<IReadOnlyList<AgendaCalendarLink>> List(string companyId, string calendarId)
    {
        var result = new List<AgendaCalendarLink>();
        for (var offset = 0; offset < 10000; offset += 250)
        {
            using var response = await authorized(HttpMethod.Get,
                "rest/v1/agenda_calendar_links?select=company_id,appointment_id,calendar_id,event_id,event_etag,sync_status" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) +
                "&calendar_id=eq." + Uri.EscapeDataString(calendarId) +
                "&order=event_id.asc&limit=250&offset=" + offset, null);
            await Check(response);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var row in document.RootElement.EnumerateArray()) result.Add(AgendaCalendarLink.Parse(row));
            if (document.RootElement.GetArrayLength() < 250) return result;
        }
        throw new InvalidOperationException("Vínculos demais para conferir com segurança. Contate o responsável.");
    }

    internal async Task<AgendaCalendarLink> Link(string companyId, AgendaLocalSnapshot local,
        string calendarId, GoogleEventSnapshot google)
    {
        if (!Guid.TryParse(companyId, out _) || !Guid.TryParse(local.Id, out _) ||
            local.Revision < 1 || string.IsNullOrWhiteSpace(calendarId) ||
            string.IsNullOrWhiteSpace(google.Id) || string.IsNullOrWhiteSpace(google.ETag))
            throw new InvalidOperationException("Identidade incompleta. Atualize antes de vincular.");
        using var response = await authorized(HttpMethod.Post, "rest/v1/rpc/agenda_link_google_event", new
        {
            p_company_id = companyId, p_appointment_id = local.Id,
            p_calendar_id = calendarId, p_event_id = google.Id,
            p_event_etag = google.ETag, p_expected_revision = local.Revision
        });
        await Check(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = document.RootElement;
        if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == 1) row = row[0];
        if (row.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Vínculo salvo, mas resposta incompleta. Atualize antes de repetir.");
        var result = AgendaCalendarLink.Parse(row);
        if (result.CompanyId != companyId || result.AppointmentId != local.Id ||
            result.CalendarId != calendarId || result.EventId != google.Id)
            throw new InvalidOperationException("A identidade retornada não corresponde ao vínculo. Atualize para conferir.");
        return result;
    }

    internal async Task Reconcile(string companyId, AgendaCalendarLink link, AgendaLocalSnapshot local,
        GoogleEventSnapshot? google, bool cancelled = false)
    {
        if (link.CompanyId != companyId || link.AppointmentId != local.Id || local.Revision < 1 ||
            string.IsNullOrWhiteSpace(link.ETag) || (!cancelled && google is null) ||
            (google is not null && google.Id != link.EventId))
            throw new InvalidOperationException("Identidade incompleta para reconciliar. Atualize a agenda.");
        if (!cancelled && (google!.SpansMultipleDays || google.Duration > 1440))
            throw new InvalidOperationException("Eventos de vários dias exigem revisão no Google antes de vincular horários.");
        using var response = await authorized(HttpMethod.Post, "rest/v1/rpc/agenda_reconcile_google_event", new
        {
            p_company_id = companyId, p_calendar_id = link.CalendarId, p_event_id = link.EventId,
            p_expected_etag = link.ETag, p_observed_etag = google?.ETag ?? link.ETag,
            p_expected_revision = local.Revision,
            p_date = cancelled ? null : google!.Date.ToString("yyyy-MM-dd"),
            p_time = cancelled ? null : google!.AllDay ? "00:00:00" :
                TimeOnly.Parse(google.Time, System.Globalization.CultureInfo.InvariantCulture).ToString("HH:mm:ss"),
            p_duration = cancelled ? (int?)null : google!.Duration, p_cancelled = cancelled
        });
        await Check(response);
    }

    private static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (error.RootElement.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String)
                code = field.GetString();
        }
        catch (JsonException) { }
        if (code is "40001" or "23505")
            throw new InvalidOperationException("O atendimento mudou ou já possui outro vínculo. Atualize antes de tentar novamente.");
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Conflict => "O atendimento mudou ou já possui outro vínculo. Atualize antes de tentar novamente.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Sem permissão para vincular este atendimento.",
            _ => "Não foi possível confirmar o vínculo (HTTP " + (int)response.StatusCode + "). Atualize antes de repetir."
        });
    }
}
