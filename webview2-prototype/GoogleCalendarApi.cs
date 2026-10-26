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
        var payload = JsonSerializer.SerializeToNode(draft.AsGoogleFields())!.AsObject();
        payload["id"] = draft.CreateRequestId;
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
            proposed.AsGoogleFields(), original.ETag, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return GoogleEventSnapshot.Parse(json.RootElement) ??
            throw new InvalidOperationException("Alteração concluída no Google; atualize a agenda para confirmar.");
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
            return new GoogleEventSnapshot(eid, Get(e, "etag"), Get(e, "summary"),
                Get(e, "description"), Get(e, "location"), date, "Dia todo",
                true, 1440, Get(e, "colorId"), Get(e, "recurringEventId"))
            {
                SpansMultipleDays = multiDay
            };
        }
        if (!DateTimeOffset.TryParse(Get(start, "dateTime"), out var begin) ||
            !DateTimeOffset.TryParse(Get(end, "dateTime"), out var finish) ||
            finish <= begin) return null;
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var local = TimeZoneInfo.ConvertTime(begin, saoPaulo);
        var localEnd = TimeZoneInfo.ConvertTime(finish, saoPaulo);
        var minutes = Math.Max(1, (int)Math.Round((finish - begin).TotalMinutes));
        return new GoogleEventSnapshot(eid, Get(e, "etag"), Get(e, "summary"),
            Get(e, "description"), Get(e, "location"), DateOnly.FromDateTime(local.DateTime),
            local.ToString("HH:mm"), false, minutes, Get(e, "colorId"),
            Get(e, "recurringEventId"))
        {
            // Importante: eventos com horário atravessando a meia-noite também
            // precisam da proteção contra edição que alteraria sua duração.
            SpansMultipleDays = local.Date != localEnd.Date
        };
    }
}

/// <summary>Somente os campos operacionais editáveis são enviados; não manipula attendees nem recurrence.</summary>
internal sealed record GoogleEventDraft(string Title, string Description, string Location,
    DateOnly Date, TimeOnly Time, int DurationMinutes, bool AllDay)
{
    // Prefixo "modu" e UUID hexadecimal usam apenas caracteres base32hex [0-9a-v],
    // como exige a API Google Calendar. Não publicar esses IDs como segredo.
    internal string CreateRequestId { get; } = "modu" + Guid.NewGuid().ToString("N");

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
