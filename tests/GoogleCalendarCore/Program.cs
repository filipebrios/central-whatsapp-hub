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
