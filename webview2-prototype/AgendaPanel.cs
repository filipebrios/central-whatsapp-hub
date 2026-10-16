using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

/// <summary>Agenda em módulo isolado. Autorização por Supabase Auth e RLS.
/// Somente a chave PUBLICÁVEL fica no aplicativo. Tokens ficam apenas em memória.</summary>
internal sealed class AgendaPanel : UserControl
{
    private const string ApiRoot = "https://snypbekmjseayljvltwq.supabase.co/";
    private const string PublicKey = "sb_publishable_X7TR995xLWtmFxrZBT65MA_yNUaizrC";
    private readonly HttpClient http = new() { BaseAddress = new Uri(ApiRoot), Timeout = TimeSpan.FromSeconds(25) };
    private string? accessToken, refreshToken, companyId, currentUserId;
    private bool busy;

    private readonly Panel loginPane = new() { Dock = DockStyle.Fill };
    private readonly Panel appointmentsPane = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly TextBox email = new() { Width = 330, PlaceholderText = "E-mail autorizado" };
    private readonly TextBox password = new() { Width = 330, UseSystemPasswordChar = true, PlaceholderText = "Senha da Agenda" };
    private readonly Button signIn = MakeButton("Entrar na Agenda");
    private readonly Button signOut = MakeButton("Sair");
    private readonly Button refresh = MakeButton("Atualizar");
    private readonly Button add = MakeButton("+ Novo agendamento");
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 54, AutoEllipsis = true, ForeColor = Color.FromArgb(59, 74, 92), Padding = new Padding(12, 9, 12, 0) };
    private readonly DateTimePicker startDate = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 125 };
    private readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AutoGenerateColumns = false
    };

    private static Button MakeButton(string text) => new()
    {
        Text = text, AutoSize = true, Height = 35, Padding = new Padding(10, 4, 10, 4),
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 103, 44),
        ForeColor = Color.White, Margin = new Padding(5)
    };

    internal AgendaPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 10);
        var card = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Height = 350, Padding = new Padding(28),
            BackColor = Color.White
        };
        card.Controls.Add(new Label { Text = "Agenda Inteligente • São Bento",
            AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Margin = new Padding(5, 5, 5, 18) });
        card.Controls.Add(new Label
        {
            Text = "Acesso exclusivo para a equipe autorizada.",
            AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(5, 0, 5, 18)
        });
        card.Controls.Add(email); card.Controls.Add(password); card.Controls.Add(signIn);
        card.Controls.Add(new Label
        {
            Text = "É necessário convidar a conta no Supabase Auth e vinculá-la à São Bento.",
            AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(5, 15, 5, 0)
        });
        loginPane.Controls.Add(card);
        signIn.Click += async (_, _) => await Login();
        password.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Login(); }
        };

        period.Items.AddRange(["Hoje", "7 dias", "30 dias"]);
        period.SelectedIndex = 1;
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 65, WrapContents = false,
            Padding = new Padding(10, 13, 5, 5), BackColor = Color.White
        };
        bar.Controls.Add(new Label { Text = "A partir de:", AutoSize = true, Margin = new Padding(6, 10, 5, 0) });
        bar.Controls.Add(startDate); bar.Controls.Add(period);
        bar.Controls.Add(refresh); bar.Controls.Add(add); bar.Controls.Add(signOut);
        foreach (var (name, weight) in new (string Name, float Weight)[]
        {
            ("Data", 65), ("Hora", 45), ("Cliente", 145), ("Serviço", 115),
            ("Situação", 85), ("Telefone", 105), ("Endereço", 155)
        })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight });
        grid.ColumnHeadersHeight = 36; grid.RowTemplate.Height = 34;
        appointmentsPane.Controls.Add(grid); appointmentsPane.Controls.Add(bar);
        refresh.Click += async (_, _) => await LoadAppointments();
        startDate.ValueChanged += async (_, _) => { if (companyId is not null) await LoadAppointments(); };
        period.SelectedIndexChanged += async (_, _) => { if (companyId is not null) await LoadAppointments(); };
        add.Click += async (_, _) => await CreateAppointment();
        signOut.Click += (_, _) => Logout();
        Controls.Add(loginPane); Controls.Add(appointmentsPane); Controls.Add(status);
        Status("Agenda compartilhada. WhatsApp e Google Agenda não são sincronizados por esta versão.");
    }

    private void Status(string message) => status.Text = message;
    private void SetBusy(bool value)
    {
        busy = value;
        signIn.Enabled = refresh.Enabled = add.Enabled = signOut.Enabled = !value;
    }
    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private void ApplyTokens(JsonElement value)
    {
        accessToken = value.GetProperty("access_token").GetString();
        refreshToken = value.GetProperty("refresh_token").GetString();
    }
    private async Task Login()
    {
        if (busy) return;
        if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrEmpty(password.Text))
        { Status("Informe e-mail e senha."); return; }
        SetBusy(true);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/token?grant_type=password");
            request.Headers.Add("apikey", PublicKey);
            request.Content = Json(new { email = email.Text.Trim(), password = password.Text });
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("E-mail, senha ou convite inválidos.");
            using var session = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ApplyTokens(session.RootElement);
            currentUserId = session.RootElement.TryGetProperty("user", out var user)
                ? user.GetProperty("id").GetString() : null;
            using var memberResponse = await Authorized(HttpMethod.Get,
                "rest/v1/agenda_members?select=company_id,role&active=eq.true&limit=1");
            RequireSuccess(memberResponse);
            using var members = JsonDocument.Parse(await memberResponse.Content.ReadAsStringAsync());
            if (members.RootElement.GetArrayLength() == 0)
            {
                Logout();
                Status("Conta autenticada, mas sem permissão ativa para a Agenda São Bento.");
                return;
            }
            companyId = members.RootElement[0].GetProperty("company_id").GetString();
            loginPane.Visible = false;
            appointmentsPane.Visible = true;
            password.Clear();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Logout(); Status("Não foi possível entrar: " + e.Message); }
        finally { SetBusy(false); }
        if (companyId is not null) await LoadAppointments();
    }
    private void Logout()
    {
        accessToken = refreshToken = companyId = currentUserId = null;
        password.Clear();
        grid.Rows.Clear(); appointmentsPane.Visible = false; loginPane.Visible = true;
    }
    private async Task<bool> RenewToken()
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token");
            request.Headers.Add("apikey", PublicKey);
            request.Content = Json(new { refresh_token = refreshToken });
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ApplyTokens(doc.RootElement); return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return false; }
    }
    private async Task<HttpResponseMessage> Authorized(HttpMethod method, string path, object? data = null)
    {
        async Task<HttpResponseMessage> Send()
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("apikey", PublicKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (data is not null) request.Content = Json(data);
            return await http.SendAsync(request);
        }
        var response = await Send();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            if (!await RenewToken()) throw new InvalidOperationException("Sessão expirada. Entre novamente.");
            response = await Send();
        }
        return response;
    }
    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Operação não permitida para esta conta.",
            HttpStatusCode.Unauthorized => "Conta não autorizada.",
            _ => $"Falha de comunicação com a Agenda (HTTP {(int)response.StatusCode})."
        });
    }
    private async Task LoadAppointments()
    {
        if (busy || companyId is null) return;
        SetBusy(true);
        try
        {
            var first = DateOnly.FromDateTime(startDate.Value);
            var days = period.SelectedIndex switch { 0 => 1, 2 => 30, _ => 7 };
            var last = first.AddDays(days - 1);
            var query = "rest/v1/agenda_appointments?select=id,client,phone,primary_service,date,time,status,address" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) + "&deleted_at=is.null" +
                $"&date=gte.{first:yyyy-MM-dd}&date=lte.{last:yyyy-MM-dd}" +
                "&order=date.asc,time.asc&limit=500";
            using var response = await Authorized(HttpMethod.Get, query);
            RequireSuccess(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            grid.Rows.Clear();
            foreach (var appointment in doc.RootElement.EnumerateArray())
            {
                static string Read(JsonElement item, string key) =>
                    item.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
                var time = Read(appointment, "time");
                if (time.Length > 5) time = time[..5];
                grid.Rows.Add(Read(appointment, "date"), time, Read(appointment, "client"),
                    Read(appointment, "primary_service"), Read(appointment, "status"),
                    Read(appointment, "phone"), Read(appointment, "address"));
            }
            Status($"{doc.RootElement.GetArrayLength()} agendamentos • {first:dd/MM} a {last:dd/MM}. " +
                "Os dados da extensão antiga ainda não foram importados.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Status("Não foi possível atualizar: " + e.Message); }
        finally { SetBusy(false); }
    }
    private async Task CreateAppointment()
    {
        if (busy || companyId is null) return;
        using var dialog = new Form
        {
            Text = "Novo agendamento • São Bento", Width = 565, Height = 565,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White, Font = new Font("Segoe UI", 10)
        };
        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, AutoScroll = true,
            Padding = new Padding(14)
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var client = new TextBox { Dock = DockStyle.Top };
        var phone = new TextBox { Dock = DockStyle.Top };
        var emailAddress = new TextBox { Dock = DockStyle.Top };
        var address = new TextBox { Dock = DockStyle.Top };
        var service = new TextBox { Dock = DockStyle.Top };
        var date = new DateTimePicker { Dock = DockStyle.Top, Format = DateTimePickerFormat.Short };
        var time = new DateTimePicker
        { Dock = DockStyle.Top, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        var notes = new TextBox { Dock = DockStyle.Top, Multiline = true, Height = 65 };
        var items = new (string Label, Control Input)[]
        {
            ("Cliente *", client), ("Telefone *", phone), ("E-mail *", emailAddress),
            ("Endereço *", address), ("Serviço *", service), ("Data", date), ("Hora", time),
            ("Observações", notes)
        };
        for (var i = 0; i < items.Length; i++)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 7 ? 85 : 48));
            rows.Controls.Add(new Label { Text = items[i].Label, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            rows.Controls.Add(items[i].Input, 1, i);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 53,
            FlowDirection = FlowDirection.RightToLeft };
        var save = MakeButton("Salvar agendamento");
        var cancel = MakeButton("Cancelar");
        save.Click += (_, _) =>
        {
            if (new[] { client, phone, emailAddress, address, service }.Any(x => string.IsNullOrWhiteSpace(x.Text)))
            { MessageBox.Show(dialog, "Preencha os campos obrigatórios.", "Agenda São Bento"); return; }
            if (!emailAddress.Text.Contains('@'))
            { MessageBox.Show(dialog, "Confira o e-mail informado.", "Agenda São Bento"); return; }
            dialog.DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        dialog.Controls.Add(rows); dialog.Controls.Add(buttons);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            using var response = await Authorized(HttpMethod.Post, "rest/v1/agenda_appointments", new
            {
                company_id = companyId,
                kind = "pf",
                client = client.Text.Trim(), responsible = "",
                phone = phone.Text.Trim(), email = emailAddress.Text.Trim(),
                address = address.Text.Trim(), service_ids = Array.Empty<string>(),
                primary_service = service.Text.Trim(),
                date = date.Value.ToString("yyyy-MM-dd"), time = time.Value.ToString("HH:mm:ss"),
                duration = 60, status = "agendado", technician = "", notes = notes.Text.Trim(),
                source = "modux", created_by = currentUserId, updated_by = currentUserId
            });
            RequireSuccess(response);
            Status("Agendamento salvo na nuvem. Google Agenda e WhatsApp ainda não foram sincronizados.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Status("Não foi possível salvar: " + e.Message); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) http.Dispose();
        base.Dispose(disposing);
    }
}
