using System.Drawing;
using System.Globalization;
using System.Text;

namespace CentralWhatsApp.WebView2;

/// <summary>
/// Paleta operacional da São Bento aplicada apenas ao MODUX.
/// O Google iCal não garante expor a cor individual dos eventos: os tipos
/// são inferidos de rótulos explícitos e não alteram o Google Calendar.
/// </summary>
internal sealed record AgendaServiceCategory(
    string Id, string Label, string GoogleColorName, Color Color, Color TextColor);

internal static class AgendaServiceColors
{
    private static readonly Color White = Color.White;
    private static readonly Color Dark = Color.FromArgb(15, 30, 40);

    internal static readonly AgendaServiceCategory Desinsetizacao =
        new("desinsetizacao", "Desinsetização", "Cobalto", Color.FromArgb(3, 155, 229), White);
    internal static readonly AgendaServiceCategory Desratizacao =
        new("desratizacao", "Desratização", "Mirtilo", Color.FromArgb(63, 81, 181), White);
    internal static readonly AgendaServiceCategory Orcamento =
        new("orcamento", "Orçamento", "Banana", Color.FromArgb(246, 191, 38), Dark);
    internal static readonly AgendaServiceCategory Confirmar =
        new("confirmacao", "Confirmar", "Manjericão", Color.FromArgb(11, 128, 67), White);
    internal static readonly AgendaServiceCategory Cancelado =
        new("cancelado", "Cancelado / reagendar", "Tomate", Color.FromArgb(213, 0, 0), White);
    internal static readonly AgendaServiceCategory Interno =
        new("interno", "Internos / curso", "Abóbora", Color.FromArgb(244, 81, 30), White);

    // Cores auxiliares propostas para serviços sem cor histórica específica.
    internal static readonly AgendaServiceCategory Descupinizacao =
        new("descupinizacao", "Descupinização", "Uva", Color.FromArgb(142, 36, 170), White);
    internal static readonly AgendaServiceCategory Reservatorio =
        new("reservatorio", "Reservatório", "Turquesa", Color.FromArgb(0, 121, 107), White);
    internal static readonly AgendaServiceCategory CaixaGordura =
        new("caixa_gordura", "Caixa de gordura", "Grafite", Color.FromArgb(97, 97, 97), White);
    internal static readonly AgendaServiceCategory Sanitizacao =
        new("sanitizacao", "Sanitização", "Verde", Color.FromArgb(67, 160, 71), White);
    internal static readonly AgendaServiceCategory Capina =
        new("capina_quimica", "Capina química", "Oliva", Color.FromArgb(111, 125, 47), White);
    internal static readonly AgendaServiceCategory NaoClassificado =
        new("outros", "Não identificado", "Cinza", Color.FromArgb(224, 229, 232), Dark);

    internal static readonly IReadOnlyList<AgendaServiceCategory> Legend =
    [
        Desinsetizacao, Desratizacao, Orcamento, Confirmar, Cancelado, Interno,
        Descupinizacao, Reservatorio, CaixaGordura, Sanitizacao, Capina, NaoClassificado
    ];

    internal static AgendaServiceCategory FromServiceIds(IEnumerable<string>? ids, string? serviceName, string? status)
    {
        var statusKey = Normalize(status);
        if (Has(statusKey, "cancel", "reagend", "remarc")) return Cancelado;
        if (Has(statusKey, "confirmar", "a confirmar", "aguardando confirmacao")) return Confirmar;
        var keys = ids?.Select(Normalize).Where(x => x.Length > 0).ToArray() ?? [];
        foreach (var id in keys)
        {
            var category = ByExplicitId(id);
            if (category != NaoClassificado) return category;
        }
        return Classify(serviceName, null, status);
    }

    internal static AgendaServiceCategory Classify(string? title, string? description = null, string? status = null)
    {
        var statusKey = Normalize(status);
        var titleKey = Normalize(title);
        if (Has(statusKey, "cancel", "reagend", "remarc") ||
            Has(titleKey, "cancel", "reagend", "remarc")) return Cancelado;
        if (Has(statusKey, "a confirmar", "aguardando confirmacao") ||
            Has(titleKey, "confirmacao", "confirmar", "confirmacao de agendamento"))
            return Confirmar;

        // Título é mais confiável do que descrição; descrição pode citar outros serviços.
        var primary = ByTerms(titleKey);
        if (primary != NaoClassificado) return primary;
        return ByTerms(Normalize(description));
    }

    private static AgendaServiceCategory ByExplicitId(string id)
    {
        return id switch
        {
            "desinsetizacao" => Desinsetizacao,
            "desratizacao" => Desratizacao,
            "orcamento" => Orcamento,
            "confirmacao" => Confirmar,
            "interno" => Interno,
            "descupinizacao" => Descupinizacao,
            "reservatorio" => Reservatorio,
            "caixa_gordura" => CaixaGordura,
            "sanitizacao" => Sanitizacao,
            "capina_quimica" => Capina,
            _ => NaoClassificado
        };
    }

    private static AgendaServiceCategory ByTerms(string text)
    {
        if (text.Length == 0) return NaoClassificado;
        if (Has(text, "desratiz", "roedor", "controle de ratos")) return Desratizacao;
        if (Has(text, "descupin", "cupim", "cupins")) return Descupinizacao;
        if (Has(text, "desinset", "dedetiz", "baratas", "formigas", "escorpioes", "pulgas", "carrapatos"))
            return Desinsetizacao;
        if (Has(text, "reservatorio", "caixa dagua", "caixa d agua", "cisterna")) return Reservatorio;
        if (Has(text, "caixa de gordura", "caixa gordura")) return CaixaGordura;
        if (Has(text, "sanitiz", "desinfeccao")) return Sanitizacao;
        if (Has(text, "capina", "herbicida")) return Capina;
        if (Has(text, "orcamento", "vistoria comercial", "visita tecnica para orcamento")) return Orcamento;
        if (Has(text, "reuniao interna", "treinamento", "curso", "ferias", "folga")) return Interno;
        return NaoClassificado;
    }

    private static bool Has(string value, params string[] snippets)
        => snippets.Any(x => value.Contains(x, StringComparison.Ordinal));

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
