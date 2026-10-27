namespace CentralWhatsApp.WebView2;

internal sealed record AgendaConfirmationRow(AgendaWorkItem Work, AgendaMessageJob? Job)
{
    internal string Key => Work.Appointment.Id + "/" + Work.Occurrence;
    internal bool HasPhone => AgendaWorkflowRules.Phone(Work.Appointment.Phone) is not null;
    public override string ToString() =>
        $"{DateOnly.Parse(Work.Appointment.Date):dd/MM} {Work.Appointment.Time} • {Work.Appointment.Client} • {Work.Appointment.Service}" +
        (!HasPhone ? " • completar WhatsApp" : Job?.State == "preparada" ? " • rascunho preparado" : "");
}

internal static class AgendaConfirmationQueue
{
    internal static IReadOnlyList<AgendaConfirmationRow> Build(IEnumerable<AgendaLocalSnapshot> appointments,
        IReadOnlyList<AgendaMessageJob> jobs, DateOnly today, int period = 0, ISet<string>? deferred = null)
    {
        var rows = new List<AgendaConfirmationRow>();
        foreach (var a in appointments)
        {
            if (a.Status is not ("agendado" or "revisao") ||
                !DateOnly.TryParseExact(a.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date) ||
                !TimeOnly.TryParse(a.Time, out _) || date < today || date > today.AddDays(1) ||
                (period == 1 && date != today) || (period == 2 && date != today.AddDays(1))) continue;
            var work = new AgendaWorkItem(a, "confirmacao", "A confirmar");
            var matching = jobs.Where(j => j.AppointmentId == a.Id && j.TemplateKey == work.TemplateKey && j.Occurrence == work.Occurrence).ToArray();
            if (matching.Any(j => j.State is "enviada_manual" or "tratada_manual" or "cancelada")) continue;
            var row = new AgendaConfirmationRow(work, matching.FirstOrDefault());
            if (deferred?.Contains(row.Key) != true) rows.Add(row);
        }
        return rows.DistinctBy(r => r.Key).OrderBy(r => r.Work.Appointment.Date, StringComparer.Ordinal)
            .ThenBy(r => r.Work.Appointment.Time, StringComparer.Ordinal)
            .ThenBy(r => r.Work.Appointment.Client, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
