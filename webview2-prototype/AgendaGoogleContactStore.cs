using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

/// <summary>Cadastro explícito de contato para um evento existente; nunca cria/escreve no Google.</summary>
internal sealed class AgendaGoogleContactStore(Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
    internal static string ContactId(string company, string calendar, string eventId)
        => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("modux-google-contact-v1\n" + company + "\n" + calendar + "\n" + eventId)).AsSpan(0, 16)).ToString("D");

    internal async Task<(AgendaLocalSnapshot Local, AgendaCalendarLink Link)> Save(string company, string user,
        string calendar, GoogleEventSnapshot original, AgendaLocalSnapshot? expected, string phone,
        string[] serviceIds, string serviceLabel, Func<string, Task<GoogleEventSnapshot>> readEvent)
    {
        var normalized = AgendaWorkflowRules.Phone(phone) ?? throw new InvalidOperationException("Informe um WhatsApp válido com DDD.");
        if (!Guid.TryParse(company, out _) || !Guid.TryParse(user, out _) || string.IsNullOrWhiteSpace(calendar))
            throw new InvalidOperationException("Entre novamente na Agenda para salvar o contato.");
        if (original.OperationalCompanyId is { } identityCompany && identityCompany.ToString("D") != company)
            throw new InvalidOperationException("O evento identifica outra empresa. Confira o vínculo antes de cadastrar o contato.");
        if (original.AllDay || original.SpansMultipleDays || original.Duration > 1440)
            throw new InvalidOperationException("O contato para confirmação exige evento de um dia com horário definido.");
        var current = await readEvent(original.Id);
        if (current.Id != original.Id || current.ETag != original.ETag)
            throw new InvalidOperationException("O evento Google mudou. Atualize antes de salvar o contato.");
        using var links = await authorized(HttpMethod.Get,
            "rest/v1/agenda_calendar_links?select=company_id,appointment_id,calendar_id,event_id,event_etag,sync_status" +
            "&company_id=eq." + Uri.EscapeDataString(company) + "&calendar_id=eq." + Uri.EscapeDataString(calendar) +
            "&event_id=eq." + Uri.EscapeDataString(original.Id) + "&limit=2", null);
        Check(links);
        using var linkJson = JsonDocument.Parse(await links.Content.ReadAsStringAsync());
        if (linkJson.RootElement.GetArrayLength() > 1) throw new InvalidOperationException("Vínculo ambíguo. Atualize a agenda.");
        var link = linkJson.RootElement.GetArrayLength() == 1 ? AgendaCalendarLink.Parse(linkJson.RootElement[0]) : null;
        AgendaLocalSnapshot local;
        if (link is not null)
        {
            if (link.CompanyId != company || link.CalendarId != calendar || link.EventId != original.Id)
                throw new InvalidOperationException("Identidade do vínculo divergente.");
            local = await Read(company, link.AppointmentId) ?? throw new InvalidOperationException("Atendimento vinculado indisponível.");
            if (expected is null && !string.IsNullOrWhiteSpace(local.Phone))
                throw new InvalidOperationException("Este evento já recebeu um contato. Atualize antes de editar o telefone.");
            if (expected is not null && (expected.Id != local.Id || expected.Revision != local.Revision))
                throw new InvalidOperationException("O contato mudou. Atualize antes de salvar novamente.");
        }
        else
        {
            if (expected is not null) throw new InvalidOperationException("O vínculo mudou. Atualize a agenda.");
            if (serviceIds.Length == 0 || serviceIds.Length > 32 ||
                serviceIds.Any(id => !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]{1,80}$")))
                throw new InvalidOperationException("Marque o serviço do evento antes de cadastrar o WhatsApp.");
            var id = original.OperationalAppointmentId?.ToString("D") ?? ContactId(company, calendar, original.Id);
            local = (await Read(company, id))!;
            if (local is null && original.OperationalAppointmentId is not null)
                throw new InvalidOperationException("O evento já identifica um atendimento indisponível. Confira o vínculo; não crie outro cadastro.");
            if (local is null)
            {
                // Sem telefone até o vínculo ser confirmado: falha parcial não libera mensagens.
                using var created = await authorized(HttpMethod.Post, "rest/v1/agenda_appointments", new
                {
                    id, company_id = company, kind = "pf", client = original.Title, phone = "", responsible = "", email = "",
                    address = original.Location, service_ids = serviceIds, primary_service = serviceLabel,
                    date = original.Date.ToString("yyyy-MM-dd"), time = original.Time, duration = original.Duration,
                    status = "agendado", technician = "", notes = original.Description, source = "modux", created_by = user, updated_by = user
                });
                if (created.StatusCode == HttpStatusCode.Conflict)
                    local = await Read(company, id) ?? throw new InvalidOperationException("Cadastro não confirmado. Atualize antes de repetir.");
                else
                {
                    Check(created);
                    using var saved = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                    if (saved.RootElement.ValueKind != JsonValueKind.Array || saved.RootElement.GetArrayLength() != 1)
                        throw new InvalidOperationException("Cadastro não confirmado. Atualize antes de repetir; não crie outro evento.");
                    local = AgendaLocalSnapshot.Parse(saved.RootElement[0]);
                    if (local.Id != id) throw new InvalidOperationException("Identidade do contato não confirmada.");
                }
            }
            if (local.Date != original.Date.ToString("yyyy-MM-dd") || local.Time != original.Time || local.Duration != original.Duration)
                throw new InvalidOperationException("Cadastro parcial com horário diferente. Use Conferir vínculo antes de continuar.");
            link = await new AgendaCalendarLinkStore(authorized).Link(company, local, calendar, current);
        }
        local = await new AgendaAppointmentStore(authorized).UpdatePhone(company, local, normalized);
        return (local, link);
    }

    private async Task<AgendaLocalSnapshot?> Read(string company, string id)
    {
        if (!Guid.TryParse(id, out _)) throw new InvalidOperationException("Identidade do contato inválida.");
        using var response = await authorized(HttpMethod.Get,
            "rest/v1/agenda_appointments?select=" + AgendaWorkflowStore.Fields +
            "&company_id=eq." + Uri.EscapeDataString(company) + "&id=eq." + Uri.EscapeDataString(id) + "&deleted_at=is.null&limit=2", null);
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.GetArrayLength() > 1) throw new InvalidOperationException("Contato ambíguo.");
        var local = json.RootElement.GetArrayLength() == 1 ? AgendaLocalSnapshot.Parse(json.RootElement[0]) : null;
        if (local is not null && local.Id != id) throw new InvalidOperationException("Identidade do contato divergente.");
        return local;
    }

    private static void Check(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Contato não confirmado. Atualize e confira suas permissões (HTTP " + (int)response.StatusCode + ").");
    }
}
