using CentralWhatsApp.WebView2;
using System.Text.Json;

var source = """
{
 "id":"event-987", "etag":"\"v42\"",
 "summary":"Desinsetização - Cliente XYZ",
 "description":"Unidade Central", "location":"Itajubá",
 "start":{"dateTime":"2026-10-22T08:00:00-03:00","timeZone":"America/Sao_Paulo"},
 "end":{"dateTime":"2026-10-22T09:30:00-03:00","timeZone":"America/Sao_Paulo"},
 "colorId":"9"
}
""";
using var doc = JsonDocument.Parse(source);
var item = GoogleEventSnapshot.Parse(doc.RootElement);
if (item is null || item.Title != "Desinsetização - Cliente XYZ"
    || item.Duration != 90 || item.Date.ToString("yyyy-MM-dd") != "2026-10-22"
    || item.Time != "08:00" || item.ETag != "\"v42\"")
    throw new Exception("Falha ao ler compromisso do Google.");
var change = new GoogleEventDraft(
    item.Title, item.Description, item.Location, item.Date,
    new TimeOnly(10, 15), 75, false);
// IDs gerados para criação de eventos: base32hex, únicos e estáveis no mesmo rascunho.
var generatedIds = new HashSet<string>(StringComparer.Ordinal);
for (var i = 0; i < 1000; i++)
{
    var generated = new GoogleEventDraft(
        "Teste", "", "", new DateOnly(2026, 10, 23), new TimeOnly(10, 0), 30, false);
    var id = generated.CreateRequestId;
    if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-v0-9]{5,1024}$") ||
        !generatedIds.Add(id) || generated.CreateRequestId != id)
        throw new Exception("ID Google invalido, duplicado ou instavel.");
}
Console.WriteLine("PASS: 1000 IDs Google base32hex únicos e estáveis por rascunho.");
var patch = JsonSerializer.Serialize(change.AsGoogleFields());
using var parsed = JsonDocument.Parse(patch);
if (parsed.RootElement.GetProperty("start").GetProperty("dateTime").GetString()
        != "2026-10-22T10:15:00-03:00"
    || parsed.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
        != "2026-10-22T11:30:00-03:00"
    || parsed.RootElement.TryGetProperty("attendees", out _)
    || parsed.RootElement.TryGetProperty("recurrence", out _))
    throw new Exception("PATCH inválido ou campos indevidos.");

using var allDayDoc = JsonDocument.Parse("""
{"id":"day-1","etag":"\"x\"","start":{"date":"2026-10-22"},"end":{"date":"2026-10-23"}}
""");
var day = GoogleEventSnapshot.Parse(allDayDoc.RootElement);
if (day is null || !day.AllDay || day.Date.Day != 22)
    throw new Exception("Evento de dia inteiro inválido.");
var allDayPatch = JsonSerializer.Serialize(new GoogleEventDraft(
    "Feriado", "", "", day.Date, TimeOnly.MinValue, 1440, true).AsGoogleFields());
using var dayJSON = JsonDocument.Parse(allDayPatch);
if (dayJSON.RootElement.GetProperty("end").GetProperty("date").GetString() != "2026-10-23")
    throw new Exception("Fim exclusivo de dia inteiro inválido.");
Console.WriteLine("PASS: leitura, edição parcial, fuso -03, duração, ETag e dia inteiro.");


// Casos reais de risco identificados na auditoria: recorrência, virada da meia-noite,
// dia inteiro com mais de um dia, duração invertida e título inválido.
static GoogleEventSnapshot? FromJson(string json)
{
    using var document = JsonDocument.Parse(json);
    return GoogleEventSnapshot.Parse(document.RootElement);
}
var recurring = FromJson("""
{"id":"series-123_20261023T130000Z","etag":"\"v7\"",
 "summary":"Desratização recorrente",
 "recurringEventId":"series-123",
 "start":{"dateTime":"2026-10-23T10:00:00-03:00"},
 "end":{"dateTime":"2026-10-23T11:00:00-03:00"}}
""");
if (recurring is null || recurring.RecurringEventId != "series-123" ||
    recurring.SpansMultipleDays || recurring.Duration != 60)
    throw new Exception("Instância recorrente identificada incorretamente.");

var overMidnight = FromJson("""
{"id":"night","etag":"\"v1\"",
 "start":{"dateTime":"2026-10-23T23:30:00-03:00"},
 "end":{"dateTime":"2026-10-24T00:45:00-03:00"}}
""");
if (overMidnight is null || overMidnight.Duration != 75 ||
    !overMidnight.SpansMultipleDays || overMidnight.Time != "23:30")
    throw new Exception("Evento passando da meia-noite deve ser protegido.");

var manyDays = FromJson("""
{"id":"holiday","etag":"\"v2\"",
 "start":{"date":"2026-10-23"},"end":{"date":"2026-10-27"}}
""");
if (manyDays is null || !manyDays.AllDay || !manyDays.SpansMultipleDays)
    throw new Exception("Evento de vários dias inteiros deve ser protegido.");

var invalidAllDay = FromJson("""
{"id":"invalid-day","etag":"\"v1\"",
 "start":{"date":"2026-10-23"},"end":{"date":"2026-10-23"}}
""");
if (invalidAllDay is not null)
    throw new Exception("Fim exclusivo igual ao início não é válido.");

var backwards = FromJson("""
{"id":"invalid-time","etag":"\"v1\"",
 "start":{"dateTime":"2026-10-23T11:00:00-03:00"},
 "end":{"dateTime":"2026-10-23T10:30:00-03:00"}}
""");
if (backwards is not null)
    throw new Exception("Evento com término anterior ao início não é válido.");

var missingEnd = FromJson("""
{"id":"incomplete","start":{"dateTime":"2026-10-23T11:00:00-03:00"}}
""");
if (missingEnd is not null)
    throw new Exception("Evento sem horário de término não é válido.");

var invalidDraftRejected = false;
try
{
    _ = new GoogleEventDraft(" ", "", "", new DateOnly(2026, 10, 23),
        new TimeOnly(8, 0), 60, false).AsGoogleFields();
}
catch (InvalidOperationException) { invalidDraftRejected = true; }
if (!invalidDraftRejected)
    throw new Exception("Título em branco não pode produzir payload de escrita.");

var attendees = JsonSerializer.Serialize(change.AsGoogleFields());
if (attendees.Contains("attendees", StringComparison.OrdinalIgnoreCase) ||
    attendees.Contains("recurrence", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Campos sensíveis não podem ser substituídos em PATCH.");

Console.WriteLine("PASS: recorrência, meia-noite, evento multidiário, datas inválidas e edição restrita.");


// Etapa 2: um teste NÃO pode gravar na agenda operacional, mesmo que o usuário
// confirme o diálogo ou uma chamada futura acesse diretamente a API.
static void ExpectBlocked(string? name, string? id, bool authorized)
{
    try
    {
        GoogleCalendarWriteGuard.DemandTestCalendar(name, id, authorized);
    }
    catch (InvalidOperationException) { return; }
    throw new Exception("Falha de segurança: gravação liberada na agenda errada: " + name);
}
ExpectBlocked("Agendamento São Bento", "agendamento.saobento@gmail.com", true);
ExpectBlocked("MODUX - TESTES extra", "example@group.calendar.google.com", true);
ExpectBlocked("MODUX - TESTES", "", true);
ExpectBlocked("MODUX - TESTES", "example@group.calendar.google.com", false);
ExpectBlocked(null, "example@group.calendar.google.com", true);
ExpectBlocked("MODUX - TESTES", "agendamento.saobento@gmail.com", true);
ExpectBlocked("MODUX - TESTES", "primary", true);
GoogleCalendarWriteGuard.DemandTestCalendar("MODUX - TESTES", "example@group.calendar.google.com", true);
GoogleCalendarWriteGuard.DemandTestCalendar("  MODUX - TESTES  ", "example@group.calendar.google.com", true);
if (GoogleCalendarWriteGuard.IsTestCalendar("Agendamento São Bento"))
    throw new Exception("O calendário operacional não pode ser reconhecido como testes.");
Console.WriteLine("PASS: trava da agenda operacional, calendarId e OAuth testados.");

// Testes do cliente HTTP completo. O handler abaixo nunca acessa a rede.
var auth = new FakeCalendarAuthorization();
var draft = new GoogleEventDraft("Teste controlado", "Observação", "Itajubá",
    new DateOnly(2026, 10, 23), new TimeOnly(8, 0), 60, false);
using (var transport = new CalendarTestHandler((method, uri, headers, body) =>
{
    if (method != "POST" || !uri.AbsolutePath.EndsWith("/events") ||
        uri.Query != "?sendUpdates=none" || headers != "Bearer test-only-token")
        throw new Exception("Criação enviada com método, destino ou autenticação incorretos.");
    using var payload = JsonDocument.Parse(body!);
    if (payload.RootElement.GetProperty("id").GetString() != draft.CreateRequestId ||
        payload.RootElement.TryGetProperty("attendees", out _))
        throw new Exception("Criação perdeu a identidade estável ou adicionou convidados.");
    return CalendarTestHandler.Json(source);
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    var created = await api.Create(draft);
    if (created.Id != "event-987" || transport.Calls != 1)
        throw new Exception("Criação deve fazer exatamente uma requisição.");
}

// Uma agenda operacional deve ser barrada antes de qualquer chamada HTTP.
auth.AllowWrite = false;
using (var transport = new CalendarTestHandler((_, _, _, _) =>
    throw new Exception("Uma escrita bloqueada tentou acessar a rede.")))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.Create(draft), "calendário de testes");
    await ExpectFailure(() => api.Update(item!, draft), "calendário de testes");
    await ExpectFailure(() => api.Delete(item!), "calendário de testes");
    if (transport.Calls != 0) throw new Exception("Guarda permitiu requisição HTTP.");
}
auth.AllowWrite = true;

// GET prévio + PATCH com If-Match, sem substituir recorrência/convidados.
using (var transport = new CalendarTestHandler((method, _, _, body) =>
{
    if (method == "GET") return CalendarTestHandler.Json(source);
    if (method != "PATCH" || body is null) throw new Exception("Método de edição incorreto.");
    return CalendarTestHandler.Json(source);
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    _ = await api.Update(item!, draft);
    if (transport.Calls != 2 || transport.LastETag != item!.ETag)
        throw new Exception("PATCH sem verificação prévia ou sem If-Match.");
}

// Mudança por outro operador entre leitura e edição: nenhum PATCH deve sair.
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    if (method != "GET") throw new Exception("Conflito não bloqueou a escrita.");
    return CalendarTestHandler.Json(source.Replace("v42", "v43"));
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.Update(item!, draft), "alterado no Google");
    await ExpectFailure(() => api.Delete(item!), "mudou no Google");
    if (transport.Calls != 2) throw new Exception("Conflito gerou escrita inesperada.");
}

// Conflito depois do GET ainda deve ser barrado pelo If-Match no servidor.
using (var transport = new CalendarTestHandler((method, _, _, _) => method == "GET"
    ? CalendarTestHandler.Json(source)
    : new HttpResponseMessage(System.Net.HttpStatusCode.PreconditionFailed)))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.Update(item!, draft), "alterado por outra pessoa");
    if (transport.Calls != 2 || transport.LastETag != item!.ETag)
        throw new Exception("Conflito tardio sem ETag.");
}

using (var transport = new CalendarTestHandler((method, _, _, _) => method == "GET"
    ? CalendarTestHandler.Json(source)
    : new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await api.Delete(item!);
    if (transport.Calls != 2 || transport.LastETag != item!.ETag)
        throw new Exception("Exclusão sem comparação da versão.");
}

foreach (var failure in new[] {
    (System.Net.HttpStatusCode.Unauthorized, "expirou"),
    (System.Net.HttpStatusCode.Forbidden, "permissão"),
    (System.Net.HttpStatusCode.InternalServerError, "HTTP 500") })
{
    using var transport = new CalendarTestHandler((_, _, _, _) => new HttpResponseMessage(failure.Item1));
    using var api = new GoogleCalendarApi(auth, new HttpClient(transport));
    await ExpectFailure(() => api.Events(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23)), failure.Item2);
    if (transport.Calls != 1) throw new Exception("Erro HTTP gerou repetição automática.");
}

foreach (var timeout in new[] { false, true })
{
    using var transport = new CalendarTestHandler((_, _, _, _) =>
    {
        if (timeout) throw new TaskCanceledException("timeout simulado");
        throw new HttpRequestException("falha de rede simulada");
    });
    using var api = new GoogleCalendarApi(auth, new HttpClient(transport));
    await ExpectFailure(() => api.Create(draft), "pode ter salvo");
    if (transport.Calls != 1) throw new Exception("Criação ambígua foi repetida automaticamente.");
}
Console.WriteLine("PASS: cliente HTTP isolado, CRUD, bloqueio operacional, ETag, erros e ausência de reenvio cego.");

// Reconciliação usa IDs explícitos; nomes iguais nunca autorizam fusão de registros.
var company = "11111111-1111-1111-1111-111111111111";
var localRecord = new AgendaLocalSnapshot("22222222-2222-2222-2222-222222222222", 7,
    item!.Title, "Desinsetização", ["desinsetizacao"], "2026-09-22", "08:00", "agendado", "contato fictício", "Endereço local");
var calendarId = "test@group.calendar.google.com";
var association = new AgendaCalendarLink(company, localRecord.Id, calendarId, item.Id, item.ETag);
var separate = AgendaCalendarProjection.Merge(company, calendarId, [localRecord], [item], []);
if (separate.Count != 2) throw new Exception("Nome igual não pode fundir registros sem vínculo.");
var merged = AgendaCalendarProjection.Merge(company, calendarId, [localRecord], [item], [association]);
if (merged.Count != 1 || merged[0].Google != item || merged[0].Local != localRecord ||
    merged[0].Google!.Date.Month != 10 || merged[0].Local!.Phone != "contato fictício")
    throw new Exception("Vínculo perdeu o evento ou os dados operacionais.");
foreach (var wrong in new[] { association with { CompanyId = "outra empresa" },
    association with { CalendarId = "outra agenda" }, association with { EventId = "outro evento" } })
    if (AgendaCalendarProjection.Merge(company, calendarId, [localRecord], [item], [wrong]).Count != 2)
        throw new Exception("Vínculo fora do escopo ocultou atendimento.");
var missingLocal = AgendaCalendarProjection.Merge(company, calendarId, [], [item], [association]);
if (missingLocal.Count != 1 || missingLocal[0].Google is null || missingLocal[0].Link is null)
    throw new Exception("Vínculo sem cadastro precisa permanecer visível para revisão.");
var missingGoogle = AgendaCalendarProjection.Merge(company, calendarId, [localRecord], [], [association]);
if (missingGoogle.Count != 1 || missingGoogle[0].Local is null || missingGoogle[0].Link is null)
    throw new Exception("Falha de leitura Google não pode apagar atendimento da tela.");
var inconsistentBlocked = false;
try { _ = AgendaCalendarProjection.Merge(company, calendarId, [localRecord], [item], [association, association]); }
catch (InvalidOperationException) { inconsistentBlocked = true; }
if (!inconsistentBlocked) throw new Exception("Vínculo duplicado precisa ser sinalizado.");

var linkCalls = 0;
var store = new AgendaCalendarLinkStore((method, path, data) =>
{
    linkCalls++;
    if (method != HttpMethod.Post || path != "rest/v1/rpc/agenda_link_google_event")
        throw new Exception("Vínculo deve usar RPC com identidade e revisão.");
    using var payload = JsonDocument.Parse(JsonSerializer.Serialize(data));
    if (payload.RootElement.GetProperty("p_expected_revision").GetInt32() != 7 ||
        payload.RootElement.GetProperty("p_event_id").GetString() != item.Id ||
        payload.RootElement.GetProperty("p_appointment_id").GetString() != localRecord.Id)
        throw new Exception("Vínculo perdeu identidade ou revisão.");
    return Task.FromResult(CalendarTestHandler.Json(JsonSerializer.Serialize(new
    {
        company_id = company, appointment_id = localRecord.Id, calendar_id = calendarId,
        event_id = item.Id, event_etag = item.ETag, sync_status = "vinculado"
    })));
});
await store.Link(company, localRecord, calendarId, item);
if (linkCalls != 1) throw new Exception("Vínculo enviou múltiplos pedidos.");
await ExpectFailure(() => store.Link(company, localRecord with { Id = "inválido" }, calendarId, item), "Identidade incompleta");
if (linkCalls != 1) throw new Exception("Identidade inválida chegou à API.");
var conflictStore = new AgendaCalendarLinkStore((_, _, _) => Task.FromResult(
    new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
    { Content = new StringContent("{\"code\":\"40001\"}") }));
await ExpectFailure(() => conflictStore.Link(company, localRecord, calendarId, item), "atendimento mudou");
var listCalls = 0;
var pagedStore = new AgendaCalendarLinkStore((method, path, _) =>
{
    if (method != HttpMethod.Get || !path.Contains("company_id=eq." + company) ||
        !path.Contains("calendar_id=eq." + Uri.EscapeDataString(calendarId)))
        throw new Exception("Leitura de vínculos sem filtro de empresa/calendário.");
    listCalls++;
    if (!path.Contains("offset=" + (listCalls == 1 ? "0" : "250")))
        throw new Exception("Paginação de vínculos incorreta.");
    var rows = listCalls == 1 ? Enumerable.Range(0, 250).Select(i => new
    {
        company_id = company, appointment_id = localRecord.Id, calendar_id = calendarId,
        event_id = "event" + i, event_etag = "v1"
    }).ToArray() : [];
    return Task.FromResult(CalendarTestHandler.Json(JsonSerializer.Serialize(rows)));
});
if ((await pagedStore.List(company, calendarId)).Count != 250 || listCalls != 2)
    throw new Exception("Paginação truncou vínculos.");
Console.WriteLine("PASS: vínculos por ID, isolamento, metadados preservados, ausência de eventos, RPC/revisão e paginação.");

// Identidade integrada estável entre novos rascunhos, reinícios e operadores.
var operationDraft = draft.ForAppointment(Guid.Parse(company), Guid.Parse(localRecord.Id));
var anotherDraft = new GoogleEventDraft("Outro texto", "", "", draft.Date, draft.Time, 60, false)
    .ForAppointment(Guid.Parse(company), Guid.Parse(localRecord.Id));
if (operationDraft.CreateRequestId != anotherDraft.CreateRequestId)
    throw new Exception("Novo rascunho perdeu a identidade do atendimento.");
var operationalJson = System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject();
operationalJson["id"] = operationDraft.CreateRequestId;
operationalJson["extendedProperties"] = JsonSerializer.SerializeToNode(
    new { @private = new { modux_company_id = company, modux_appointment_id = localRecord.Id } });
var operationResponse = operationalJson.ToJsonString();
using (var transport = new CalendarTestHandler((method, _, _, body) =>
{
    if (method == "GET") return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    if (method != "POST") throw new Exception("Criação integrada usou método inesperado.");
    using var request = JsonDocument.Parse(body!);
    var identity = request.RootElement.GetProperty("extendedProperties").GetProperty("private");
    if (identity.GetProperty("modux_appointment_id").GetString() != localRecord.Id ||
        request.RootElement.GetProperty("id").GetString() != operationDraft.CreateRequestId)
        throw new Exception("Criação integrada não registrou identidade privada.");
    return CalendarTestHandler.Json(operationResponse);
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    _ = await api.EnsureOperationalEvent(operationDraft);
    if (transport.Calls != 2) throw new Exception("Deve consultar antes de criar evento integrado.");
}
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    if (method != "GET") throw new Exception("Evento existente foi criado novamente ou alterado.");
    return CalendarTestHandler.Json(operationResponse);
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    _ = await api.EnsureOperationalEvent(anotherDraft);
    if (transport.Calls != 1) throw new Exception("Recuperação deveria apenas consultar a identidade.");
}
var raceStep = 0;
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    raceStep++;
    return raceStep switch
    {
        1 when method == "GET" => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
        2 when method == "POST" => new HttpResponseMessage(System.Net.HttpStatusCode.Conflict),
        3 when method == "GET" => CalendarTestHandler.Json(operationResponse),
        _ => throw new Exception("Corrida de criação gerou reenvio inesperado.")
    };
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    _ = await api.EnsureOperationalEvent(operationDraft);
    if (transport.Calls != 3) throw new Exception("Conflito de identidade não foi reconciliado por leitura.");
}
foreach (var forbiddenResponse in new[] {
    operationResponse.Replace(company, "33333333-3333-3333-3333-333333333333"), source })
{
    using var transport = new CalendarTestHandler((method, _, _, _) =>
    {
        if (method != "GET") throw new Exception("Identidade de outra origem provocou escrita.");
        return CalendarTestHandler.Json(forbiddenResponse);
    });
    using var api = new GoogleCalendarApi(auth, new HttpClient(transport));
    await ExpectFailure(() => api.EnsureOperationalEvent(operationDraft), "outra origem");
}
using (var transport = new CalendarTestHandler((_, _, _, _) => new HttpResponseMessage(System.Net.HttpStatusCode.Gone)))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.EnsureOperationalEvent(operationDraft), "excluído");
    if (transport.Calls != 1) throw new Exception("Evento excluído foi recriado silenciosamente.");
}
var ambiguousPosts = 0;
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    if (method == "GET") return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    ambiguousPosts++;
    throw new HttpRequestException("falha depois de enviar");
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.EnsureOperationalEvent(operationDraft), "pode ter salvo");
    if (ambiguousPosts != 1 || transport.Calls != 2)
        throw new Exception("Criação integrada ambígua foi repetida.");
}
Console.WriteLine("PASS: criação integrada recuperável, identidade por atendimento, conflito concorrente, origem e exclusão protegidas.");

var reconciledPosts = 0;
var reconcileStore = new AgendaCalendarLinkStore((method, path, payload) =>
{
    reconciledPosts++;
    if (method != HttpMethod.Post || path != "rest/v1/rpc/agenda_reconcile_google_event")
        throw new Exception("Reconciliação deve usar RPC transacional.");
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
    if (json.RootElement.GetProperty("p_expected_revision").GetInt32() != localRecord.Revision ||
        json.RootElement.GetProperty("p_expected_etag").GetString() != association.ETag)
        throw new Exception("Reconciliação sem revisão/ETag esperado.");
    if (!json.RootElement.GetProperty("p_cancelled").GetBoolean())
    {
        if (json.RootElement.GetProperty("p_date").GetString() != "2026-10-22" ||
            json.RootElement.GetProperty("p_time").GetString() != "08:00:00" ||
            json.RootElement.GetProperty("p_duration").GetInt32() != 90)
            throw new Exception("Reconciliação perdeu horário/duração do Google.");
    }
    else if (json.RootElement.GetProperty("p_date").ValueKind != JsonValueKind.Null)
        throw new Exception("Cancelamento não deve inventar horário novo.");
    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
});
await reconcileStore.Reconcile(company, association, localRecord, item);
await reconcileStore.Reconcile(company, association, localRecord, null, cancelled: true);
await ExpectFailure(() => reconcileStore.Reconcile("outra empresa", association, localRecord, item), "Identidade incompleta");
await ExpectFailure(() => reconcileStore.Reconcile(company, association, localRecord, item with { SpansMultipleDays = true }), "vários dias");
if (reconciledPosts != 2) throw new Exception("Reconciliação inválida chegou à API.");
Console.WriteLine("PASS: reconciliação/cancelamento RPC, revisão/ETag e guarda multidiária.");

var proposal = new AgendaAppointmentInput("pj", "Cliente atualizado", "Responsável", "(35) 99999-9999",
    "", "Endereço novo", ["desratizacao"], "Desratização", "Equipe A", "Nota preservada",
    "2026-12-01", "15:00:00", 120, "concluido");
foreach (var linkedSchedule in new[] { false, true })
{
    var storeCalls = 0;
    var operationalStore = new AgendaAppointmentStore((method, path, data) =>
    {
        storeCalls++;
        if (method != HttpMethod.Patch || !path.Contains("company_id=eq." + company) ||
            !path.Contains("id=eq." + localRecord.Id) || !path.Contains("revision=eq.7") ||
            !path.Contains("deleted_at=is.null")) throw new Exception("Edição local sem identidade/revisão.");
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(data));
        var hasSchedule = request.RootElement.TryGetProperty("date", out _);
        if (hasSchedule == linkedSchedule || request.RootElement.TryGetProperty("company_id", out _) ||
            request.RootElement.TryGetProperty("id", out _) ||
            !request.RootElement.TryGetProperty("service_completed_date", out _))
            throw new Exception("Edição local alterou identidade/horário vinculado ou perdeu conclusão.");
        return Task.FromResult(CalendarTestHandler.Json(JsonSerializer.Serialize(new[] { new
        {
            id = localRecord.Id, revision = 8, client = proposal.Client, primary_service = proposal.PrimaryService,
            service_ids = proposal.ServiceIds, date = localRecord.Date, time = localRecord.Time,
            status = proposal.Status, phone = proposal.Phone, address = proposal.Address
        } })));
    });
    var changedLocal = await operationalStore.Update(company, localRecord, proposal, linkedSchedule);
    if (changedLocal.Revision != 8 || storeCalls != 1) throw new Exception("Edição local não confirmou a nova revisão.");
}
var staleLocalStore = new AgendaAppointmentStore((_, _, _) => Task.FromResult(CalendarTestHandler.Json("[]")));
await ExpectFailure(() => staleLocalStore.Update(company, localRecord, proposal, false), "atendimento mudou");
Console.WriteLine("PASS: edição operacional com CAS, horário vinculado protegido e data de conclusão.");

var catalogJson = JsonSerializer.Serialize(new[]
{
    new { id = "desinsetizacao", label = "Desinsetização", color = "#000000", sort_order = 10, is_active = true, updated_at = "2026-10-26T12:00:00+00:00" },
    new { id = "cancelado", label = "Cancelado / reagendar", color = "#D50000", sort_order = 110, is_active = false, updated_at = "2026-10-26T12:00:00+00:00" }
});
var catalogWrites = 0;
var sharedCatalog = new AgendaServiceCatalog((method, path, data) =>
{
    if (!path.Contains("company_id=eq." + company)) throw new Exception("Catálogo sem escopo de empresa.");
    if (method == HttpMethod.Patch)
    {
        catalogWrites++;
        if (!path.Contains("updated_at=eq.")) throw new Exception("Cor compartilhada sem trava de revisão.");
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(data));
        if (payload.RootElement.GetProperty("label").GetString() != "Nome atualizado" ||
            payload.RootElement.GetProperty("color").GetString() != "#FFFFFF")
            throw new Exception("Configuração compartilhada perdeu rótulo/cor.");
        using var fixture = JsonDocument.Parse(catalogJson);
        return Task.FromResult(CalendarTestHandler.Json("[" + fixture.RootElement[0].GetRawText() + "]"));
    }
    return Task.FromResult(CalendarTestHandler.Json(catalogJson));
});
await sharedCatalog.Load(company);
var sharedColor = sharedCatalog.Resolve(localRecord);
if (sharedColor.Color.ToArgb() != System.Drawing.Color.Black.ToArgb() ||
    sharedColor.TextColor.ToArgb() != System.Drawing.Color.White.ToArgb())
    throw new Exception("Cor compartilhada ou contraste de texto inválido.");
if (sharedCatalog.Resolve(localRecord with { Status = "cancelado" }).Id != "cancelado")
    throw new Exception("Cor de serviço apagou a situação cancelada.");
await sharedCatalog.SaveAppearance(company, sharedCatalog.Items[0], "Nome atualizado", "#FFFFFF");
if (catalogWrites != 1) throw new Exception("Configuração de cor gerou múltiplas escritas.");
// Resposta de PATCH deve representar uma única linha, nunca aceitar alteração indefinida.
var appearanceStore = new AgendaServiceCatalog((_, _, _) => Task.FromResult(CalendarTestHandler.Json("[]")));
await ExpectFailure(() => appearanceStore.SaveAppearance(company, sharedCatalog.Items[0], "Nome", "#FFFFFF"), "catálogo mudou");
sharedCatalog.Clear();
if (sharedCatalog.Items.Count != 0) throw new Exception("Logout deixou configuração da empresa anterior.");
Console.WriteLine("PASS: catálogo por empresa, cores compartilhadas, contraste, status e conflito de configuração.");

var workflowToday = new DateOnly(2026, 10, 26);
var todayAppointment = localRecord with { Date = "2026-10-26", Time = "08:00", Phone = "(35) 99999-9999" };
var todayWork = AgendaWorkflowRules.Plan([todayAppointment], workflowToday);
if (todayWork.Count != 2 || !todayWork.Any(w => w.TemplateKey == "confirmacao") ||
    !todayWork.Any(w => w.TemplateKey == "lembrete_d0")) throw new Exception("Rotina de hoje incorreta.");
if (AgendaWorkflowRules.Plan([todayAppointment with { Status = "cancelado" }], workflowToday).Count != 0)
    throw new Exception("Cancelado entrou na rotina de mensagem.");
var tomorrowWork = AgendaWorkflowRules.Plan([todayAppointment with { Date = "2026-10-27", Status = "confirmado" }], workflowToday);
if (tomorrowWork.Count != 1 || tomorrowWork[0].TemplateKey != "lembrete_d1")
    throw new Exception("Confirmado deve manter lembrete sem nova confirmação.");
var completedAppointment = todayAppointment with { Status = "concluido", CompletedDate = "2026-10-19" };
if (AgendaWorkflowRules.Plan([completedAppointment], workflowToday).Single().TemplateKey != "pos_venda" ||
    AgendaWorkflowRules.Plan([completedAppointment with { CompletedDate = "2026-10-20" }], workflowToday).Count != 0)
    throw new Exception("Regra de sete dias deve usar a conclusão real.");
if (AgendaWorkflowRules.Plan([completedAppointment with { FeedbackScore = 7 }], workflowToday).Count != 0 ||
    AgendaWorkflowRules.Plan([completedAppointment with { FeedbackScore = 8 }], workflowToday).Single().TemplateKey != "avaliacao_google")
    throw new Exception("Regra de retorno/convite da avaliação incorreta.");
if (AgendaWorkflowRules.Phone("(55) 99999-9999") != "5555999999999" ||
    AgendaWorkflowRules.Phone("+55 35 99999-9999") != "5535999999999" || AgendaWorkflowRules.Phone("123") is not null)
    throw new Exception("Normalização do telefone brasileiro incorreta.");
var reminderWork = todayWork.Single(x => x.TemplateKey == "lembrete_d0");
var preparedMessage = AgendaWorkflowRules.Message(reminderWork, "Olá, {cliente}! {servico} em {data} às {hora}.");
if (preparedMessage.Source != "saobento" || preparedMessage.Phone != "5535999999999" ||
    !preparedMessage.Text.Contains("26/10/2026") || !preparedMessage.WhatsAppUrl.StartsWith("https://web.whatsapp.com/send?"))
    throw new Exception("Rascunho perdeu origem, telefone ou data.");
// A fila de confirmação tem uma mensagem por atendimento, sem misturar lembrete/NPS.
var secondConfirmation = todayAppointment with { Id = "55555555-5555-5555-5555-555555555555", Date = "2026-10-27", Client = "Outro cliente", Time = "14:00" };
var blockedConfirmation = todayAppointment with { Id = "66666666-6666-6666-6666-666666666666", Phone = "123", Time = "09:00" };
var confirmationFixtures = new[] { secondConfirmation, todayAppointment, todayAppointment,
    blockedConfirmation, todayAppointment with { Status = "confirmado" }, completedAppointment,
    todayAppointment with { Status = "cancelado" }, todayAppointment with { Date = "2026-10-28" } };
var confirmationRows = AgendaConfirmationQueue.Build(confirmationFixtures, [], workflowToday);
if (confirmationRows.Count != 3 || confirmationRows[0].Work.Appointment.Id != todayAppointment.Id ||
    confirmationRows[1].HasPhone || confirmationRows[2].Work.Appointment.Id != secondConfirmation.Id ||
    confirmationRows.Any(r => r.Work.TemplateKey != "confirmacao"))
    throw new Exception("Fila repetiu cliente/rotina, perdeu cadastro incompleto ou ordem cronológica.");
if (AgendaConfirmationQueue.Build(confirmationFixtures, [], workflowToday, 1).Count != 2 ||
    AgendaConfirmationQueue.Build(confirmationFixtures, [], workflowToday, 2).Single().Work.Appointment.Id != secondConfirmation.Id)
    throw new Exception("Filtro de hoje/amanhã incorreto.");
var confirmationJob = new AgendaMessageJob("job-fixture", todayAppointment.Id, "confirmacao", confirmationRows[0].Work.Occurrence, "enviada_manual");
foreach (var terminal in new[] { "enviada_manual", "tratada_manual", "cancelada" })
    if (AgendaConfirmationQueue.Build(confirmationFixtures, [confirmationJob with { State = terminal }], workflowToday).Count != 2)
        throw new Exception("Confirmação tratada reapareceu para envio.");
if (AgendaConfirmationQueue.Build(confirmationFixtures, [confirmationJob with { TemplateKey = "lembrete_d0" }], workflowToday).Count != 3 ||
    AgendaConfirmationQueue.Build(confirmationFixtures, [confirmationJob with { Occurrence = "horario-anterior" }], workflowToday).Count != 3 ||
    AgendaConfirmationQueue.Build(confirmationFixtures, [confirmationJob with { State = "preparada" }], workflowToday).Count != 3)
    throw new Exception("Fila confundiu lembrete/ocorrência anterior/preparação com envio confirmado.");
var deferredConfirmations = new HashSet<string> { confirmationRows[0].Key };
if (AgendaConfirmationQueue.Build(confirmationFixtures, [], workflowToday, deferred: deferredConfirmations).Count != 2)
    throw new Exception("Pular por enquanto não adiou a conversa da sessão.");
deferredConfirmations.Clear();
if (AgendaConfirmationQueue.Build(confirmationFixtures, [], workflowToday, deferred: deferredConfirmations).Count != 3)
    throw new Exception("Rever pulados perdeu uma confirmação.");
var otherMessage = AgendaWorkflowRules.Message(new AgendaWorkItem(secondConfirmation, "confirmacao", "A confirmar"), "{cliente}: {servico}, {data} {hora}");
if (!otherMessage.Text.Contains("Outro cliente") || !otherMessage.Text.Contains("27/10/2026 14:00") || otherMessage.Text == preparedMessage.Text)
    throw new Exception("Mensagens de dois clientes não foram personalizadas separadamente.");
Console.WriteLine("PASS: serviços a confirmar por identidade/ocorrência, filtros, ordem, contatos incompletos, adiamento e texto individual.");

var workflowCalls = 0;
var workflow = new AgendaWorkflowStore((method, path, data) =>
{
    workflowCalls++;
    if (method == HttpMethod.Post && path == "rest/v1/rpc/agenda_prepare_manual_message")
        return Task.FromResult(CalendarTestHandler.Json(JsonSerializer.Serialize(new
        {
            id = "44444444-4444-4444-4444-444444444444", appointment_id = todayAppointment.Id,
            template_key = reminderWork.TemplateKey, occurrence_key = reminderWork.Occurrence, state = "preparada"
        })));
    if (method != HttpMethod.Patch || !path.Contains("company_id=eq." + company) || !path.Contains("state=eq.preparada"))
        throw new Exception("Registro de envio manual sem CAS/empresa.");
    return Task.FromResult(CalendarTestHandler.Json("[{}]"));
});
var preparedJob = await workflow.Prepare(company, reminderWork);
if (preparedJob.State != "preparada" || workflowCalls != 1)
    throw new Exception("Preparar mensagem não pode marcar como enviada.");
await workflow.Mark(company, preparedJob, "enviada_manual");
if (workflowCalls != 2) throw new Exception("Confirmação manual não foi uma ação separada.");
await ExpectFailure(() => workflow.SaveReviewTemplate(company, "https://google.com.example.test/avaliar"), "link HTTPS oficial");
if (workflowCalls != 2) throw new Exception("Link externo inválido chegou ao servidor.");
Console.WriteLine("PASS: pendências, lembretes, NPS sete dias, telefone, rascunho e envio manual separado.");

string[] filterValues = ["2026-10-26", "08:00", "João da Silva", "Desinsetização", "TESTES • agendado", "(35) 99999-9999", "Rua Central"];
if (!AgendaViewFilter.Matches(filterValues, todayAppointment, item, "desinsetizacao", "joao", 0, 4, "desinsetizacao"))
    throw new Exception("Busca sem acento ou filtro vinculado falhou.");
if (AgendaViewFilter.Matches(filterValues, todayAppointment, item, "desinsetizacao", "", 4, 0, null) ||
    AgendaViewFilter.Matches(filterValues, todayAppointment, item, "desinsetizacao", "", 0, 3, null))
    throw new Exception("Filtros de situação/origem misturaram registros.");
var csv = AgendaExport.Csv(new IReadOnlyList<string>[]
{
    ["2026-10-26", "08:00", "=HYPERLINK(\"https://example.test\")", "Serviço; combinado", "agendado", "+5535999999999", "Rua\nNova"]
});
if (!csv.Contains("\"'=HYPERLINK(") || !csv.Contains("\"Serviço; combinado\"") ||
    !csv.Contains("\"'+5535999999999\"") || !csv.Contains("Rua\nNova"))
    throw new Exception("CSV perdeu escape de campo ou proteção contra fórmula.");
Console.WriteLine("PASS: filtros por origem/situação/serviço, busca sem acento e CSV seguro.");

// Consentimento operacional exige ID concreto exato; o piloto continua recusando agenda principal.
if (!GoogleCalendarWriteGuard.IsExplicitOperationalCalendar("equipe@example.test", "equipe@example.test", true) ||
    GoogleCalendarWriteGuard.IsExplicitOperationalCalendar("primary", "primary", true) ||
    GoogleCalendarWriteGuard.IsExplicitOperationalCalendar("equipe@example.test", null, true) ||
    GoogleCalendarWriteGuard.IsExplicitOperationalCalendar("equipe@example.test", "outra@example.test", true) ||
    GoogleCalendarWriteGuard.IsExplicitOperationalCalendar("equipe@example.test", "equipe@example.test", false))
    throw new Exception("Consentimento operacional não foi vinculado ao calendário autorizado.");
var feedIdentity = GoogleCalendarIdentity.FromPrivateIcalUrl(new Uri(
    "https://calendar.google.com/calendar/ical/equipe%40group.calendar.google.com/private-fixture/basic.ics"));
if (feedIdentity != "equipe@group.calendar.google.com" ||
    GoogleCalendarIdentity.FromPrivateIcalUrl(new Uri(
        "https://calendar.google.com.example.test/calendar/ical/equipe/private-fixture/basic.ics")) is not null)
    throw new Exception("Identidade iCal incorreta: poderia esconder outro calendário.");
Console.WriteLine("PASS: consentimento por ID, alias principal bloqueado e deduplicação de fonte iCal por identidade.");


// Capabilities stay in a fragment; only authenticated RPC issuance carries the appointment identity.
var responseToken = new string('a', 64);
var responseDraft = AgendaWorkflowRules.WithResponseLink(new ComposeRequest("5535999999999", "Mensagem", "saobento"), responseToken);
if (!responseDraft.Text.Contains("modux-resposta.html#token=" + responseToken) ||
    responseDraft.Source != "saobento" || responseDraft.Phone != "5535999999999")
    throw new Exception("Link perdeu fragmento ou origem da conta.");
await ExpectFailure(() => Task.Run(() => AgendaWorkflowRules.WithResponseLink(responseDraft, "../invalid")), "inválido");
await ExpectFailure(() => Task.Run(() => AgendaWorkflowRules.WithResponseLink(
    new ComposeRequest("5535999999999", new string('x', 1790), "saobento"), responseToken)), "longo");
var issuanceCalls = 0;
var responseStore = new AgendaWorkflowStore((method, path, payload) =>
{
    issuanceCalls++;
    if (method != HttpMethod.Post || path != "rest/v1/rpc/agenda_issue_response_token")
        throw new Exception("Emissão fora da RPC autenticada.");
    using var body = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(payload));
    if (body.RootElement.GetProperty("p_company_id").GetString() != company ||
        body.RootElement.GetProperty("p_appointment_id").GetString() != todayAppointment.Id ||
        body.RootElement.GetProperty("p_expected_revision").GetInt32() != todayAppointment.Revision)
        throw new Exception("Emissão perdeu empresa/atendimento/revisão.");
    return Task.FromResult(CalendarTestHandler.Json(System.Text.Json.JsonSerializer.Serialize(new
    { token = responseToken, expires_at = DateTimeOffset.UtcNow.AddDays(1).ToString("O") })));
});
var responseWork = new AgendaWorkItem(todayAppointment, "confirmacao", "Teste");
if (await responseStore.IssueResponseToken(company, responseWork) != responseToken) throw new Exception("Token emitido inválido.");
await ExpectFailure(() => responseStore.IssueResponseToken(company, responseWork with { TemplateKey = "lembrete_d0" }), "não usa");
if (issuanceCalls != 1) throw new Exception("Rotina inadequada chegou ao servidor.");
Console.WriteLine("PASS: link exclusivo em fragmento, emissão por revisão/empresa e rascunho sem envio.");


// Explicit Google invitations preserve existing attendees and never retry ambiguous sends.
var invitationJson = System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject();
invitationJson["attendees"] = new System.Text.Json.Nodes.JsonArray(
    new System.Text.Json.Nodes.JsonObject { ["email"] = "existing@example.test", ["responseStatus"] = "accepted", ["optional"] = true });
using (var transport = new CalendarTestHandler((method, uri, _, body) =>
{
    if (method == "GET") return CalendarTestHandler.Json(invitationJson.ToJsonString());
    if (method != "PATCH" || uri.Query != "?sendUpdates=all") throw new Exception("Convite não usou operação explícita.");
    using var payload = JsonDocument.Parse(body!);
    var guests = payload.RootElement.GetProperty("attendees");
    if (guests.GetArrayLength() != 2 || guests[0].GetProperty("responseStatus").GetString() != "accepted" ||
        !guests[0].GetProperty("optional").GetBoolean() || guests[1].GetProperty("email").GetString() != "client@example.test" ||
        payload.RootElement.TryGetProperty("summary", out var ignoredSummary))
        throw new Exception("Convite perdeu convidados/respostas ou alterou o evento.");
    return CalendarTestHandler.Json(source);
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await api.Invite(item!, "client@example.test");
    if (transport.Calls != 2 || transport.LastETag != item!.ETag) throw new Exception("Convite sem ETag.");
}
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    if (method != "GET") throw new Exception("Repetição reenviou convite existente.");
    return CalendarTestHandler.Json(invitationJson.ToJsonString());
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await api.Invite(item!, "EXISTING@example.test");
    if (transport.Calls != 1) throw new Exception("Convite existente não foi idempotente.");
}
invitationJson["attendeesOmitted"] = true;
using (var transport = new CalendarTestHandler((method, _, _, _) =>
{
    if (method != "GET") throw new Exception("Lista incompleta foi sobrescrita.");
    return CalendarTestHandler.Json(invitationJson.ToJsonString());
}))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
    await ExpectFailure(() => api.Invite(item!, "client@example.test"), "incompleta");
using (var transport = new CalendarTestHandler((_, _, _, _) => throw new Exception("E-mail inválido chegou à rede.")))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.Invite(item!, "Nome <client@example.test>"), "e-mail válido");
    await ExpectFailure(() => api.Invite(item!, "client@example.test,other@example.test"), "e-mail válido");
}
auth.AllowWrite = false;
using (var transport = new CalendarTestHandler((_, _, _, _) => throw new Exception("Guarda de convite falhou.")))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
    await ExpectFailure(() => api.Invite(item!, "client@example.test"), "calendário de testes");
auth.AllowWrite = true;
using (var transport = new CalendarTestHandler((method, _, _, _) => method == "GET"
    ? CalendarTestHandler.Json(source) : throw new HttpRequestException("fixture timeout")))
using (var api = new GoogleCalendarApi(auth, new HttpClient(transport)))
{
    await ExpectFailure(() => api.Invite(item!, "client@example.test"), "pode ter processado");
    if (transport.Calls != 2) throw new Exception("Falha ambígua foi reenviada.");
}
Console.WriteLine("PASS: convites explícitos, preservação de participantes, ETag, repetição e falha sem reenvio.");


var scheduleReads = 0;
var scheduleStore = new AgendaWorkflowStore((method, path, _) =>
{
    if (method != HttpMethod.Get || !path.Contains("company_id=eq." + company) ||
        !path.Contains("appointment_id=eq." + todayAppointment.Id))
        throw new Exception("Conferência perdeu escopo do atendimento.");
    return Task.FromResult(CalendarTestHandler.Json(System.Text.Json.JsonSerializer.Serialize(new[]
    {
        new { company_id = company, appointment_id = todayAppointment.Id, calendar_id = "fixture-calendar",
            event_id = item!.Id, event_etag = item.ETag, sync_status = "sincronizado" }
    })));
});
var scheduledWork = responseWork with { Appointment = todayAppointment with
{ Date = item!.Date.ToString("yyyy-MM-dd"), Time = item.Time, Duration = item.Duration } };
await scheduleStore.ValidateSchedule(company, scheduledWork, "fixture-calendar", id =>
{ scheduleReads++; return Task.FromResult(item!); });
await ExpectFailure(() => scheduleStore.ValidateSchedule(company, scheduledWork, "other-calendar", id =>
    throw new Exception("Calendário errado chegou ao Google.")), "calendário vinculado");
await ExpectFailure(() => scheduleStore.ValidateSchedule(company, scheduledWork, "fixture-calendar",
    id => Task.FromResult(item! with { ETag = "changed" })), "Conferir vínculo");
if (scheduleReads != 1) throw new Exception("Conferência inesperada do calendário.");
Console.WriteLine("PASS: confirmação/lembretes conferem evento vinculado antes de emitir link ou rascunho.");

// Google colors must come from the API, including IDs beyond the historic eleven.
using (var colorTransport = new CalendarTestHandler((method, uri, bearer, body) =>
{
    if (method != "GET" || body is not null) throw new Exception("Leitura de cores escreveu dados.");
    if (uri.AbsolutePath.EndsWith("/colors"))
        return CalendarTestHandler.Json("""{"event":{"9":{"background":"#3F51B5"},"24":{"background":"#123456"},"bad":{"background":"invalid"}}}""");
    if (uri.AbsolutePath.Contains("/calendarList/"))
        return CalendarTestHandler.Json("""{"backgroundColor":"#0B8043"}""");
    throw new Exception("Endpoint de cores inesperado.");
}))
using (var colorApi = new GoogleCalendarApi(new FakeCalendarAuthorization(), new HttpClient(colorTransport)))
{
    await colorApi.LoadColors();
    await colorApi.LoadColors();
    if (colorTransport.Calls != 2) throw new Exception("Cache da paleta repetiu GETs desnecessários.");
    colorApi.InvalidateColors(); await colorApi.LoadColors();
    if (colorTransport.Calls != 4) throw new Exception("Atualização explícita não invalidou a paleta.");
    if (colorApi.EventColorHex(item! with { ColorId = "24" }) != "#123456" ||
        colorApi.EventColorHex(item! with { ColorId = "" }) != "#0B8043" ||
        colorApi.EventColorHex(item! with { ColorId = "0" }) != "#0B8043" ||
        colorApi.EventColorHex(item! with { ColorId = "unknown" }) is not null ||
        colorApi.ClosestColor("#3F51B5") != "9" || colorApi.EventColors.ContainsKey("bad"))
        throw new Exception("Cor individual, herdada ou paleta dinâmica incorreta.");
}
var typedDraft = change with { ServiceTypeId = "orcamento", SelectedColorId = "5" };
var typedPayload = typedDraft.GooglePayload(new Dictionary<string, string>
{ ["other_app"] = "preserve", ["modux_company_id"] = "fixture-company", ["modux_appointment_id"] = "fixture-appointment" });
if (typedPayload["colorId"]!.GetValue<string>() != "5" ||
    typedPayload["summary"]!.GetValue<string>() != change.Title ||
    typedPayload["extendedProperties"]!["private"]!["other_app"]!.GetValue<string>() != "preserve" ||
    typedPayload["extendedProperties"]!["private"]!["modux_company_id"]!.GetValue<string>() != "fixture-company" ||
    typedPayload["extendedProperties"]!["private"]!["modux_service_id"]!.GetValue<string>() != "orcamento" ||
    change.GooglePayload().ContainsKey("colorId") || change.GooglePayload().ContainsKey("extendedProperties") ||
    (typedDraft with { SelectedColorId = null }).GooglePayload().ContainsKey("colorId"))
    throw new Exception("Tipo alterou título, perdeu metadados ou recoloriu sem escolha explícita.");
using var typedJson = JsonDocument.Parse(source.Replace("\"colorId\":\"9\"",
    "\"extendedProperties\":{\"private\":{\"modux_service_id\":\"orcamento\",\"other_app\":\"preserve\"}},\"colorId\":\"9\""));
var typedEvent = GoogleEventSnapshot.Parse(typedJson.RootElement)!;
if (typedEvent.ServiceTypeId != "orcamento" || typedEvent.PrivateProperties["other_app"] != "preserve")
    throw new Exception("Tipo explícito não sobreviveu à leitura Google.");
Console.WriteLine("PASS: cores reais/herdadas, paleta dinâmica e tipo explícito sem recoloração automática/perda de metadados.");

// Real display ranges, exact end-time fading and collision layout.
var sunday = new DateOnly(2026, 11, 1);
var weekRange = AgendaCalendarLayout.Range(sunday, AgendaCalendarMode.Week);
if (weekRange.First != new DateOnly(2026, 10, 26) || weekRange.Last != sunday ||
    AgendaCalendarLayout.Range(sunday, AgendaCalendarMode.Day).First != sunday ||
    AgendaCalendarLayout.Range(sunday, AgendaCalendarMode.Month).Last != new DateOnly(2026, 11, 30))
    throw new Exception("Dia/semana/mês retornaram intervalos incorretos.");
var calendarEntry = new AgendaCalendarEntry(new DateOnly(2026, 10, 27), "08:00", "Fixture", "Dois serviços", "agendado", AgendaServiceColors.Desinsetizacao, 60);
if (calendarEntry.IsPast(new DateTime(2026, 10, 27, 8, 59, 59)) || !calendarEntry.IsPast(new DateTime(2026, 10, 27, 9, 0, 0)))
    throw new Exception("Opacidade deve usar término, não o início do compromisso.");
var allDays = calendarEntry with { AllDay = true, ExclusiveEndDate = new DateOnly(2026, 10, 29) };
if (allDays.IsPast(new DateTime(2026, 10, 28, 23, 59, 59)) || !allDays.IsPast(new DateTime(2026, 10, 29)))
    throw new Exception("Dia inteiro não respeitou o fim exclusivo.");
var collisions = AgendaCalendarLayout.Slots([calendarEntry with { Duration = 120, Key = "a" },
    calendarEntry with { Time = "09:00", Duration = 120, Key = "b" },
    calendarEntry with { Time = "10:00", Duration = 120, Key = "c" }], calendarEntry.Date);
if (collisions.Count != 3 || collisions.Any(slot => slot.Lanes != 2) || collisions[0].Lane != collisions[2].Lane)
    throw new Exception("Eventos simultâneos foram sobrepostos/ocultados ou horários adjacentes perderam a coluna.");
var crossing = AgendaCalendarLayout.Slots([calendarEntry with { Time = "23:30", Duration = 90 }], calendarEntry.Date.AddDays(1));
if (crossing.Count != 1 || crossing[0].StartMinute != 0 || crossing[0].EndMinute != 60)
    throw new Exception("Evento atravessando meia-noite desapareceu na visão diária.");
var originalColor = calendarEntry.Category.Color;
if (AgendaCalendarLayout.Fade(originalColor, System.Drawing.Color.White, false) != originalColor ||
    AgendaCalendarLayout.Fade(originalColor, System.Drawing.Color.White, true) == originalColor || calendarEntry.Category.Color != originalColor)
    throw new Exception("Opacidade alterou a cor de origem ou afetou compromisso futuro.");
Console.WriteLine("PASS: dia/semana/mês, semana segunda-domingo, virada de mês/meia-noite, sobreposição e opacidade por término.");

var combinedDraft = change with { ServiceTypeIds = ["desinsetizacao", "reservatorio"], SelectedColorId = "7" };
using (var multiTransport = new CalendarTestHandler((method, uri, bearer, body) =>
{
    if (method == "GET") return CalendarTestHandler.Json(typedJson.RootElement.GetRawText());
    using var written = JsonDocument.Parse(body!);
    var metadata = written.RootElement.GetProperty("extendedProperties").GetProperty("private");
    if (method != "PATCH" || metadata.GetProperty("modux_service_ids").GetString() != "desinsetizacao,reservatorio" ||
        metadata.GetProperty("other_app").GetString() != "preserve" || written.RootElement.TryGetProperty("attendees", out var ignoredGuests))
        throw new Exception("PATCH combinado perdeu serviço/metadados ou escreveu convidados.");
    return CalendarTestHandler.Json(source.Replace("\"colorId\":\"9\"", "\"extendedProperties\":{\"private\":{\"modux_service_ids\":\"desinsetizacao,reservatorio\"}},\"colorId\":\"7\""));
}))
using (var combinedApi = new GoogleCalendarApi(new FakeCalendarAuthorization(), new HttpClient(multiTransport)))
{
    var combined = await combinedApi.Update(item!, combinedDraft);
    if (!combined.ServiceTypeIds.SequenceEqual(new[] { "desinsetizacao", "reservatorio" }))
        throw new Exception("Dois serviços não sobreviveram à gravação/leitura.");
    if (!AgendaViewFilter.Matches(new[] { "2026-10-27", "08:00", "Fixture", "Dois serviços", "Google • editável", "", "" },
        null, combined, "desinsetizacao", "", 0, 0, "reservatorio"))
        throw new Exception("Filtro não encontrou o segundo serviço.");
}
var serviceOriginal = new AgendaLocalSnapshot(Guid.NewGuid().ToString(), 4, "Fixture", "Anterior", ["orcamento"], "2026-10-27", "08:00", "agendado", "", "");
var serviceStore = new AgendaAppointmentStore((method, path, body) =>
{
    using var written = JsonDocument.Parse(JsonSerializer.Serialize(body));
    if (method != HttpMethod.Patch || !path.Contains("revision=eq.4") || !path.Contains("company_id=eq." + company) ||
        written.RootElement.EnumerateObject().Count() != 2 || written.RootElement.GetProperty("service_ids").GetArrayLength() != 2)
        throw new Exception("Edição vinculada de serviços escreveu campos alheios ou ignorou revisão/empresa.");
    return Task.FromResult(CalendarTestHandler.Json(JsonSerializer.Serialize(new[] { new { id = serviceOriginal.Id, revision = 5,
        client = "Fixture", primary_service = "Desinsetização + Reservatório", service_ids = new[] { "desinsetizacao", "reservatorio" },
        date = "2026-10-27", time = "08:00", status = "agendado" } })));
});
var servicesSaved = await serviceStore.UpdateServices(company, serviceOriginal, ["desinsetizacao", "reservatorio"], "Desinsetização + Reservatório");
if (servicesSaved.ServiceIds.Length != 2 || servicesSaved.Revision != 5) throw new Exception("Serviços operacionais não confirmados.");
Console.WriteLine("PASS: combinação de serviços, PATCH Google real simulado, preservação, filtro secundário e CAS operacional restrito.");

// Regression: nearest RGB picked Blueberry for #039BE5 with Google's pastel API palette.
using (var historicalTransport = new CalendarTestHandler((method, uri, bearer, body) =>
    CalendarTestHandler.Json(uri.AbsolutePath.EndsWith("/colors")
        ? """{"event":{"7":{"background":"#46d6db"},"9":{"background":"#5484ed"}}}"""
        : """{"backgroundColor":"#039BE5"}""")))
using (var historicalApi = new GoogleCalendarApi(new FakeCalendarAuthorization(), new HttpClient(historicalTransport)))
{
    await historicalApi.LoadColors();
    if (historicalApi.ClosestColor("#039BE5") != "9" ||
        historicalApi.ColorForService("desinsetizacao", "#039BE5") != "0" ||
        historicalApi.ColorForService("desratizacao", "#3F51B5") != "9" ||
        historicalApi.ColorForService("orcamento", "#F6BF26") != "5" ||
        historicalApi.ColorForService("confirmacao", "#0B8043") != "10" ||
        historicalApi.ColorForService("cancelado", "#D50000") != "11" ||
        historicalApi.ColorForService("interno", "#F4511E") != "6")
        throw new Exception("RGB aproximado voltou a confundir desinsetização com Mirtilo.");
    var inheritedDraft = change with { ServiceTypeIds = ["desinsetizacao", "reservatorio"],
        SelectedColorId = historicalApi.ColorForService("desinsetizacao", "#039BE5") };
    var inheritedJson = System.Text.Json.Nodes.JsonNode.Parse(typedJson.RootElement.GetRawText())!.AsObject();
    inheritedJson.Remove("colorId");
    inheritedJson["extendedProperties"]!["private"]!["modux_service_ids"] = "desinsetizacao,reservatorio";
    inheritedJson["extendedProperties"]!["private"]!["modux_service_id"] = "desinsetizacao";
    using (var writeTransport = new CalendarTestHandler((method, uri, bearer, body) =>
    {
        if (method == "GET") return CalendarTestHandler.Json(typedJson.RootElement.GetRawText());
        using var payload = JsonDocument.Parse(body!);
        if (method == "PATCH" && (!payload.RootElement.TryGetProperty("colorId", out var reset) || reset.ValueKind != JsonValueKind.Null))
            throw new Exception("PATCH não removeu explicitamente a cor Mirtilo anterior.");
        if (method == "POST" && payload.RootElement.TryGetProperty("colorId", out var forbiddenColor))
            throw new Exception("Novo evento impôs uma cor em vez de herdar a agenda.");
        if (!uri.Query.Contains("sendUpdates=none") ||
            (method == "PATCH" && payload.RootElement.GetProperty("extendedProperties").GetProperty("private").GetProperty("other_app").GetString() != "preserve"))
            throw new Exception("Correção perdeu metadados ou alterou o fluxo de notificação.");
        return CalendarTestHandler.Json(inheritedJson.ToJsonString());
    }))
    using (var writeApi = new GoogleCalendarApi(new FakeCalendarAuthorization(), new HttpClient(writeTransport)))
    {
        var corrected = await writeApi.Update(item!, inheritedDraft);
        if (historicalApi.EventColorHex(corrected) != "#039BE5" || corrected.ServiceTypeIds.Length != 2)
            throw new Exception("Desinsetização não herdou o azul padrão ou perdeu o segundo serviço.");
        await writeApi.Create(inheritedDraft);
        if (writeTransport.Calls != 3) throw new Exception("Correção repetiu uma escrita Google.");
    }
    var ratColor = (change with { ServiceTypeIds = ["desratizacao"], SelectedColorId = historicalApi.ColorForService("desratizacao", "#3F51B5") }).GooglePayload();
    if (ratColor["colorId"]!.GetValue<string>() != "9" || change.GooglePayload().ContainsKey("colorId"))
        throw new Exception("Desratização perdeu Mirtilo ou edição comum passou a recolorir.");
}
Console.WriteLine("PASS: bug RGB reproduzido, desinsetização herda azul padrão, PATCH remove override, criação omite cor e desratização mantém Mirtilo.");

static async Task ExpectFailure(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (InvalidOperationException error) when (error.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
    { return; }
    throw new Exception("Operação deveria falhar com mensagem: " + message);
}

sealed class FakeCalendarAuthorization : IGoogleCalendarAuthorization
{
    public string? CalendarId => "isolated-test@group.calendar.google.com";
    internal bool AllowWrite { get; set; } = true;
    public Task<string> AccessTokenAsync(CancellationToken ct) => Task.FromResult("test-only-token");
    public void DemandSafeWrite()
    {
        if (!AllowWrite) throw new InvalidOperationException("Selecione o calendário de testes.");
    }
}

sealed class CalendarTestHandler(
    Func<string, Uri, string?, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    internal int Calls { get; private set; }
    internal string? LastETag { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        LastETag = request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request.Method.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body);
    }
    internal static HttpResponseMessage Json(string body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
}
