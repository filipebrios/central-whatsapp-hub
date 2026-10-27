using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CentralWhatsApp.WebView2;

/// <summary>
/// Cliente Google Calendar API com leitura, criação, edição e exclusão condicionada a ETag.
/// Nenhuma operação de escrita ocorre sem chamada explícita da interface do usuário.
/// </summary>
internal sealed class GoogleCalendarApi : IDisposable
{
    private readonly IGoogleCalendarAuthorization auth;
    private readonly HttpClient http;
    internal IReadOnlyDictionary<string, string> EventColors { get; private set; } = new Dictionary<string, string>();
    private string? calendarColor;
    private string? colorCalendarId;
    private DateTimeOffset colorsLoadedAt;
    internal void InvalidateColors() => colorsLoadedAt = default;

    internal async Task LoadColors(CancellationToken ct = default)
    {
        var calendar = SelectedCalendar();
        if (colorCalendarId == calendar && DateTimeOffset.UtcNow - colorsLoadedAt < TimeSpan.FromMinutes(15)) return;
        colorCalendarId = null;
        using var response = await Request(HttpMethod.Get, "colors", ct: ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var colors = new Dictionary<string, string>();
        foreach (var entry in json.RootElement.GetProperty("event").EnumerateObject())
            if (entry.Value.TryGetProperty("background", out var background) && IsHex(background.GetString()))
                colors[entry.Name] = background.GetString()!;
        using var calendarResponse = await Request(HttpMethod.Get,
            "users/me/calendarList/" + Uri.EscapeDataString(calendar), ct: ct);
        using var calendarJson = JsonDocument.Parse(await calendarResponse.Content.ReadAsStringAsync(ct));
        var hex = calendarJson.RootElement.TryGetProperty("backgroundColor", out var value) ? value.GetString() : null;
        if (auth.CalendarId != calendar) return;
        EventColors = colors;
        calendarColor = IsHex(hex) ? hex : null;
        colorCalendarId = calendar;
        colorsLoadedAt = DateTimeOffset.UtcNow;
    }

    internal string? EventColorHex(GoogleEventSnapshot item) => colorCalendarId != auth.CalendarId ? null
        : !string.IsNullOrEmpty(item.ColorId) && EventColors.TryGetValue(item.ColorId, out var hex) ? hex
        : string.IsNullOrEmpty(item.ColorId) || item.ColorId == "0" ? calendarColor : null;

    internal string? ClosestColor(string hex)
    {
        if (!IsHex(hex) || colorCalendarId != auth.CalendarId) return null;
        var wanted = System.Drawing.ColorTranslator.FromHtml(hex);
        return EventColors.OrderBy(entry =>
        {
            var color = System.Drawing.ColorTranslator.FromHtml(entry.Value);
            return Math.Pow(color.R - wanted.R, 2) + Math.Pow(color.G - wanted.G, 2) + Math.Pow(color.B - wanted.B, 2);
        }).Select(entry => entry.Key).FirstOrDefault();
    }

    private static bool IsHex(string? hex) => hex is not null &&
        System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$");

    internal GoogleCalendarApi(GoogleDesktopOAuth auth)
        : this(auth, new HttpClient()) { }

    // O transporte injetável permite testar todo o fluxo HTTP sem credenciais,
    // token real, navegador ou acesso ao calendário do usuário.
    internal GoogleCalendarApi(IGoogleCalendarAuthorization auth, HttpClient http)
    {
        this.auth = auth;
        this.http = http;
        http.BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/");
        http.Timeout = TimeSpan.FromSeconds(35);
    }

    private async Task<HttpResponseMessage> Request(
        HttpMethod method, string url, object? body = null, string? etag = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await auth.AccessTokenAsync(ct));
        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await http.SendAsync(request, ct);
        var status = response.StatusCode;
        if (status == HttpStatusCode.PreconditionFailed)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "O evento foi alterado por outra pessoa. Atualize a agenda antes de salvar.");
        }
        if (status == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "Sua conta não tem permissão de edição neste calendário Google.");
        }
        if (status == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new InvalidOperationException("A autorização Google expirou. Reconecte sua conta.");
        }
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            response.Dispose();
            throw new GoogleEventUnavailableException(status);
        }
        if (status == HttpStatusCode.Conflict)
        {
            response.Dispose();
            throw new GoogleEventIdentityConflictException();
        }
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "O Google Agenda não concluiu a operação (HTTP " + (int)status + ").");
        }
        return response;
    }

    internal async Task<IReadOnlyList<GoogleCalendarChoice>> Calendars(CancellationToken ct = default)
    {
        var all = new List<GoogleCalendarChoice>();
        string? cursor = null;
        do
        {
            using var response = await Request(HttpMethod.Get, "users/me/calendarList?maxResults=250" +
                (cursor is null ? "" : "&pageToken=" + Uri.EscapeDataString(cursor)), ct: ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("items", out var array))
                foreach (var item in array.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? "";
                    var label = item.TryGetProperty("summary", out var s) ? s.GetString() ?? id : id;
                    var role = item.TryGetProperty("accessRole", out var a) ? a.GetString() ?? "" : "";
                    if (role is "writer" or "owner")
                        all.Add(new GoogleCalendarChoice(id, label, role,
                            item.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True));
                }
            cursor = json.RootElement.TryGetProperty("nextPageToken", out var next) ?
                next.GetString() : null;
        } while (cursor is not null && all.Count < 500);
        return all.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ToArray();
    }

    internal async Task<IReadOnlyList<GoogleEventSnapshot>> Events(
        DateOnly first, DateOnly last, CancellationToken ct = default)
    {
        var calendar = auth.CalendarId ??
            throw new InvalidOperationException("Selecione primeiro o calendário Google.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        static string Timestamp(DateOnly day, TimeZoneInfo zone)
        {
            var date = day.ToDateTime(TimeOnly.MinValue);
            return Uri.EscapeDataString(new DateTimeOffset(date, zone.GetUtcOffset(date)).ToString("O"));
        }
        var prefix = $"calendars/{Uri.EscapeDataString(calendar)}/events?" +
            "singleEvents=true&showDeleted=false&orderBy=startTime&maxResults=2500" +
            $"&timeMin={Timestamp(first, zone)}&timeMax={Timestamp(last.AddDays(1), zone)}";
        var list = new List<GoogleEventSnapshot>();
        string? cursor = null;
        do
        {
            using var response = await Request(HttpMethod.Get,
                prefix + (cursor is null ? "" : "&pageToken=" + Uri.EscapeDataString(cursor)), ct: ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("items", out var array))
                foreach (var item in array.EnumerateArray())
                {
                    var parsed = GoogleEventSnapshot.Parse(item);
                    if (parsed is not null) list.Add(parsed);
                }
            cursor = json.RootElement.TryGetProperty("nextPageToken", out var next) ?
                next.GetString() : null;
            if (list.Count > 10000)
                throw new InvalidOperationException("Eventos demais neste período. Reduza a consulta.");
        } while (cursor is not null);
        return list;
    }

    internal async Task<GoogleEventSnapshot> Event(string eventId, CancellationToken ct = default)
    {
        var path = EventPath(eventId);
        using var response = await Request(HttpMethod.Get, path, ct: ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return GoogleEventSnapshot.Parse(json.RootElement) ??
            throw new InvalidOperationException("Não foi possível identificar o evento.");
    }

    internal async Task<GoogleEventSnapshot> Create(GoogleEventDraft draft, CancellationToken ct = default)
    {
        auth.DemandSafeWrite();
        var path = "calendars/" + Uri.EscapeDataString(SelectedCalendar()) +
            "/events?sendUpdates=none";
        // O Google permite IDs explícitos em base32hex. Mantemos o mesmo ID
        // enquanto este rascunho existir, evitando duplicar uma tentativa repetida.
        // Uma dúvida de rede deve ser resolvida por consulta, nunca novo POST cego.
        var payload = draft.GooglePayload();
        payload["id"] = draft.CreateRequestId;
        if (draft.OperationalCompanyId is { } company && draft.OperationalAppointmentId is { } appointment)
        {
            var metadata = payload["extendedProperties"]?["private"]?.AsObject() ?? new JsonObject();
            metadata["modux_company_id"] = company.ToString("D");
            metadata["modux_appointment_id"] = appointment.ToString("D");
            payload["extendedProperties"] = new JsonObject { ["private"] = metadata.DeepClone() };
        }
        try
        {
            using var response = await Request(HttpMethod.Post, path, payload, ct: ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return GoogleEventSnapshot.Parse(json.RootElement) ??
                throw new InvalidOperationException("Evento salvo pelo Google, mas resposta incompleta. Atualize antes de tentar novamente.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Uma falha após o envio não prova que a criação foi rejeitada.
            // Não reenviar POST automaticamente; a consulta à agenda deve decidir.
            throw new InvalidOperationException(
                "A conexão falhou durante a criação. O Google pode ter salvo o evento. " +
                "Atualize o calendário de testes antes de tentar criar novamente.", error);
        }
    }

    /// <summary>Recupera antes de criar. Repetições entre processos usam o ID do atendimento.</summary>
    internal async Task<GoogleEventSnapshot> EnsureOperationalEvent(GoogleEventDraft draft,
        CancellationToken ct = default)
    {
        auth.DemandSafeWrite();
        if (draft.OperationalCompanyId is null || draft.OperationalAppointmentId is null)
            throw new InvalidOperationException("A criação integrada exige a identidade do atendimento.");
        GoogleEventSnapshot CheckIdentity(GoogleEventSnapshot found)
        {
            if (found.Id != draft.CreateRequestId ||
                found.OperationalCompanyId != draft.OperationalCompanyId ||
                found.OperationalAppointmentId != draft.OperationalAppointmentId)
                throw new InvalidOperationException("O identificador Google já existe com outra origem. Nenhum evento foi alterado.");
            return found;
        }
        try { return CheckIdentity(await Event(draft.CreateRequestId, ct)); }
        catch (GoogleEventUnavailableException error) when (error.Status == HttpStatusCode.NotFound) { }
        // 410 indica exclusão anterior: não recriar silenciosamente um evento cancelado.
        try { return CheckIdentity(await Create(draft, ct)); }
        catch (GoogleEventIdentityConflictException)
        {
            // Outro operador pode ter criado entre nosso GET e POST. Apenas ler,
            // verificar a origem e reaproveitar; nunca substituir o registro.
            return CheckIdentity(await Event(draft.CreateRequestId, ct));
        }
    }

    internal async Task<GoogleEventSnapshot> Update(
        GoogleEventSnapshot original, GoogleEventDraft proposed, CancellationToken ct = default)
    {
        auth.DemandSafeWrite();
        if (string.IsNullOrWhiteSpace(original.ETag))
            throw new InvalidOperationException("A versão do evento é desconhecida. Atualize a agenda.");
        if (original.SpansMultipleDays || original.Duration > 1440)
            throw new InvalidOperationException("Eventos que duram mais de um dia ainda devem ser editados diretamente no Google Agenda.");
        var current = await Event(original.Id, ct);
        if (current.ETag != original.ETag)
            throw new InvalidOperationException(
                "O compromisso foi alterado no Google. Atualize antes de editar.");
        var path = EventPath(original.Id) + "?sendUpdates=none";
        // PATCH preserva convidados, recorrência, lembretes e demais propriedades não editadas.
        using var response = await Request(new HttpMethod("PATCH"), path,
            proposed.GooglePayload(current.PrivateProperties), original.ETag, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return GoogleEventSnapshot.Parse(json.RootElement) ??
            throw new InvalidOperationException("Alteração concluída no Google; atualize a agenda para confirmar.");
    }

    internal async Task<GoogleEventSnapshot> Invite(GoogleEventSnapshot original, string email,
        CancellationToken ct = default)
    {
        auth.DemandSafeWrite();
        if (string.IsNullOrWhiteSpace(original.ETag))
            throw new InvalidOperationException("Atualize o evento antes de convidar.");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(email, parsedEmail.Address, StringComparison.OrdinalIgnoreCase) ||
            email.Length > 254 || email.Any(char.IsControl))
            throw new InvalidOperationException("Informe apenas um e-mail válido para o convite.");
        using var currentResponse = await Request(HttpMethod.Get, EventPath(original.Id), ct: ct);
        using var currentJson = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync(ct));
        var source = currentJson.RootElement;
        var current = GoogleEventSnapshot.Parse(source) ??
            throw new InvalidOperationException("Evento indisponível para convidar.");
        if (current.ETag != original.ETag)
            throw new InvalidOperationException("O compromisso mudou no Google. Atualize antes de convidar.");
        if (source.TryGetProperty("attendeesOmitted", out var omitted) && omitted.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("A lista de convidados está incompleta. Use o Google Agenda para este convite.");
        var guests = new JsonArray();
        if (source.TryGetProperty("attendees", out var attendees))
        {
            if (attendees.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Lista de convidados inválida. Confira no Google Agenda.");
            foreach (var attendee in attendees.EnumerateArray())
            {
                if (attendee.TryGetProperty("email", out var existing) &&
                    string.Equals(existing.GetString(), email, StringComparison.OrdinalIgnoreCase))
                    return current; // Idempotência: convidado existente não provoca outro envio.
                var guest = new JsonObject();
                foreach (var field in new[] { "email", "displayName", "optional", "resource",
                    "responseStatus", "comment", "additionalGuests" })
                    if (attendee.TryGetProperty(field, out var value))
                        guest[field] = JsonNode.Parse(value.GetRawText());
                guests.Add(guest);
            }
        }
        guests.Add(new JsonObject { ["email"] = parsedEmail.Address });
        try
        {
            // Arrays replace the existing list: preserve every current guest and response.
            // sendUpdates=all is reserved for this separately confirmed operation.
            using var response = await Request(new HttpMethod("PATCH"),
                EventPath(original.Id) + "?sendUpdates=all",
                new JsonObject { ["attendees"] = guests }, original.ETag, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return GoogleEventSnapshot.Parse(json.RootElement) ??
                throw new InvalidOperationException("Convite processado pelo Google; atualize o evento para conferir.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("A conexão falhou e o Google pode ter processado o convite. " +
                "Confira os convidados no Google antes de repetir; nenhum reenvio automático foi feito.", error);
        }
    }

    internal async Task Delete(GoogleEventSnapshot original, CancellationToken ct = default)
    {
        auth.DemandSafeWrite();
        if (string.IsNullOrWhiteSpace(original.ETag))
            throw new InvalidOperationException("Atualize o compromisso antes de cancelar.");
        var current = await Event(original.Id, ct);
        if (current.ETag != original.ETag)
            throw new InvalidOperationException("O compromisso mudou no Google. Atualize antes de excluir.");
        using var response = await Request(HttpMethod.Delete,
            EventPath(original.Id) + "?sendUpdates=none", etag: original.ETag, ct: ct);
    }

    private string SelectedCalendar() => auth.CalendarId ??
        throw new InvalidOperationException("Selecione um calendário na sua conta Google.");
    private string EventPath(string id) =>
        "calendars/" + Uri.EscapeDataString(SelectedCalendar()) +
        "/events/" + Uri.EscapeDataString(id);

    public void Dispose() => http.Dispose();
}

internal sealed record GoogleEventTypeChoice(string Id, string Label, string? ColorId)
{
    public override string ToString() => Label;
}

internal sealed record GoogleCalendarChoice(string Id, string Name, string Role, bool IsPrimary)
{
    public override string ToString() => Name + (IsPrimary ? " (principal)" : "");
}

/// <summary>Instantâneo do Google; ETag serve como trava de alterações concorrentes.</summary>
internal sealed record GoogleEventSnapshot(
    string Id, string ETag, string Title, string Description,
    string Location, DateOnly Date, string Time, bool AllDay,
    int Duration, string? ColorId, string? RecurringEventId)
{
    internal bool SpansMultipleDays { get; init; }
    internal Guid? OperationalCompanyId { get; init; }
    internal Guid? OperationalAppointmentId { get; init; }
    internal IReadOnlyDictionary<string, string> PrivateProperties { get; init; } = new Dictionary<string, string>();
    internal string[] ServiceTypeIds => PrivateProperties.TryGetValue("modux_service_ids", out var ids)
        ? ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray()
        : PrivateProperties.TryGetValue("modux_service_id", out var id) && !string.IsNullOrEmpty(id) ? [id] : [];
    internal string? ServiceTypeId => ServiceTypeIds.FirstOrDefault();
    internal DateOnly? ExclusiveEndDate { get; init; }

    private static GoogleEventSnapshot WithIdentity(GoogleEventSnapshot snapshot, JsonElement source)
    {
        Guid? Read(string key)
        {
            if (source.TryGetProperty("extendedProperties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object &&
                properties.TryGetProperty("private", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
                metadata.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out var id)) return id;
            return null;
        }
        var privateProperties = new Dictionary<string, string>();
        if (source.TryGetProperty("extendedProperties", out var props) && props.ValueKind == JsonValueKind.Object &&
            props.TryGetProperty("private", out var entries) && entries.ValueKind == JsonValueKind.Object)
            foreach (var entry in entries.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.String) privateProperties[entry.Name] = entry.Value.GetString()!;
        return snapshot with { OperationalCompanyId = Read("modux_company_id"),
            OperationalAppointmentId = Read("modux_appointment_id"), PrivateProperties = privateProperties };
    }

    internal static GoogleEventSnapshot? Parse(JsonElement e)
    {
        if (!e.TryGetProperty("id", out var id) ||
            !e.TryGetProperty("start", out var start) ||
            !e.TryGetProperty("end", out var end)) return null;
        var eid = id.GetString();
        if (string.IsNullOrEmpty(eid)) return null;
        static string Get(JsonElement element, string name) =>
            element.TryGetProperty(name, out var field) &&
            field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
        var allDay = start.TryGetProperty("date", out var d);
        if (allDay)
        {
            if (!DateOnly.TryParse(d.GetString(), out var date) ||
                !end.TryGetProperty("date", out var endDay) ||
                !DateOnly.TryParse(endDay.GetString(), out var endsOn) ||
                endsOn <= date) return null; // Fim de dia inteiro é exclusivo.
            var multiDay = endsOn > date.AddDays(1);
            return WithIdentity(new GoogleEventSnapshot(eid, Get(e, "etag"), Get(e, "summary"),
                Get(e, "description"), Get(e, "location"), date, "Dia todo",
                true, 1440, Get(e, "colorId"), Get(e, "recurringEventId"))
            {
                SpansMultipleDays = multiDay, ExclusiveEndDate = endsOn
            }, e);
        }
        if (!DateTimeOffset.TryParse(Get(start, "dateTime"), out var begin) ||
            !DateTimeOffset.TryParse(Get(end, "dateTime"), out var finish) ||
            finish <= begin) return null;
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var local = TimeZoneInfo.ConvertTime(begin, saoPaulo);
        var localEnd = TimeZoneInfo.ConvertTime(finish, saoPaulo);
        var minutes = Math.Max(1, (int)Math.Round((finish - begin).TotalMinutes));
        return WithIdentity(new GoogleEventSnapshot(eid, Get(e, "etag"), Get(e, "summary"),
            Get(e, "description"), Get(e, "location"), DateOnly.FromDateTime(local.DateTime),
            local.ToString("HH:mm"), false, minutes, Get(e, "colorId"),
            Get(e, "recurringEventId"))
        {
            // Importante: eventos com horário atravessando a meia-noite também
            // precisam da proteção contra edição que alteraria sua duração.
            SpansMultipleDays = local.Date != localEnd.Date
        }, e);
    }
}

/// <summary>Somente os campos operacionais editáveis são enviados; não manipula attendees nem recurrence.</summary>
internal sealed record GoogleEventDraft(string Title, string Description, string Location,
    DateOnly Date, TimeOnly Time, int DurationMinutes, bool AllDay)
{
    // Prefixo "modu" e UUID hexadecimal usam apenas caracteres base32hex [0-9a-v],
    // como exige a API Google Calendar. Não publicar esses IDs como segredo.
    internal string CreateRequestId { get; private init; } = "modu" + Guid.NewGuid().ToString("N");
    internal Guid? OperationalCompanyId { get; private init; }
    internal Guid? OperationalAppointmentId { get; private init; }
    internal string? ServiceTypeId { get; init; }
    internal string[]? ServiceTypeIds { get; init; }
    internal string? SelectedColorId { get; init; }

    internal JsonObject GooglePayload(IReadOnlyDictionary<string, string>? existing = null)
    {
        var payload = JsonSerializer.SerializeToNode(AsGoogleFields())!.AsObject();
        var types = ServiceTypeIds ?? (ServiceTypeId is not null ? [ServiceTypeId] : null);
        if (types is not null)
        {
            if (types.Length > 32 || types.Any(id => !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]{1,80}$")))
                throw new InvalidOperationException("Tipo de serviço inválido.");
            var metadata = existing is null ? new JsonObject() : JsonSerializer.SerializeToNode(existing)!.AsObject();
            metadata["modux_service_ids"] = string.Join(",", types.Distinct(StringComparer.Ordinal));
            metadata["modux_service_id"] = types.FirstOrDefault() ?? "";
            payload["extendedProperties"] = new JsonObject { ["private"] = metadata };
            if (!string.IsNullOrEmpty(SelectedColorId)) payload["colorId"] = SelectedColorId;
        }
        return payload;
    }

    internal GoogleEventDraft ForAppointment(Guid companyId, Guid appointmentId)
    {
        if (companyId == Guid.Empty || appointmentId == Guid.Empty)
            throw new InvalidOperationException("Identidade do atendimento inválida.");
        return this with { CreateRequestId = "modu" + appointmentId.ToString("N"),
            OperationalCompanyId = companyId, OperationalAppointmentId = appointmentId };
    }

    internal object AsGoogleFields()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 500 ||
            Description.Length > 10000 || Location.Length > 1500 ||
            DurationMinutes < 15 || DurationMinutes > 1440)
            throw new InvalidOperationException("Revise título, descrição, endereço e duração.");
        if (AllDay)
            return new
            {
                summary = Title.Trim(), description = Description, location = Location,
                start = new { date = Date.ToString("yyyy-MM-dd") },
                end = new { date = Date.AddDays(1).ToString("yyyy-MM-dd") }
            };
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var first = Date.ToDateTime(Time);
        var last = first.AddMinutes(DurationMinutes);
        if (zone.IsInvalidTime(first) || zone.IsInvalidTime(last))
            throw new InvalidOperationException("Horário inválido no fuso horário de São Paulo.");
        var start = new DateTimeOffset(first, zone.GetUtcOffset(first));
        var finish = new DateTimeOffset(last, zone.GetUtcOffset(last));
        return new
        {
            summary = Title.Trim(), description = Description, location = Location,
            start = new { dateTime = start.ToString("yyyy-MM-ddTHH:mm:sszzz"), timeZone = "America/Sao_Paulo" },
            end = new { dateTime = finish.ToString("yyyy-MM-ddTHH:mm:sszzz"), timeZone = "America/Sao_Paulo" }
        };
    }
}

/// <summary>Autorização mínima do cliente Calendar, substituível apenas em testes internos.</summary>
internal interface IGoogleCalendarAuthorization
{
    string? CalendarId { get; }
    Task<string> AccessTokenAsync(CancellationToken ct);
    void DemandSafeWrite();
}

internal sealed class GoogleEventUnavailableException(HttpStatusCode status)
    : InvalidOperationException(status == HttpStatusCode.Gone
        ? "O evento foi excluído no Google. Confira o atendimento antes de reagendar."
        : "Evento não encontrado no Google Agenda.")
{
    internal HttpStatusCode Status { get; } = status;
}

internal sealed class GoogleEventIdentityConflictException()
    : InvalidOperationException("O identificador do evento já existe no Google. Atualize antes de tentar novamente.") { }
