using CentralWhatsApp.WebView2;

var tests = new (string Name, AgendaServiceCategory Actual, AgendaServiceCategory Expected)[]
{
    ("Desinsetização com acento", AgendaServiceColors.Classify("Desinsetização no condomínio"), AgendaServiceColors.Desinsetizacao),
    ("Desratização", AgendaServiceColors.Classify("DESRATIZAÇÃO cliente"), AgendaServiceColors.Desratizacao),
    ("Orçamento", AgendaServiceColors.Classify("Orçamento Sr. João"), AgendaServiceColors.Orcamento),
    ("Confirmar", AgendaServiceColors.Classify("CONFIRMAR visita terça"), AgendaServiceColors.Confirmar),
    ("Cancelado tem prioridade", AgendaServiceColors.Classify("CANCELADO - Desratização"), AgendaServiceColors.Cancelado),
    ("Reagendar", AgendaServiceColors.Classify("Reagendar cliente A"), AgendaServiceColors.Cancelado),
    ("Interno e curso", AgendaServiceColors.Classify("Curso técnico"), AgendaServiceColors.Interno),
    ("Descupinização", AgendaServiceColors.Classify("Descupinização de solo"), AgendaServiceColors.Descupinizacao),
    ("Higienização reservatório", AgendaServiceColors.Classify("Limpeza e higienização de reservatório"), AgendaServiceColors.Reservatorio),
    ("Caixa de água", AgendaServiceColors.Classify("Limpeza caixa d'água"), AgendaServiceColors.Reservatorio),
    ("Caixa de gordura", AgendaServiceColors.Classify("Limpeza caixa de gordura"), AgendaServiceColors.CaixaGordura),
    ("Sanitização", AgendaServiceColors.Classify("Sanitização preventiva"), AgendaServiceColors.Sanitizacao),
    ("Capina", AgendaServiceColors.Classify("Capina química pátio"), AgendaServiceColors.Capina),
    ("Pelo texto da descrição", AgendaServiceColors.Classify("Clínica Alfa", "Desratização do prédio"), AgendaServiceColors.Desratizacao),
    ("Título prevalece sobre texto", AgendaServiceColors.Classify("Desinsetização condomínio", "Desratização complementar"), AgendaServiceColors.Desinsetizacao),
    ("Cliente desconhecido fica neutro", AgendaServiceColors.Classify("NF CASA GRAND CENTRAL"), AgendaServiceColors.NaoClassificado),
    ("IDs existentes preservados", AgendaServiceColors.FromServiceIds(["desratizacao"], "Cliente", "agendado"), AgendaServiceColors.Desratizacao),
    ("Status de reagendar prevalece", AgendaServiceColors.FromServiceIds(["desinsetizacao"], "Cliente", "reagendar"), AgendaServiceColors.Cancelado)
};
var problems = tests.Where(test => test.Actual.Id != test.Expected.Id).ToList();
foreach (var test in tests)
    Console.WriteLine((test.Actual.Id == test.Expected.Id ? "OK  " : "ERRO") +
        $" {test.Name}: {test.Actual.Id} (esperado {test.Expected.Id})");

var palette = new[]
{
    (AgendaServiceColors.Desinsetizacao, "#039BE5"),
    (AgendaServiceColors.Desratizacao, "#3F51B5"),
    (AgendaServiceColors.Orcamento, "#F6BF26"),
    (AgendaServiceColors.Confirmar, "#0B8043"),
    (AgendaServiceColors.Cancelado, "#D50000"),
    (AgendaServiceColors.Interno, "#F4511E")
};
foreach (var (service, expected) in palette)
{
    var actual = $"#{service.Color.R:X2}{service.Color.G:X2}{service.Color.B:X2}";
    if (actual != expected)
    {
        problems.Add(("Cor " + service.Label, service, AgendaServiceColors.NaoClassificado));
        Console.WriteLine($"COR INCORRETA {service.Label}: {actual} ≠ {expected}");
    }
}
if (problems.Count > 0)
{
    Console.Error.WriteLine($"{problems.Count} testes de cores falharam.");
    return 1;
}
Console.WriteLine($"OK {tests.Length} testes de classificação e {palette.Length} testes de cores passaram.");
return 0;
