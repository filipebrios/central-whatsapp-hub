namespace CentralWhatsApp.WebView2;

internal sealed record AgendaAppointmentInput(
    string Kind, string Client, string Responsible, string Phone,
    string Email, string Address, string[] ServiceIds, string PrimaryService,
    string Technician, string Notes, string Date, string Time, int Duration);

/// <summary>Cadastro PF/PJ com múltiplos serviços, sem enviar mensagens ou criar evento Google.</summary>
internal sealed class AgendaAppointmentDialog : Form
{
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly TextBox client = new() { Dock = DockStyle.Top, PlaceholderText = "Nome completo ou razão social" };
    private readonly TextBox responsible = new() { Dock = DockStyle.Top, PlaceholderText = "Responsável no local (opcional)" };
    private readonly TextBox phone = new() { Dock = DockStyle.Top, PlaceholderText = "(35) 99999-9999" };
    private readonly TextBox email = new() { Dock = DockStyle.Top, PlaceholderText = "E-mail (opcional)" };
    private readonly TextBox address = new() { Dock = DockStyle.Top, PlaceholderText = "Rua, número, bairro e cidade" };
    private readonly CheckedListBox services = new()
    {
        Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false
    };
    private readonly TextBox otherService = new() { Dock = DockStyle.Top, PlaceholderText = "Outro serviço (opcional)" };
    private readonly DateTimePicker day = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Top };
    private readonly DateTimePicker time = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm",
        ShowUpDown = true, Dock = DockStyle.Top
    };
    private readonly NumericUpDown duration = new()
    {
        Minimum = 15, Maximum = 1440, Increment = 15, Value = 60, Dock = DockStyle.Top
    };
    private readonly TextBox technician = new() { Dock = DockStyle.Top, PlaceholderText = "Técnico ou equipe (opcional)" };
    private readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button save = MakeButton("Salvar na Agenda");
    private readonly Button cancel = MakeButton("Cancelar");

    private static readonly (string Id, string Label)[] Catalog =
    [
        ("desinsetizacao", "Desinsetização"),
        ("desratizacao", "Desratização"),
        ("descupinizacao", "Descupinização"),
        ("reservatorio", "Higienização de reservatório"),
        ("caixa_gordura", "Limpeza de caixa de gordura"),
        ("sanitizacao", "Sanitização"),
        ("capina_quimica", "Capina química"),
        ("orcamento", "Visita para orçamento"),
        ("confirmacao", "Confirmação / vistoria"),
        ("interno", "Serviço interno")
    ];

    internal AgendaAppointmentInput? Result { get; private set; }

    internal AgendaAppointmentDialog(DateOnly initialDay)
    {
        Text = "Novo agendamento • São Bento";
        Width = 680; Height = 765;
        MinimumSize = new Size(570, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);
        kind.Items.AddRange(["Pessoa física", "Pessoa jurídica"]);
        kind.SelectedIndex = 0;
        kind.SelectedIndexChanged += (_, _) =>
        {
            client.PlaceholderText = kind.SelectedIndex == 1 ? "Razão social da empresa" : "Nome completo do cliente";
        };
        foreach (var svc in Catalog) services.Items.Add(svc.Label);
        day.Value = initialDay.ToDateTime(new TimeOnly(8, 0));
        time.Value = DateTime.Today.AddHours(8);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 16),
            ColumnCount = 2, RowCount = 13, AutoScroll = true
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(fields, 0, "Tipo de cliente", kind, 46);
        AddRow(fields, 1, "Cliente / empresa *", client, 46);
        AddRow(fields, 2, "Responsável", responsible, 46);
        AddRow(fields, 3, "WhatsApp *", phone, 46);
        AddRow(fields, 4, "E-mail", email, 46);
        AddRow(fields, 5, "Endereço *", address, 58);
        AddRow(fields, 6, "Serviços *", services, 130);
        AddRow(fields, 7, "Outro serviço", otherService, 46);
        AddRow(fields, 8, "Data", day, 46);
        AddRow(fields, 9, "Hora", time, 46);
        AddRow(fields, 10, "Duração (min)", duration, 46);
        AddRow(fields, 11, "Técnico / equipe", technician, 46);
        AddRow(fields, 12, "Observações", notes, 94);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 65,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 9, 10, 5)
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 29, AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Salva no Supabase. Não envia WhatsApp e não cria evento no Google Agenda.",
            ForeColor = Color.FromArgb(78, 96, 81), Font = new Font("Segoe UI", 8)
        };
        Controls.Add(fields); Controls.Add(hint); Controls.Add(buttons);
        save.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static Button MakeButton(string label) => new()
    {
        Text = label, AutoSize = true, Height = 36, Padding = new Padding(13, 4, 13, 4),
        Margin = new Padding(8, 3, 8, 3), FlatStyle = FlatStyle.Flat,
        ForeColor = Color.White, BackColor = Color.FromArgb(0, 103, 44)
    };

    private static void AddRow(TableLayoutPanel table, int index, string text, Control field, int height)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(new Label
        {
            Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 6, 0)
        }, 0, index);
        table.Controls.Add(field, 1, index);
    }

    private void Confirm()
    {
        var selected = Catalog.Where((_, i) => services.GetItemChecked(i)).ToList();
        var custom = otherService.Text.Trim();
        if (string.IsNullOrWhiteSpace(client.Text) || string.IsNullOrWhiteSpace(phone.Text) ||
            string.IsNullOrWhiteSpace(address.Text) || (selected.Count == 0 && custom.Length == 0))
        {
            MessageBox.Show(this, "Preencha cliente, WhatsApp, endereço e ao menos um serviço.",
                "Agenda São Bento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(email.Text) &&
            (!email.Text.Contains('@') || !email.Text.Split('@').Last().Contains('.')))
        {
            MessageBox.Show(this, "Confira o e-mail ou deixe o campo vazio.",
                "Agenda São Bento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (custom.Length > 120 || notes.Text.Length > 4000)
        {
            MessageBox.Show(this, "O texto de serviço personalizado ou observações está muito longo.",
                "Agenda São Bento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var ids = selected.Select(x => x.Id).ToList();
        if (custom.Length > 0) ids.Add("outro");
        Result = new AgendaAppointmentInput(
            kind.SelectedIndex == 1 ? "pj" : "pf",
            client.Text.Trim(), responsible.Text.Trim(), phone.Text.Trim(), email.Text.Trim(),
            address.Text.Trim(), ids.ToArray(),
            string.Join(" + ", selected.Select(x => x.Label).Concat(custom.Length > 0 ? [custom] : [])),
            technician.Text.Trim(), notes.Text.Trim(),
            day.Value.ToString("yyyy-MM-dd"), time.Value.ToString("HH:mm:ss"), (int)duration.Value);
        DialogResult = DialogResult.OK;
    }
}
