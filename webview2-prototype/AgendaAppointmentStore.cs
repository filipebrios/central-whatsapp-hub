using System.Net;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal sealed class AgendaAppointmentStore(
    Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
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
        if (proposed.Status == "concluido" && original.Status != "concluido")
            fields["service_completed_date"] = DateTime.Today.ToString("yyyy-MM-dd");
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
