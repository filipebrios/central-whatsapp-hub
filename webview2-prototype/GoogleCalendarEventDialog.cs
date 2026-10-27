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
    private bool linked;
    private readonly TextBox phone = new() { Dock = DockStyle.Fill, MaxLength = 30, PlaceholderText = "DDD + número do WhatsApp" };
    private readonly Button savePhone = Button("Salvar WhatsApp");
    private readonly Label contactStatus = new() { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(20, 3, 20, 0), AutoEllipsis = true };
    private bool savingContact;
    private string phoneSavedValue = "";
    internal bool ContactSaved { get; private set; }
    private readonly CheckedListBox serviceType = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private bool serviceTypeChanged;

    internal GoogleEventUserAction Action { get; private set; }
    internal GoogleEventDraft? Draft { get; private set; }

    internal GoogleCalendarEventDialog(GoogleEventSnapshot? source, DateOnly initialDay, bool linkedAppointment = false,
        IEnumerable<GoogleEventTypeChoice>? eventTypes = null, IReadOnlyList<string>? originalServices = null,
        string? originalPhone = null, Func<string, string[], Task>? saveContact = null)
    {
        original = source;
        linked = linkedAppointment;
        Text = source is null ? "Criar evento no Google Agenda" : "Editar evento do Google Agenda";
        Width = 720; Height = 800; MinimumSize = new Size(620, 700);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        var selectedTypes = originalServices ?? source?.ServiceTypeIds ?? [];
        var types = eventTypes?.ToList() ?? [];
        foreach (var id in selectedTypes)
            if (!types.Any(type => type.Id == id)) types.Add(new GoogleEventTypeChoice(id, id, null));
        foreach (var type in types) serviceType.Items.Add(type, selectedTypes.Contains(type.Id));
        serviceType.Enabled = types.Count > 0;
        serviceType.ItemCheck += (_, _) => serviceTypeChanged = true;

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
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9,
            Padding = new Padding(24, 16, 24, 14), AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Título *", title, 48);
        phone.Text = originalPhone ?? "";
        phoneSavedValue = phone.Text;
        var contactRow = new Panel { Dock = DockStyle.Fill };
        savePhone.Dock = DockStyle.Right;
        savePhone.AutoSize = false; savePhone.Width = 150; savePhone.Height = 34;
        contactRow.Controls.Add(phone); contactRow.Controls.Add(savePhone);
        contactRow.Enabled = source is not null && saveContact is not null && !source.AllDay;
        AddRow(layout, 1, "WhatsApp do cliente", contactRow, 42);
        AddRow(layout, 2, "Endereço / local", location, 42);
        AddRow(layout, 3, "Data", date, 42);
        AddRow(layout, 4, "Hora", time, 42);
        AddRow(layout, 5, "Duração (min)", minutes, 42);
        AddRow(layout, 6, "Período", allDay, 42);
        AddRow(layout, 7, "Serviços / tipo de evento\nMarque um ou mais", serviceType, 110);
        AddRow(layout, 8, "Observações", description, 90);

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
        contactStatus.Text = source is null ? "Para cadastrar cliente com WhatsApp, use + Agendamento, ou salve o evento e abra-o novamente." :
            "Salvar WhatsApp cadastra o contato deste atendimento para as confirmações. Não envia mensagem.";
        savePhone.Click += async (_, _) =>
        {
            if (savingContact || saveContact is null) return;
            var normalized = ContactPhone.Normalize(phone.Text);
            if (normalized is null) { contactStatus.Text = "Informe um WhatsApp válido com DDD."; phone.Focus(); return; }
            savingContact = true; layout.Enabled = footer.Enabled = false;
            try
            {
                await saveContact(normalized, serviceType.CheckedItems.Cast<GoogleEventTypeChoice>().Select(type => type.Id).ToArray());
                if (IsDisposed) return;
                phone.Text = phoneSavedValue = normalized; ContactSaved = true; linked = true;
                contactStatus.Text = "WhatsApp salvo. Use Serviços a confirmar para revisar e enviar a mensagem.";
            }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            { if (!IsDisposed) contactStatus.Text = "Contato não salvo: " + error.Message; }
            finally { savingContact = false; if (!IsDisposed) layout.Enabled = footer.Enabled = true; }
        };
        FormClosing += (_, e) => { if (savingContact) e.Cancel = true; };

        if (source is not null)
        {
            var delete = Button("Excluir do Google");
            delete.BackColor = Color.FromArgb(168, 42, 42);
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this,
                    linked
                        ? "Excluir este evento do Google e marcar o atendimento vinculado como cancelado? O cadastro e histórico serão preservados."
                        : "Excluir este evento do Google Agenda? Essa ação removerá o compromisso do calendário original.",
                    "Confirmar exclusão", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
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
        Controls.Add(layout); Controls.Add(contactStatus); Controls.Add(hint); Controls.Add(footer);
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
        if (phone.Enabled && phone.Text != phoneSavedValue)
        { contactStatus.Text = "Clique em Salvar WhatsApp para confirmar a alteração do telefone antes de salvar o evento."; phone.Focus(); return; }
        var day = DateOnly.FromDateTime(date.Value);
        var clock = TimeOnly.FromDateTime(time.Value);
        var proposal = new GoogleEventDraft(
            title.Text.Trim(), description.Text.Trim(), location.Text.Trim(), day, clock,
            allDay.Checked ? 1440 : (int)minutes.Value, allDay.Checked);
        if (serviceTypeChanged)
        {
            var selected = serviceType.CheckedItems.Cast<GoogleEventTypeChoice>().ToArray();
            proposal = proposal with { ServiceTypeIds = selected.Select(type => type.Id).ToArray(), SelectedColorId = selected.FirstOrDefault()?.ColorId };
        }
        try
        {
            // Não enviar nada ao Google antes da confirmação do usuário.
            _ = proposal.GooglePayload();
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
                : linked ? "Salvar as alterações no Google Agenda e atualizar o atendimento vinculado agora?"
                    : "Salvar as alterações deste compromisso no Google Agenda agora?",
            "Confirmar alteração no Google",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Draft = proposal;
        Action = GoogleEventUserAction.Save;
        DialogResult = DialogResult.OK;
    }
}
