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
