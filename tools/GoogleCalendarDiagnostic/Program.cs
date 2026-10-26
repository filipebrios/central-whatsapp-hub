using CentralWhatsApp.WebView2;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DiagnosticForm());
    }
}

internal sealed class DiagnosticForm : Form
{
    private readonly GoogleDesktopOAuth oauth = new(diagnostic: true);
    private readonly GoogleCalendarApi api;
    private readonly Button connect = new() { Text = "1. Conectar Google", AutoSize = true };
    private readonly Button refresh = new() { Text = "2. Consultar dia", AutoSize = true };
    private readonly Button create = new() { Text = "3. Criar evento fictício", AutoSize = true };
    private readonly Button edit = new() { Text = "Editar / excluir selecionado", AutoSize = true };
    private readonly DateTimePicker date = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy",
        Value = new DateTime(2026, 10, 23), Width = 135
    };
    private readonly ListBox events = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label status = new()
    {
        Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(14),
        Text = "Conecte a conta e selecione MODUX - TESTES. Credenciais ficam somente neste Windows."
    };
    private bool busy;

    internal DiagnosticForm()
    {
        api = new GoogleCalendarApi(oauth);
        Text = "MODUX 1.9 • Verificação isolada Google (desenvolvimento)";
        Width = 920; Height = 560; MinimumSize = new Size(800, 500);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var header = new Label
        {
            Dock = DockStyle.Top, Height = 95, Padding = new Padding(14),
            Text = "Verificação técnica da integração Google Calendar\n" +
                   "Use somente dados fictícios na agenda secundária MODUX - TESTES.\n" +
                   "Esta ferramenta usa uma pasta própria e não abre sessões WhatsApp/WaSeller."
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 88, Padding = new Padding(10), AutoScroll = true
        };
        actions.Controls.AddRange([connect, date, refresh, create, edit]);
        Controls.Add(events); Controls.Add(actions); Controls.Add(header); Controls.Add(status);
        connect.Click += async (_, _) =>
        {
            if (busy) return;
            using var dialog = new GoogleCalendarManageDialog(oauth, api);
            dialog.ShowDialog(this);
            RefreshControls();
            if (oauth.CanWriteSelectedCalendar) await Run(LoadEvents);
        };
        refresh.Click += async (_, _) => await Run(LoadEvents);
        create.Click += async (_, _) => await EditEvent(null);
        edit.Click += async (_, _) =>
        {
            if (events.SelectedItem is EventChoice selected) await EditEvent(selected.Event);
        };
        events.SelectedIndexChanged += (_, _) => RefreshControls();
        date.ValueChanged += (_, _) =>
        {
            events.Items.Clear();
            status.Text = "Data alterada. Clique em Consultar dia para carregar eventos desta data.";
        };
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            status.Text = "Aguarde a operação Google terminar antes de fechar (até 35 segundos).";
        };
        FormClosed += (_, _) => { api.Dispose(); oauth.Dispose(); };
        RefreshControls();
    }

    private void RefreshControls()
    {
        connect.Enabled = date.Enabled = events.Enabled = !busy;
        refresh.Enabled = create.Enabled = !busy && oauth.CanWriteSelectedCalendar;
        edit.Enabled = !busy && oauth.CanWriteSelectedCalendar && events.SelectedItem is EventChoice;
    }

    private async Task Run(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        RefreshControls();
        try { await operation(); }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException
            or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            status.Text = "Operação não confirmada: " + error.Message;
            MessageBox.Show(this, error.Message, "Verificação Google", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { busy = false; RefreshControls(); }
    }

    private async Task LoadEvents()
    {
        // A guarda é usada também na leitura: esta ferramenta nunca lista a agenda operacional.
        oauth.DemandSafeWrite();
        var day = DateOnly.FromDateTime(date.Value);
        status.Text = "Consultando calendário isolado MODUX - TESTES...";
        var found = await api.Events(day, day);
        events.Items.Clear();
        foreach (var item in found) events.Items.Add(new EventChoice(item));
        status.Text = $"Leitura confirmada pela API em {day:dd/MM/yyyy}: {found.Count} evento(s). " +
                      "Selecione um evento fictício para editar ou excluir; cada escrita pede confirmação.";
    }

    private async Task EditEvent(GoogleEventSnapshot? original)
    {
        if (busy || !oauth.CanWriteSelectedCalendar) return;
        if (original?.SpansMultipleDays == true)
        {
            MessageBox.Show(this, "Este teste não edita eventos de vários dias.", "Evento protegido");
            return;
        }
        using var dialog = new GoogleCalendarEventDialog(original, DateOnly.FromDateTime(date.Value));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Run(async () =>
        {
            string result;
            if (dialog.Action == GoogleEventUserAction.Delete && original is not null)
            {
                await api.Delete(original);
                result = "Exclusão confirmada pela API Google.";
            }
            else if (dialog.Action == GoogleEventUserAction.Save && dialog.Draft is { } draft)
            {
                var saved = original is null ? await api.Create(draft) : await api.Update(original, draft);
                date.Value = saved.Date.ToDateTime(TimeOnly.MinValue);
                result = original is null ? "Criação confirmada pela API Google." : "Edição confirmada pela API Google.";
            }
            else return;
            await LoadEvents();
            status.Text = result + " " + status.Text;
        });
    }

    private sealed record EventChoice(GoogleEventSnapshot Event)
    {
        public override string ToString() => $"{Event.Time} • {Event.Title}";
    }
}
