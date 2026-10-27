namespace CentralWhatsApp.WebView2;

/// <summary>Fila assistida: cada conversa exige revisão; nunca aciona o botão de envio.</summary>
internal sealed class AgendaConfirmationDialog : Form
{
    private readonly AgendaWorkflowStore store;
    private readonly string company;
    private readonly Func<AgendaWorkItem, Task> validateSchedule;
    private readonly Func<ComposeRequest, Task<bool>> openDraft;
    private readonly Func<DateOnly> today;
    private readonly Func<string, bool> confirmSend;
    private readonly HashSet<string> deferred = new(StringComparer.Ordinal);
    private readonly Panel details = new() { Dock = DockStyle.Fill };
    private readonly ListBox list = new() { Dock = DockStyle.Top, Height = 170, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 64, Padding = new Padding(12, 8, 12, 0) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12, 6, 12, 0) };
    private readonly Button prepare = new() { Text = "Revisar e abrir conversa", AutoSize = true };
    private readonly Button sentNext = new() { Text = "Enviei e próxima", AutoSize = true };
    private readonly Button skip = new() { Text = "Pular por enquanto", AutoSize = true };
    private readonly Button expand = new() { Text = "Ver fila", AutoSize = true, Visible = false };
    private readonly Button refresh = new() { Text = "Atualizar", AutoSize = true };
    private readonly Button editPhone = new() { Text = "Editar WhatsApp", AutoSize = true };
    private readonly Button restoreSkipped = new() { Text = "Rever pulados", AutoSize = true };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), WrapContents = true };
    private AgendaConfirmationRow? openedRow;
    private AgendaMessageJob? openedJob;
    private bool busy, compact;
    private Rectangle expandedBounds;

    internal AgendaConfirmationDialog(AgendaWorkflowStore store, string company,
        Func<AgendaWorkItem, Task> validateSchedule, Func<ComposeRequest, Task<bool>> openDraft,
        Func<DateOnly>? today = null, Func<string, bool>? confirmSend = null)
    {
        this.store = store; this.company = company; this.validateSchedule = validateSchedule; this.openDraft = openDraft;
        this.today = today ?? (() => DateOnly.FromDateTime(AgendaCalendarLayout.Now));
        this.confirmSend = confirmSend ?? (client => MessageBox.Show(this,
            "Você confirma que enviou manualmente a mensagem para " + client +
            "?\nO registro não comprova entrega e não confirma o serviço pelo cliente.", "Registrar envio e avançar",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        Text = "Serviços a confirmar • MODUX";
        Width = 900; Height = 660; MinimumSize = new Size(740, 530);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 5, 10, 5) };
        period.Items.AddRange(["Hoje e amanhã", "Hoje", "Amanhã"]); period.SelectedIndex = 0;
        filters.Controls.AddRange([period, refresh, restoreSkipped, editPhone]);
        var hint = new Label { Dock = DockStyle.Top, Height = 55, Padding = new Padding(12, 5, 12, 0),
            Text = "Atendimentos cadastrados na equipe, ainda não confirmados. Eventos só do Google precisam de cadastro/vínculo.\nCada cliente recebe seu próprio texto e link. Revise e envie no WhatsApp." };
        var previewTitle = new Label { Text = "Prévia — o link exclusivo será acrescentado ao abrir a conversa", Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 5, 0, 0) };
        details.Controls.Add(preview); details.Controls.Add(previewTitle); details.Controls.Add(list);
        details.Controls.Add(filters); details.Controls.Add(hint);
        actions.Controls.AddRange([prepare, sentNext, skip, expand]);
        Controls.Add(details); Controls.Add(status); Controls.Add(actions); Controls.Add(summary);
        list.SelectedIndexChanged += (_, _) => UpdateSelection();
        period.SelectedIndexChanged += (_, _) => Populate();
        refresh.Click += async (_, _) => await Run(LoadItems);
        restoreSkipped.Click += (_, _) => { deferred.Clear(); Populate(); };
        editPhone.Click += async (_, _) =>
        {
            if (busy || list.SelectedItem is not AgendaConfirmationRow row) return;
            using var prompt = new Form { Text = "WhatsApp do cliente • " + row.Work.Appointment.Client,
                Width = 480, Height = 190, StartPosition = FormStartPosition.CenterParent, Font = Font };
            var input = new TextBox { Dock = DockStyle.Top, Text = row.Work.Appointment.Phone, MaxLength = 30, PlaceholderText = "DDD + número do WhatsApp" };
            var save = new Button { Text = "Salvar WhatsApp", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
            var hint = new Label { Text = "Informe o telefone com DDD. Salvar não envia mensagem.", Dock = DockStyle.Top, Height = 42, Padding = new Padding(10) };
            prompt.Controls.Add(input); prompt.Controls.Add(hint); prompt.Controls.Add(save); prompt.AcceptButton = save;
            if (prompt.ShowDialog(this) != DialogResult.OK) return;
            await Run(async () =>
            {
                await store.UpdatePhone(company, row.Work.Appointment, input.Text); EnsureActive();
                openedRow = null; openedJob = null;
                await LoadItems();
            });
        };
        prepare.Click += async (_, _) => await Run(OpenSelected);
        sentNext.Click += async (_, _) => await Run(MarkAndNext);
        skip.Click += async (_, _) => await Run(async () =>
        {
            if (list.SelectedItem is not AgendaConfirmationRow row) return;
            deferred.Add(row.Key); openedRow = null; openedJob = null;
            Populate();
            if (compact) await OpenSelected();
        });
        expand.Click += (_, _) => SetCompact(false);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) => await Run(LoadItems);
        UpdateSelection();
    }

    private async Task Run(Func<Task> operation)
    {
        if (busy || IsDisposed) return;
        busy = true; UpdateControls();
        try { await operation(); }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (IsDisposed) return;
            SetCompact(false);
            status.Text = "Não foi possível avançar: " + error.Message;
            Show(); Activate();
        }
        finally { busy = false; if (!IsDisposed) UpdateControls(); }
    }

    private void EnsureActive()
    {
        if (IsDisposed) throw new InvalidOperationException("A fila foi encerrada.");
    }

    private async Task LoadItems()
    {
        status.Text = "Conferindo os atendimentos e o histórico compartilhado...";
        await store.Load(company, today()); EnsureActive(); Populate();
    }

    private void Populate(string? selectedKey = null)
    {
        selectedKey ??= (list.SelectedItem as AgendaConfirmationRow)?.Key;
        var rows = AgendaConfirmationQueue.Build(store.Appointments, store.Jobs, today(), period.SelectedIndex, deferred);
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var row in rows) list.Items.Add(row);
            var selected = rows.FirstOrDefault(r => r.Key == selectedKey);
            if (selected is not null) list.SelectedItem = selected;
            else if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        finally { list.EndUpdate(); }
        status.Text = $"{rows.Count} serviço(s) na fila • {deferred.Count} pulado(s) nesta sessão. " +
            "Abrir conversa não confirma envio nem o atendimento.";
        if (rows.Count == 0) SetCompact(false);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        preview.Clear();
        if (list.SelectedItem is AgendaConfirmationRow row)
        {
            var a = row.Work.Appointment;
            summary.Text = $"{a.Client} • {DateOnly.Parse(a.Date):dd/MM/yyyy} às {a.Time}\n{a.Service} • WhatsApp: {a.Phone}";
            if (store.Templates.TryGetValue("confirmacao", out var template))
            {
                try { preview.Text = AgendaWorkflowRules.Message(row.Work, template).Text; }
                catch (InvalidOperationException error) { preview.Text = error.Message; }
            }
            else preview.Text = "Modelo de confirmação não configurado para esta equipe.";
        }
        else summary.Text = "Nenhum serviço a confirmar neste período.\nUse Atualizar ou Rever pulados para conferir novamente.";
        UpdateControls();
    }

    private void UpdateControls()
    {
        list.Enabled = period.Enabled = refresh.Enabled = restoreSkipped.Enabled = expand.Enabled = !busy;
        var row = list.SelectedItem as AgendaConfirmationRow;
        prepare.Enabled = !busy && row?.HasPhone == true && store.Templates.ContainsKey("confirmacao");
        sentNext.Enabled = !busy && openedJob is not null && row?.Key == openedRow?.Key;
        skip.Enabled = !busy && row is not null;
        restoreSkipped.Enabled = !busy && deferred.Count > 0;
        editPhone.Enabled = !busy && row is not null;
    }

    private async Task OpenSelected()
    {
        if (list.SelectedItem is not AgendaConfirmationRow selected) return;
        openedRow = null; openedJob = null;
        // Releitura por identidade e revisão antes de cada conversa, sem preparar a fila em lote.
        await store.Load(company, today()); EnsureActive(); Populate(selected.Key);
        var row = list.Items.Cast<AgendaConfirmationRow>().FirstOrDefault(r => r.Key == selected.Key);
        if (row is null) throw new InvalidOperationException("Este atendimento já foi tratado ou mudou de horário/situação. Confira a fila atualizada.");
        list.SelectedItem = row;
        if (!store.Templates.TryGetValue("confirmacao", out var template))
            throw new InvalidOperationException("Configure o modelo de confirmação da equipe.");
        var message = AgendaWorkflowRules.Message(row.Work, template);
        await validateSchedule(row.Work); EnsureActive();
        var job = await store.Prepare(company, row.Work); EnsureActive();
        if (job.State is not ("preparada" or "erro"))
            throw new InvalidOperationException("Esta confirmação já foi tratada. Atualize a fila.");
        var token = await store.IssueResponseToken(company, row.Work); EnsureActive();
        message = AgendaWorkflowRules.WithResponseLink(message, token);
        Hide();
        var opened = await openDraft(message);
        if (IsDisposed) return;
        if (opened) { openedRow = row; openedJob = job; SetCompact(true); }
        else SetCompact(false);
        Show(); Activate();
        status.Text = opened ? "Envie no WhatsApp. Depois clique em Enviei e próxima. O MODUX não verifica entrega." :
            "Conversa não aberta. A confirmação continua pendente; nenhum envio foi registrado.";
    }

    private async Task MarkAndNext()
    {
        if (openedRow is null || openedJob is null || (list.SelectedItem as AgendaConfirmationRow)?.Key != openedRow.Key) return;
        if (!confirmSend(openedRow.Work.Appointment.Client)) return;
        await store.Mark(company, openedJob, "enviada_manual"); EnsureActive();
        openedRow = null; openedJob = null;
        await LoadItems();
        if (list.Items.Count > 0) await OpenSelected();
    }

    private void SetCompact(bool value)
    {
        if (value == compact) return;
        if (value) expandedBounds = Bounds;
        compact = value;
        details.Visible = !value; expand.Visible = value;
        prepare.Visible = !value;
        MinimumSize = value ? new Size(740, 220) : new Size(740, 530);
        if (value)
        {
            var area = Screen.FromControl(Owner ?? this).WorkingArea;
            Bounds = new Rectangle(area.Right - 760, area.Bottom - 245, 750, 235);
        }
        else if (!expandedBounds.IsEmpty) Bounds = expandedBounds;
    }
}
