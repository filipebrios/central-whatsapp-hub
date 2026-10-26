namespace CentralWhatsApp.WebView2;

internal enum GoogleEventUserAction { None, Save, Delete }

/// <summary>Formulário Google: usuário confirma uma escrita antes da API alterar a agenda.</summary>
internal sealed class GoogleCalendarEventDialog : Form
{
    private readonly TextBox title = new() { Dock = DockStyle.Top };
    private readonly TextBox location = new() { Dock = DockStyle.Top };
    private readonly TextBox description = new()
    { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly DateTimePicker date = new()
    { Format = DateTimePickerFormat.Short, Dock = DockStyle.Top };
    private readonly DateTimePicker time = new()
    { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Dock = DockStyle.Top };
    private readonly NumericUpDown minutes = new()
    { Minimum = 15, Maximum = 1440, Increment = 15, Value = 60, Dock = DockStyle.Top };
    private readonly CheckBox allDay = new()
    { Text = "Dia inteiro", Dock = DockStyle.Top, AutoSize = true };
    private readonly GoogleEventSnapshot? original;

    internal GoogleEventUserAction Action { get; private set; }
    internal GoogleEventDraft? Draft { get; private set; }

    internal GoogleCalendarEventDialog(GoogleEventSnapshot? source, DateOnly initialDay, bool linkedAppointment = false)
    {
        original = source;
        Text = source is null ? "Criar evento no Google Agenda" : "Editar evento do Google Agenda";
        Width = 680; Height = 660; MinimumSize = new Size(540, 590);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;

        if (source is not null)
        {
            title.Text = source.Title;
            location.Text = source.Location;
            description.Text = source.Description;
            initialDay = source.Date;
            allDay.Checked = source.AllDay;
            minutes.Value = Math.Clamp(source.Duration, 15, 1440);
            if (!source.AllDay && TimeOnly.TryParse(source.Time, out var startAt))
                time.Value = DateTime.Today.Add(startAt.ToTimeSpan());
        }
        else time.Value = DateTime.Today.AddHours(8);
        date.Value = initialDay.ToDateTime(TimeOnly.MinValue);
        allDay.CheckedChanged += (_, _) =>
        {
            time.Enabled = minutes.Enabled = !allDay.Checked;
        };
        time.Enabled = minutes.Enabled = !allDay.Checked;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7,
            Padding = new Padding(24, 16, 24, 14), AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Título *", title, 48);
        AddRow(layout, 1, "Endereço / local", location, 48);
        AddRow(layout, 2, "Data", date, 48);
        AddRow(layout, 3, "Hora", time, 48);
        AddRow(layout, 4, "Duração (min)", minutes, 48);
        AddRow(layout, 5, "Período", allDay, 48);
        AddRow(layout, 6, "Observações", description, 135);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(12, 9, 12, 3),
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        var save = Button(source is null ? "Criar no Google" : "Salvar no Google");
        var cancel = Button("Voltar");
        cancel.BackColor = Color.FromArgb(83, 96, 107);
        footer.Controls.Add(save); footer.Controls.Add(cancel);
        save.Click += (_, _) => Submit();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        if (source is not null)
        {
            var delete = Button("Excluir do Google");
            delete.BackColor = Color.FromArgb(168, 42, 42);
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this,
                    linkedAppointment
                        ? "Excluir este evento do Google e marcar o atendimento vinculado como cancelado? O cadastro e histórico serão preservados."
                        : "Excluir este evento do Google Agenda? Essa ação removerá o compromisso do calendário original.",
                    "Confirmar exclusão", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
                Action = GoogleEventUserAction.Delete;
                DialogResult = DialogResult.OK;
            };
            footer.Controls.Add(delete);
        }
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 51, Padding = new Padding(20, 4, 20, 4),
            ForeColor = Color.FromArgb(76, 94, 103), Font = new Font("Segoe UI", 8.5f),
            Text = source?.RecurringEventId is { Length: > 0 }
                ? "Evento recorrente: alterações serão aplicadas SOMENTE a esta ocorrência. Convidados não serão notificados automaticamente."
                : "Somente o calendário Google selecionado será alterado; eventos e lembretes do WhatsApp não serão enviados."
        };
        Controls.Add(layout); Controls.Add(hint); Controls.Add(footer);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel container, int index, string label, Control child, int height)
    {
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        container.Controls.Add(new Label
        {
            Text = label, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, index);
        container.Controls.Add(child, 1, index);
    }

    private static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, Height = 37,
        Padding = new Padding(13, 5, 13, 5),
        Margin = new Padding(7, 2, 7, 2),
        BackColor = Color.FromArgb(0, 103, 44), ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    private void Submit()
    {
        var day = DateOnly.FromDateTime(date.Value);
        var clock = TimeOnly.FromDateTime(time.Value);
        var proposal = new GoogleEventDraft(
            title.Text.Trim(), description.Text.Trim(), location.Text.Trim(), day, clock,
            allDay.Checked ? 1440 : (int)minutes.Value, allDay.Checked);
        try
        {
            // Não enviar nada ao Google antes da confirmação do usuário.
            _ = proposal.AsGoogleFields();
        }
        catch (InvalidOperationException e)
        {
            MessageBox.Show(this, e.Message, "Revisar dados", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this,
            original is null
                ? "Criar este compromisso no Google Agenda agora?"
                : "Salvar as alterações deste compromisso no Google Agenda agora?",
            "Confirmar alteração no Google",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Draft = proposal;
        Action = GoogleEventUserAction.Save;
        DialogResult = DialogResult.OK;
    }
}
