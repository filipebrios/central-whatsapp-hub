using System.Net;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal sealed class AgendaAppointmentStore(
    Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
    internal async Task<AgendaLocalSnapshot> UpdatePhone(string companyId, AgendaLocalSnapshot original, string phone)
    {
        var normalized = AgendaWorkflowRules.Phone(phone) ?? throw new InvalidOperationException("Informe um WhatsApp válido com DDD.");
        if (!Guid.TryParse(companyId, out _) || !Guid.TryParse(original.Id, out _) || original.Revision < 1)
            throw new InvalidOperationException("Identidade/revisão do contato incompleta.");
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_appointments?id=eq." + Uri.EscapeDataString(original.Id) +
            "&company_id=eq." + Uri.EscapeDataString(companyId) + "&revision=eq." + original.Revision + "&deleted_at=is.null",
            new { phone = normalized });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Telefone não confirmado. Atualize antes de repetir.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O contato mudou. Atualize antes de salvar novamente.");
        var updated = AgendaLocalSnapshot.Parse(json.RootElement[0]);
        if (updated.Id != original.Id || updated.Revision <= original.Revision || updated.Phone != normalized)
            throw new InvalidOperationException("Resposta sem confirmação do telefone. Atualize a agenda.");
        return updated;
    }
    internal async Task<AgendaLocalSnapshot> UpdateServices(string companyId, AgendaLocalSnapshot original,
        string[] ids, string label)
    {
        if (!Guid.TryParse(companyId, out _) || !Guid.TryParse(original.Id, out _) || original.Revision < 1 ||
            ids.Length > 32 || ids.Any(id => !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]{1,80}$")))
            throw new InvalidOperationException("Identidade ou serviços do atendimento inválidos.");
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_appointments?id=eq." + Uri.EscapeDataString(original.Id) +
            "&company_id=eq." + Uri.EscapeDataString(companyId) + "&revision=eq." + original.Revision + "&deleted_at=is.null",
            new { service_ids = ids, primary_service = string.IsNullOrWhiteSpace(label) ? "Não identificado" : label });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Os serviços do atendimento não foram confirmados. Confira em Editar atendimento.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O atendimento mudou. Confira os serviços em Editar atendimento; o Google já foi atualizado.");
        var updated = AgendaLocalSnapshot.Parse(json.RootElement[0]);
        if (updated.Id != original.Id || updated.Revision <= original.Revision)
            throw new InvalidOperationException("Confira a revisão dos serviços antes de continuar.");
        return updated;
    }
    internal async Task<bool> HasCalendarLink(string companyId, string appointmentId)
    {
        using var response = await authorized(HttpMethod.Get,
            "rest/v1/agenda_calendar_links?select=event_id&company_id=eq." + Uri.EscapeDataString(companyId) +
            "&appointment_id=eq." + Uri.EscapeDataString(appointmentId) + "&limit=1", null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Não foi possível conferir os vínculos do atendimento. Atualize antes de editar horários.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetArrayLength() > 0;
    }

    internal async Task<AgendaLocalSnapshot> Update(string companyId, AgendaLocalSnapshot original,
        AgendaAppointmentInput proposed, bool linkedSchedule)
    {
        if (!Guid.TryParse(companyId, out _) || !Guid.TryParse(original.Id, out _) || original.Revision < 1)
            throw new InvalidOperationException("Identidade/revisão do atendimento incompleta.");
        var fields = new Dictionary<string, object?>
        {
            ["kind"] = proposed.Kind, ["client"] = proposed.Client,
            ["responsible"] = proposed.Responsible, ["phone"] = proposed.Phone,
            ["email"] = proposed.Email, ["address"] = proposed.Address,
            ["service_ids"] = proposed.ServiceIds, ["primary_service"] = proposed.PrimaryService,
            ["technician"] = proposed.Technician, ["notes"] = proposed.Notes,
            ["status"] = proposed.Status
        };
        if (!linkedSchedule)
        {
            fields["date"] = proposed.Date; fields["time"] = proposed.Time;
            fields["duration"] = proposed.Duration;
        }
        if (proposed.Status == "concluido")
        {
            var completed = proposed.CompletedDate ?? original.CompletedDate ?? DateTime.Today.ToString("yyyy-MM-dd");
            if (!DateOnly.TryParseExact(completed, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day) || day > DateOnly.FromDateTime(DateTime.Today))
                throw new InvalidOperationException("Confira a data real de conclusão do serviço.");
            fields["service_completed_date"] = completed;
        }
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_appointments?id=eq." + Uri.EscapeDataString(original.Id) +
            "&company_id=eq." + Uri.EscapeDataString(companyId) +
            "&revision=eq." + original.Revision + "&deleted_at=is.null", fields);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? "Sem permissão para editar este atendimento."
                : "Atualização não confirmada. Atualize antes de repetir (HTTP " + (int)response.StatusCode + ").");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O atendimento mudou ou já foi salvo. Atualize e confira antes de editar novamente.");
        var updated = AgendaLocalSnapshot.Parse(json.RootElement[0]);
        if (updated.Id != original.Id || updated.Revision <= original.Revision)
            throw new InvalidOperationException("Resposta sem confirmação da revisão. Atualize a agenda.");
        return updated;
    }
}
