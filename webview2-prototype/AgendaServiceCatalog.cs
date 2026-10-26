using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal sealed record AgendaServiceDefinition(string Id, string Label, string ColorHex,
    int SortOrder, bool Active, string UpdatedAt)
{
    internal Color Color => ColorTranslator.FromHtml(ColorHex);
    public override string ToString() => Label + (Active ? "" : " (legenda)");
    internal static AgendaServiceDefinition Parse(JsonElement row)
    {
        var hex = row.GetProperty("color").GetString() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$"))
            throw new InvalidOperationException("O catálogo retornou uma cor inválida.");
        return new(row.GetProperty("id").GetString()!, row.GetProperty("label").GetString()!, hex,
            row.GetProperty("sort_order").GetInt32(), row.GetProperty("is_active").GetBoolean(),
            row.GetProperty("updated_at").GetString()!);
    }
}

internal sealed class AgendaServiceCatalog(
    Func<HttpMethod, string, object?, Task<HttpResponseMessage>> authorized)
{
    internal IReadOnlyList<AgendaServiceDefinition> Items { get; private set; } = [];

    internal void Clear() => Items = [];

    internal async Task Load(string companyId)
    {
        using var response = await authorized(HttpMethod.Get,
            "rest/v1/agenda_service_types?select=id,label,color,sort_order,is_active,updated_at" +
            "&company_id=eq." + Uri.EscapeDataString(companyId) + "&order=sort_order.asc,id.asc&limit=250", null);
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.GetArrayLength() >= 250)
            throw new InvalidOperationException("Catálogo grande demais para carregar sem revisão.");
        Items = json.RootElement.EnumerateArray().Select(AgendaServiceDefinition.Parse).ToArray();
    }

    internal AgendaServiceCategory Apply(AgendaServiceCategory original)
    {
        var item = Items.FirstOrDefault(x => x.Id == original.Id);
        return item is null ? original : Category(item, original.GoogleColorName);
    }

    internal AgendaServiceCategory Resolve(AgendaLocalSnapshot local)
    {
        var fallback = AgendaServiceColors.FromServiceIds(local.ServiceIds, local.Service, local.Status);
        if (fallback.Id is "cancelado" || local.Status is "revisao" or "confirmar")
            return Apply(local.Status is "revisao" or "confirmar" ? AgendaServiceColors.Confirmar : fallback);
        foreach (var id in local.ServiceIds)
            if (Items.FirstOrDefault(x => x.Id == id) is { } item)
                return Category(item, "Cor da equipe");
        return Apply(fallback);
    }

    private static AgendaServiceCategory Category(AgendaServiceDefinition item, string colorName)
    {
        var color = item.Color;
        // Luminância relativa para texto legível em cores escolhidas pela equipe.
        static double Linear(byte value)
        {
            var c = value / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return new(item.Id, item.Label, colorName, color, luminance > 0.179 ? Color.Black : Color.White);
    }

    internal async Task SaveAppearance(string companyId, AgendaServiceDefinition original, string label, string hex)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120 ||
            !System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$") ||
            string.IsNullOrWhiteSpace(original.UpdatedAt))
            throw new InvalidOperationException("Confira o nome e a cor do serviço.");
        using var response = await authorized(HttpMethod.Patch,
            "rest/v1/agenda_service_types?company_id=eq." + Uri.EscapeDataString(companyId) +
            "&id=eq." + Uri.EscapeDataString(original.Id) +
            "&updated_at=eq." + Uri.EscapeDataString(original.UpdatedAt), new
            {
                label = label.Trim(), color = hex.ToUpperInvariant(),
                updated_at = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
        Check(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("O catálogo mudou. Atualize antes de salvar novamente.");
    }

    private static void Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? "Somente um administrador pode alterar o catálogo da equipe."
            : "Catálogo não confirmado. Atualize antes de tentar novamente (HTTP " + (int)response.StatusCode + ").");
    }
}
