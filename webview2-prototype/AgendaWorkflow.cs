using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CentralWhatsApp.WebView2;

internal sealed record AgendaWorkItem(AgendaLocalSnapshot Appointment, string TemplateKey, string Label)
{
    internal string Occurrence => TemplateKey is "pos_venda" or "avaliacao_google"
        ? Appointment.CompletedDate ?? "" : Appointment.Date + "T" + Appointment.Time;
    public override string ToString() => $"{Label} • {Appointment.Client} • {Appointment.Date} {Appointment.Time}";
}

internal sealed record AgendaMessageJob(string Id, string AppointmentId, string TemplateKey,
    string Occurrence, string State)
{
    internal static AgendaMessageJob Parse(JsonElement row) => new(
        row.GetProperty("id").GetString()!, row.GetProperty("appointment_id").GetString()!,
        row.GetProperty("template_key").GetString()!, row.GetProperty("occurrence_key").GetString()!,
        row.GetProperty("state").GetString()!);
}

internal static class AgendaWorkflowRules
{
    internal static string? Phone(string value)
    {
        var digits = string.Concat(value.Where(c => c is >= '0' and <= '9'));
        if (digits.Length is 10 or 11) digits = "55" + digits;
        return Regex.IsMatch(digits, "^55[0-9]{10,11}$") ? digits : null;
    }

    internal static IReadOnlyList<AgendaWorkItem> Plan(IEnumerable<AgendaLocalSnapshot> appointments, DateOnly today, bool includeRecorded = false)
    {
        var result = new List<AgendaWorkItem>();
        foreach (var a in appointments)
        {
            if (a.Status is "cancelado" or "cancelar") continue;
            if (Phone(a.Phone) is null)
            { result.Add(new(a, "", "Completar WhatsApp")); continue; }
            if (a.Status == "concluido")
            {
                if (DateOnly.TryParseExact(a.CompletedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var completed) && completed <= today.AddDays(-7))
                {
                    if (a.FeedbackScore is null) result.Add(new(a, "pos_venda", "Pós-venda • NPS"));
                    else if (a.FeedbackScore >= 8) result.Add(new(a, "avaliacao_google", "Convite • avaliação Google"));
                    else if (includeRecorded) result.Add(new(a, "", "Retorno registrado • nota " + a.FeedbackScore));
                }
                continue;
            }
            if (!DateOnly.TryParseExact(a.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || !TimeOnly.TryParse(a.Time, out _))
            { result.Add(new(a, "", "Completar data e horário")); continue; }
            if (a.Status == "reagendar")
            { result.Add(new(a, "reagendamento", "Combinar reagendamento")); continue; }
            if (date < today)
            { result.Add(new(a, "", "Concluir ou revisar atendimento")); continue; }
            if (a.Status is "agendado" or "revisao" && date >= today && date <= today.AddDays(1))
                result.Add(new(a, "confirmacao", "A confirmar"));
            if (date == today) result.Add(new(a, "lembrete_d0", "Lembrete • hoje"));
            if (date == today.AddDays(1)) result.Add(new(a, "lembrete_d1", "Lembrete • amanhã"));
        }
        return result;
    }

    internal static ComposeRequest WithResponseLink(ComposeRequest message, string token)
    {
        if (!Regex.IsMatch(token, "^[0-9a-f]{64}$"))
            throw new InvalidOperationException("Link de resposta inválido. Atualize a rotina.");
        var text = message.Text + "\n\nResponda por este link exclusivo do atendimento:\n" +
            "https://www.sbento.com.br/modux-resposta.html#token=" + token;
        if (text.Length > ComposeRequest.MaxTextLength)
            throw new InvalidOperationException("O modelo ficou longo demais com o link. Reduza o texto antes de preparar.");
        return message with { Text = text };
    }

    internal static ComposeRequest Message(AgendaWorkItem work, string template)
    {
        var phone = Phone(work.Appointment.Phone) ?? throw new InvalidOperationException("Complete o WhatsApp do cliente.");
        var a = work.Appointment;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cliente"] = a.Client, ["servico"] = a.Service, ["hora"] = a.Time,
            ["endereco"] = a.Address, ["responsavel"] = a.Responsible,
            ["nota"] = a.FeedbackScore?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["data"] = DateOnly.TryParse(a.Date, out var day) ? day.ToString("dd/MM/yyyy") : "a combinar"
        };
        var text = Regex.Replace(template, @"\{([a-z_]+)\}", match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value :
                throw new InvalidOperationException("Modelo contém campo desconhecido: " + match.Groups[1].Value));
        if (string.IsNullOrWhiteSpace(text) || text.Length > ComposeRequest.MaxTextLength ||
            text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new InvalidOperationException("Revise o modelo: texto vazio, inválido ou longo demais para preparar.");
        return new(phone, text, "saobento");
    }
}

internal sealed class AgendaWorkflowStore(
    Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
    internal const string Fields = "id,revision,client,phone,primary_service,service_ids,date,time,status,address,duration,notes,responsible,email,kind,technician,service_completed_date,feedback_score,feedback_note";
    internal IReadOnlyList<AgendaLocalSnapshot> Appointments { get; private set; } = [];
    internal IReadOnlyList<AgendaMessageJob> Jobs { get; private set; } = [];
    internal IReadOnlyDictionary<string, string> Templates { get; private set; } = new Dictionary<string, string>();

    internal async Task Load(string companyId, DateOnly today)
    {
        var last = today.AddDays(1);
        var query = $"or=(status.eq.reagendar,status.eq.revisao,and(date.gte.{today.AddDays(-7):yyyy-MM-dd},date.lte.{last:yyyy-MM-dd})," +
            $"and(status.eq.concluido,service_completed_date.gte.{today.AddDays(-90):yyyy-MM-dd},service_completed_date.lte.{today.AddDays(-7):yyyy-MM-dd}))";
        var appointments = new List<AgendaLocalSnapshot>();
        for (var offset = 0; offset < 5000; offset += 250)
        {
            using var response = await authorized(HttpMethod.Get,
                "rest/v1/agenda_appointments?select=" + Fields + "&company_id=eq." + Uri.EscapeDataString(companyId) +
                "&deleted_at=is.null&" + query + "&order=id.asc&limit=250&offset=" + offset, null);
            Check(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            appointments.AddRange(json.RootElement.EnumerateArray().Select(AgendaLocalSnapshot.Parse));
            if (json.RootElement.GetArrayLength() < 250) break;
            if (offset == 4750) throw new InvalidOperationException("Pendências demais. Reduza o período pelo cadastro.");
        }
        using var templates = await authorized(HttpMethod.Get,
            "rest/v1/agenda_message_templates?select=key,body&company_id=eq." + Uri.EscapeDataString(companyId), null);
        Check(templates);
        using var templateJson = JsonDocument.Parse(await templates.Content.ReadAsStringAsync());
        var dictionary = templateJson.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!,
            x => x.GetProperty("body").GetString()!, StringComparer.Ordinal);
        var jobs = new List<AgendaMessageJob>();
        // Buscar somente registros pertinentes aos atendimentos carregados, sem truncar o histórico global.
        foreach (var group in appointments.Select(x => x.Id).Chunk(50))
        {
            if (group.Any(id => !Guid.TryParse(id, out _))) throw new InvalidOperationException("Identificação inválida na fila.");
            using var response = await authorized(HttpMethod.Get,
                "rest/v1/agenda_message_queue?select=id,appointment_id,template_key,occurrence_key,state" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) + "&appointment_id=in.(" + string.Join(",", group) + ")&limit=1000", null);
            Check(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.GetArrayLength() >= 1000)
                throw new InvalidOperationException("Histórico grande demais para conferir sem revisão.");
            jobs.AddRange(json.RootElement.EnumerateArray().Select(AgendaMessageJob.Parse));
        }
        Appointments = appointments; Templates = dictionary; Jobs = jobs;
    }

    internal async Task<AgendaMessageJob> Prepare(string companyId, AgendaWorkItem work)
    {
        using var response = await authorized(HttpMethod.Post, "rest/v1/rpc/agenda_prepare_manual_message", new
        {
            p_company_id = companyId, p_appointment_id = work.Appointment.Id,
            p_template_key = work.TemplateKey, p_expected_revision = work.Appointment.Revision
        });
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement;
        if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == 1) row = row[0];
        var job = AgendaMessageJob.Parse(row);
        if (job.AppointmentId != work.Appointment.Id || job.TemplateKey != work.TemplateKey || job.Occurrence != work.Occurrence)
            throw new InvalidOperationException("O atendimento mudou. Atualize a fila antes de preparar.");
        return job;
    }

    internal async Task<string> IssueResponseToken(string companyId, AgendaWorkItem work)
    {
        if (work.TemplateKey is not ("confirmacao" or "pos_venda"))
            throw new InvalidOperationException("Esta rotina não usa link de resposta.");
        using var response = await authorized(HttpMethod.Post, "rest/v1/rpc/agenda_issue_response_token", new
        {
            p_company_id = companyId, p_appointment_id = work.Appointment.Id,
            p_kind = work.TemplateKey, p_expected_revision = work.Appointment.Revision
        });
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement;
        if (!row.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String ||
            !Regex.IsMatch(token.GetString()!, "^[0-9a-f]{64}$") ||
            !row.TryGetProperty("expires_at", out var expiry) ||
            !DateTimeOffset.TryParse(expiry.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires) ||
            expires <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Link não confirmado. Atualize a rotina antes de preparar novamente.");
        return token.GetString()!;
    }

    internal async Task Mark(string companyId, AgendaMessageJob job, string state)
    {
        if (state is not ("enviada_manual" or "cancelada" or "erro")) throw new InvalidOperationException("Situação inválida.");
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_message_queue?id=eq." + Uri.EscapeDataString(job.Id) +
            "&company_id=eq." + Uri.EscapeDataString(companyId) + "&state=eq." + Uri.EscapeDataString(job.State),
            new { state });
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("A mensagem mudou. Atualize para conferir o registro.");
    }

    internal async Task SaveFeedback(string companyId, AgendaLocalSnapshot original, int score, string note)
    {
        if (score is < 0 or > 10 || note.Length > 4000) throw new InvalidOperationException("Confira a nota e o comentário.");
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_appointments?id=eq." + Uri.EscapeDataString(original.Id) +
            "&company_id=eq." + Uri.EscapeDataString(companyId) + "&revision=eq." + original.Revision +
            "&status=eq.concluido&deleted_at=is.null", new { feedback_score = score, feedback_note = note });
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O atendimento mudou. Atualize antes de registrar a nota.");
    }

    internal async Task SaveReviewTemplate(string companyId, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.UserInfo.Length != 0 || !url.IsDefaultPort || value.Length > 1200 ||
            !(url.Host == "google.com" || url.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
              url.Host is "g.page" or "maps.app.goo.gl"))
            throw new InvalidOperationException("Use o link HTTPS oficial de avaliações Google da São Bento.");
        using var existing = await authorized(HttpMethod.Get,
            "rest/v1/agenda_message_templates?select=key,updated_at&company_id=eq." + Uri.EscapeDataString(companyId) +
            "&key=eq.avaliacao_google", null);
        Check(existing);
        using var previous = JsonDocument.Parse(await existing.Content.ReadAsStringAsync());
        var body = "Olá, {cliente}! Obrigado pelo retorno! Se desejar, deixe também sua avaliação da São Bento no Google: " + url.AbsoluteUri;
        var path = "rest/v1/agenda_message_templates";
        object data;
        var method = HttpMethod.Post;
        if (previous.RootElement.GetArrayLength() > 0)
        {
            method = HttpMethod.Patch;
            path += "?company_id=eq." + Uri.EscapeDataString(companyId) + "&key=eq.avaliacao_google&updated_at=eq." +
                Uri.EscapeDataString(previous.RootElement[0].GetProperty("updated_at").GetString()!);
            data = new { body, updated_at = DateTimeOffset.UtcNow.ToString("O") };
        }
        else data = new { company_id = companyId, key = "avaliacao_google", body };
        using var response = await authorized(method, path, data);
        Check(response);
        using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (saved.RootElement.ValueKind != JsonValueKind.Array || saved.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O modelo mudou. Atualize antes de salvar novamente.");
    }

    private static void Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? "Sem permissão para esta rotina."
            : "Rotina não confirmada. Atualize e confira data, situação e revisão do atendimento (HTTP " + (int)response.StatusCode + ").");
    }
}
