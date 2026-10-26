namespace CentralWhatsApp.WebView2;

internal sealed class AgendaWorkflowDialog : Form
{
    private readonly AgendaWorkflowStore store;
    private readonly string company;
    private readonly bool admin;
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Button refresh = new() { Text = "Atualizar", AutoSize = true };
    private readonly Button prepare = new() { Text = "Revisar no WhatsApp", AutoSize = true };
    private readonly Button sent = new() { Text = "Confirmar envio manual", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancelar pendência", AutoSize = true };
    private readonly Button edit = new() { Text = "Editar cadastro", AutoSize = true };
    private readonly Button feedback = new() { Text = "Registrar nota NPS", AutoSize = true };
    private readonly Button reviewModel = new() { Text = "Link de avaliações Google", AutoSize = true };
    private readonly CheckBox showHandled = new() { Text = "Mostrar tratadas", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 65, Padding = new Padding(12) };
    private bool busy;
    internal ComposeRequest? Prepared { get; private set; }
    internal AgendaLocalSnapshot? EditRequested { get; private set; }

    internal AgendaWorkflowDialog(AgendaWorkflowStore store, string company, bool admin)
    {
        this.store = store; this.company = company; this.admin = admin;
        Text = "Pendências, lembretes e pós-venda • São Bento";
        Width = 1050; Height = 620; MinimumSize = new Size(830, 480);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        var heading = new Label
        {
            Dock = DockStyle.Top, Height = 92, Padding = new Padding(14),
            Text = "Rotina da equipe\nConfirmações e lembretes de hoje/amanhã; revisão de cadastros e pós-venda após 7 dias.\n" +
                "O MODUX prepara rascunhos. O envio é manual; abrir o WhatsApp não marca mensagem como enviada."
        };
        var actions = new FlowLayoutPanel
        { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) };
        actions.Controls.AddRange([refresh, prepare, sent, cancel, edit, feedback, reviewModel, showHandled]);
        Controls.Add(list); Controls.Add(actions); Controls.Add(heading); Controls.Add(status);
        list.SelectedIndexChanged += (_, _) => UpdateControls();
        showHandled.CheckedChanged += (_, _) => Populate();
        refresh.Click += async (_, _) => await Run(LoadItems);
        prepare.Click += async (_, _) =>
        {
            await Run(async () =>
            {
                if (list.SelectedItem is not Row row || !store.Templates.TryGetValue(row.Work.TemplateKey, out var template)) return;
                var message = AgendaWorkflowRules.Message(row.Work, template);
                var job = await store.Prepare(company, row.Work);
                if (job.State is "enviada_manual" or "tratada_manual" or "cancelada")
                    throw new InvalidOperationException("Essa ocorrência já foi tratada. Atualize para conferir o registro.");
                Prepared = message;
            });
            if (Prepared is not null) DialogResult = DialogResult.OK;
        };
        sent.Click += async (_, _) => await Run(async () =>
        {
            if (list.SelectedItem is not Row { Job: { } job }) return;
            if (MessageBox.Show(this, "Você confirma que enviou esta mensagem manualmente no WhatsApp? " +
                "Este registro é uma declaração do operador, não uma confirmação automática de entrega.",
                "Confirmar envio manual", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await store.Mark(company, job, "enviada_manual"); await LoadItems();
        });
        cancel.Click += async (_, _) => await Run(async () =>
        {
            if (list.SelectedItem is not Row { Job: { } job }) return;
            if (MessageBox.Show(this, "Cancelar esta pendência de mensagem? O atendimento não será cancelado.",
                "Cancelar pendência", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await store.Mark(company, job, "cancelada"); await LoadItems();
        });
        edit.Click += (_, _) =>
        {
            if (list.SelectedItem is not Row row || busy) return;
            EditRequested = row.Work.Appointment; DialogResult = DialogResult.OK;
        };
        feedback.Click += async (_, _) => await RegisterFeedback();
        reviewModel.Click += async (_, _) => await ConfigureReview();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) => await Run(LoadItems);
        UpdateControls();
    }

    private async Task Run(Func<Task> operation)
    {
        if (busy) return;
        busy = true; UpdateControls();
        try { await operation(); }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            status.Text = "Rotina não concluída: " + error.Message;
            MessageBox.Show(this, error.Message, "Conferir rotina", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { busy = false; UpdateControls(); }
    }

    private async Task LoadItems()
    {
        status.Text = "Conferindo dados e registros compartilhados...";
        await store.Load(company, DateOnly.FromDateTime(DateTime.Today));
        Populate();
    }

    private void Populate()
    {
        list.Items.Clear();
        foreach (var work in AgendaWorkflowRules.Plan(store.Appointments, DateOnly.FromDateTime(DateTime.Today), showHandled.Checked))
        {
            var job = store.Jobs.FirstOrDefault(j => j.AppointmentId == work.Appointment.Id &&
                j.TemplateKey == work.TemplateKey && j.Occurrence == work.Occurrence);
            if (!showHandled.Checked && job?.State is "enviada_manual" or "tratada_manual" or "cancelada") continue;
            var display = work.TemplateKey.Length > 0 && !store.Templates.ContainsKey(work.TemplateKey)
                ? work with { Label = work.Label + " • configurar modelo/link" } : work;
            list.Items.Add(new Row(display, job));
        }
        status.Text = $"{list.Items.Count} item(ns). Pós-venda: serviços concluídos nos últimos 90 dias e há pelo menos 7 dias. " +
            "Envios exigem revisão e ação manual. Cadastros sem contato válido ficam pendentes.";
        UpdateControls();
    }

    private void UpdateControls()
    {
        refresh.Enabled = showHandled.Enabled = list.Enabled = !busy;
        reviewModel.Enabled = !busy && admin;
        var row = list.SelectedItem as Row;
        var pending = row?.Job?.State is null or "preparada" or "erro";
        prepare.Enabled = !busy && row is not null && pending && row.Work.TemplateKey.Length > 0 && store.Templates.ContainsKey(row.Work.TemplateKey);
        sent.Enabled = cancel.Enabled = !busy && row?.Job is not null && pending;
        edit.Enabled = !busy && row is not null;
        feedback.Enabled = !busy && row?.Work.Appointment.Status == "concluido";
    }

    private async Task RegisterFeedback()
    {
        if (busy || list.SelectedItem is not Row row) return;
        using var prompt = new Form
        { Text = "Registrar retorno do cliente", Width = 540, Height = 330, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var score = new NumericUpDown { Minimum = -1, Maximum = 10, Value = row.Work.Appointment.FeedbackScore ?? -1, Dock = DockStyle.Top };
        var note = new TextBox { Multiline = true, MaxLength = 4000, Dock = DockStyle.Fill, Text = row.Work.Appointment.FeedbackNote };
        var save = new Button { Text = "Salvar nota informada pelo cliente", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK };
        var hint = new Label { Text = "Informe a nota de 0 a 10 e o comentário recebido. -1 significa nota ainda não informada.", Dock = DockStyle.Top, Height = 55, Padding = new Padding(10) };
        prompt.Controls.Add(note); prompt.Controls.Add(score); prompt.Controls.Add(hint); prompt.Controls.Add(save);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        await Run(async () =>
        {
            await store.SaveFeedback(company, row.Work.Appointment, (int)score.Value, note.Text);
            await LoadItems();
        });
    }

    private async Task ConfigureReview()
    {
        if (busy || !admin) return;
        using var prompt = new Form
        { Text = "Link oficial de avaliações da São Bento", Width = 640, Height = 210, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var url = new TextBox { Dock = DockStyle.Top, PlaceholderText = "https://...", MaxLength = 1200 };
        var save = new Button { Text = "Salvar convite para a equipe", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        var hint = new Label { Text = "Cole o link oficial de avaliações Google da São Bento. O convite fica disponível após registrar nota a partir de 8.", Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };
        prompt.Controls.Add(url); prompt.Controls.Add(hint); prompt.Controls.Add(save);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        await Run(async () => { await store.SaveReviewTemplate(company, url.Text.Trim()); await LoadItems(); });
    }

    private sealed record Row(AgendaWorkItem Work, AgendaMessageJob? Job)
    {
        public override string ToString() => Work + " • " + (Job?.State switch
        {
            "enviada_manual" => "envio confirmado pelo operador", "tratada_manual" => "tratada manualmente",
            "cancelada" => "pendência cancelada", "erro" => "erro / conferir",
            "preparada" => "rascunho preparado", _ => Work.TemplateKey.Length == 0 ? "cadastro incompleto" : "pendente"
        });
    }
}
