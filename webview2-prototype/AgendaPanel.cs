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
    private readonly AgendaMonthView calendar = new();
    private readonly GoogleCalendarFeed googleFeed = new();
    private readonly GoogleDesktopOAuth googleOAuth = new();
    private readonly GoogleCalendarApi googleApi;
    private readonly Button googleApiButton = MakeButton("Google (editar)");
    private readonly Button googleNewButton = MakeButton("+ Evento Google");
    private readonly Button googleButton = MakeButton("Conectar Google Agenda");
    private readonly System.Windows.Forms.Timer googleRefreshTimer = new() { Interval = 15 * 60 * 1000 };
    private int googleEventCount;
    private int supabaseEventCount;
    private readonly List<(string[] Values, AgendaServiceCategory Category, GoogleEventSnapshot? Google)> cachedRows = [];
    private DateOnly? filterDate;
    private bool suspendRefresh;
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
        googleApi = new GoogleCalendarApi(googleOAuth);
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
        var recoverButton = MakeButton("Definir ou recuperar senha");
        recoverButton.BackColor = Color.White;
        recoverButton.ForeColor = Color.FromArgb(0, 103, 44);
        recoverButton.FlatAppearance.BorderColor = Color.FromArgb(0, 103, 44);
        recoverButton.Click += (_, _) =>
        {
            using var recovery = new AgendaPasswordDialog(email.Text);
            recovery.ShowDialog(this);
        };
        card.Controls.Add(email); card.Controls.Add(password);
        card.Controls.Add(signIn); card.Controls.Add(recoverButton);
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

        period.Items.AddRange(["Hoje", "7 dias", "Mês"]);
        period.SelectedIndex = 2;
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 65, WrapContents = false,
            Padding = new Padding(10, 13, 5, 5), BackColor = Color.White
        };
        bar.Controls.Add(new Label { Text = "A partir de:", AutoSize = true, Margin = new Padding(6, 10, 5, 0) });
        bar.Controls.Add(startDate); bar.Controls.Add(period);
        googleButton.BackColor = Color.FromArgb(24, 82, 139);
        googleButton.Click += async (_, _) =>
        {
            if (busy) return;
            using var dialog = new GoogleCalendarConnectDialog(googleFeed);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                UpdateGoogleLabel();
                await LoadAppointments();
            }
        };
        add.Text = "+ Agendamento local";
        googleApiButton.BackColor = Color.FromArgb(3, 91, 54);
        googleApiButton.Click += async (_, _) =>
        {
            if (busy) return;
            using var manager = new GoogleCalendarManageDialog(googleOAuth, googleApi);
            manager.ShowDialog(this);
            if (!manager.Changed) return;
            UpdateGoogleLabel();
            await LoadAppointments();
        };
        googleNewButton.BackColor = Color.FromArgb(7, 106, 63);
        googleNewButton.Click += async (_, _) => await CreateGoogleEvent();
        bar.Controls.Add(refresh); bar.Controls.Add(add);
        bar.Controls.Add(googleNewButton);
        bar.Controls.Add(googleApiButton);
        bar.Controls.Add(googleButton); bar.Controls.Add(signOut);
        UpdateGoogleLabel();
        googleRefreshTimer.Tick += async (_, _) =>
        {
            if (companyId is not null && appointmentsPane.Visible && !busy && (googleFeed.HasUrl || googleOAuth.CalendarId is not null))
                await LoadAppointments();
        };
        googleRefreshTimer.Start();
        foreach (var (name, weight) in new (string Name, float Weight)[]
        {
            ("Data", 65), ("Hora", 45), ("Cliente", 145), ("Serviço", 115),
            ("Situação", 85), ("Telefone", 105), ("Endereço", 155)
        })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight });
        grid.ColumnHeadersHeight = 36; grid.RowTemplate.Height = 34;
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && !busy)
                await EditSelectedGoogleEvent(e.RowIndex);
        };
        appointmentsPane.Controls.Add(grid);
        appointmentsPane.Controls.Add(calendar);
        appointmentsPane.Controls.Add(bar);
        calendar.DateSelected += selected =>
        {
            filterDate = selected;
            ShowCachedRows();
        };
        calendar.MonthChanged += month =>
        {
            suspendRefresh = true;
            period.SelectedIndex = 2;
            startDate.Value = month.ToDateTime(TimeOnly.MinValue);
            suspendRefresh = false;
            filterDate = null;
            _ = LoadAppointments();
        };
        refresh.Click += async (_, _) => { filterDate = null; await LoadAppointments(); };
        startDate.ValueChanged += async (_, _) =>
        {
            if (companyId is not null && !suspendRefresh) { filterDate = null; await LoadAppointments(); }
        };
        period.SelectedIndexChanged += async (_, _) =>
        {
            if (companyId is not null && !suspendRefresh) { filterDate = null; await LoadAppointments(); }
        };
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
        googleButton.Enabled = googleApiButton.Enabled = !value;
        googleNewButton.Enabled = !value && googleOAuth.CanWriteSelectedCalendar;
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
        grid.Rows.Clear(); cachedRows.Clear(); calendar.SetEntries([]);
        googleEventCount = supabaseEventCount = 0;
        filterDate = null;
        appointmentsPane.Visible = false; loginPane.Visible = true;
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
            var chosen = DateOnly.FromDateTime(startDate.Value);
            var first = period.SelectedIndex == 2
                ? new DateOnly(chosen.Year, chosen.Month, 1) : chosen;
            var last = period.SelectedIndex switch
            {
                0 => first,
                1 => first.AddDays(6),
                _ => first.AddMonths(1).AddDays(-1)
            };
            var query = "rest/v1/agenda_appointments?select=id,client,phone,primary_service,service_ids,date,time,status,address" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) + "&deleted_at=is.null" +
                $"&date=gte.{first:yyyy-MM-dd}&date=lte.{last:yyyy-MM-dd}" +
                "&order=date.asc,time.asc&limit=500";
            using var response = await Authorized(HttpMethod.Get, query);
            RequireSuccess(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            cachedRows.Clear();
            var calendarRows = new List<AgendaCalendarEntry>();
            supabaseEventCount = 0;
            googleEventCount = 0;
            string? googleWarning = null;
            foreach (var appointment in doc.RootElement.EnumerateArray())
            {
                static string Read(JsonElement item, string key) =>
                    item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
                        ? value.ToString() : "";
                var date = Read(appointment, "date");
                var time = Read(appointment, "time");
                if (time.Length > 5) time = time[..5];
                var clientName = Read(appointment, "client");
                var service = Read(appointment, "primary_service");
                var statusName = Read(appointment, "status");
                var ids = appointment.TryGetProperty("service_ids", out var services)
                    && services.ValueKind == JsonValueKind.Array
                        ? services.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString() ?? "")
                        : Enumerable.Empty<string>();
                var category = AgendaServiceColors.FromServiceIds(ids, service, statusName);
                cachedRows.Add((
                    [date, time, clientName, service, statusName,
                        Read(appointment, "phone"), Read(appointment, "address")], category, null));
                supabaseEventCount++;
                if (DateOnly.TryParseExact(date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                    calendarRows.Add(new AgendaCalendarEntry(parsed, time, clientName, service, statusName, category));
            }
            // O Google é uma fonte de leitura: não copia os eventos ao Supabase,
            // não cria serviços novos e não dispara nenhum lembrete.
            if (googleOAuth.CalendarId is not null)
            {
                try
                {
                    var googleEvents = await googleApi.Events(first, last);
                    foreach (var item in googleEvents)
                    {
                        var category = AgendaServiceColors.Classify(item.Title, item.Description);
                        cachedRows.Add(([
                            item.Date.ToString("yyyy-MM-dd"), item.Time,
                            item.Title, category.Label, "Google • editável", "",
                            item.Location
                        ], category, item));
                        calendarRows.Add(new AgendaCalendarEntry(item.Date, item.Time,
                            item.Title, category.Label, "google", category));
                        googleEventCount++;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or
                    HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    googleWarning = "Google API: " + ex.Message;
                }
            }
            else if (googleFeed.HasUrl)
            {
                try
                {
                    var googleEvents = await googleFeed.Read(first, last);
                    foreach (var item in googleEvents)
                    {
                        var category = AgendaServiceColors.Classify(item.Title, item.Description);
                        cachedRows.Add(([
                            item.Date.ToString("yyyy-MM-dd"), item.Time,
                            item.Title, category.Label, "Google • leitura", "",
                            item.Location
                        ], category, null));
                        calendarRows.Add(new AgendaCalendarEntry(item.Date, item.Time,
                            item.Title, category.Label, "google", category));
                        googleEventCount++;
                    }
                }
                catch (Exception)
                {
                    googleWarning = "Não foi possível ler o Google iCal. Revise o link privado e a conexão.";
                }
            }
            cachedRows.Sort((left, right) =>
                string.CompareOrdinal(left.Values[0] + left.Values[1],
                    right.Values[0] + right.Values[1]));
            calendar.SetEntries(calendarRows);
            ShowCachedRows();
            Status($"{supabaseEventCount} locais + {googleEventCount} Google • {first:dd/MM} a {last:dd/MM}. " +
                (googleWarning ?? (googleOAuth.CalendarId is null
                    ? "Google iCal: leitura apenas; sem duplicar nem enviar WhatsApp."
                    : "Google API conectada. Duplo clique em evento do Google para editar.")));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Status("Não foi possível atualizar: " + error.Message); }
        finally { SetBusy(false); }
    }

    private void UpdateGoogleLabel()
    {
        googleButton.Text = googleFeed.HasUrl ? "iCal conectado ✓" : "Google iCal";
        googleApiButton.Text = googleOAuth.CanWriteSelectedCalendar
            ? "Google: MODUX - TESTES ✓"
            : googleOAuth.Connected ? "Google • modo protegido" : "Google (configurar)";
        googleNewButton.Enabled = googleOAuth.CanWriteSelectedCalendar;
    }

    private void ShowCachedRows()
    {
        grid.Rows.Clear();
        foreach (var (record, category, google) in cachedRows)
        {
            if (filterDate is not null && record[0] != filterDate.Value.ToString("yyyy-MM-dd"))
                continue;
            var index = grid.Rows.Add(record);
            grid.Rows[index].Tag = google;
            grid.Rows[index].Cells[2].ToolTipText = google is not null
                ? "Duplo clique para editar diretamente no Google"
                : "Agendamento local ou iCal somente leitura";
            var state = record[4];
            var serviceCell = grid.Rows[index].Cells[3];
            serviceCell.Style.BackColor = category.Color;
            serviceCell.Style.ForeColor = category.TextColor;
            serviceCell.ToolTipText = category.Label + " • " + category.GoogleColorName
                + (state == "Google • leitura" ? " (classificado pelo texto do evento)" : "");
            if (state is "Google • leitura" or "Google • editável")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(229, 241, 255);
            else if (state is "cancelado" or "cancelar")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(164, 42, 42);
            else if (state is "confirmado")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(0, 103, 44);
            else if (state is "revisao" or "reagendar")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 223);
        }
    }

    private async Task EditSelectedGoogleEvent(int rowIndex)
    {
        if (busy || googleOAuth.CalendarId is null || rowIndex >= grid.Rows.Count) return;
        if (!googleOAuth.CanWriteSelectedCalendar)
        {
            Status("Edição bloqueada: selecione a agenda isolada MODUX - TESTES.");
            return;
        }
        if (grid.Rows[rowIndex].Tag is not GoogleEventSnapshot google)
        {
            Status("Esse compromisso ainda não é editável no Google. Eventos iCal são apenas leitura.");
            return;
        }
        if (google.SpansMultipleDays || google.Duration > 1440)
        {
            Status("Eventos de vários dias ainda devem ser editados no Google Agenda original.");
            return;
        }
        using var editor = new GoogleCalendarEventDialog(google, google.Date);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            if (editor.Action == GoogleEventUserAction.Save && editor.Draft is { } draft)
            {
                await googleApi.Update(google, draft);
                Status("Compromisso atualizado no Google Agenda.");
            }
            else if (editor.Action == GoogleEventUserAction.Delete)
            {
                await googleApi.Delete(google);
                Status("Compromisso excluído do Google Agenda.");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or
            HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, error.Message, "Google Agenda", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task CreateGoogleEvent()
    {
        if (busy || !googleOAuth.CanWriteSelectedCalendar)
        {
            Status("Para criar eventos nesta fase, selecione MODUX - TESTES no Google.");
            return;
        }
        using var editor = new GoogleCalendarEventDialog(null, calendar.SelectedDate);
        if (editor.ShowDialog(this) != DialogResult.OK ||
            editor.Action != GoogleEventUserAction.Save || editor.Draft is not { } draft) return;
        SetBusy(true);
        try
        {
            await googleApi.Create(draft);
            Status("Compromisso criado no Google Agenda. Nenhuma mensagem WhatsApp foi enviada.");
        }
        catch (Exception error) when (error is InvalidOperationException or
            HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, error.Message, "Google Agenda", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task CreateAppointment()
    {
        if (busy || companyId is null) return;
        using var dialog = new AgendaAppointmentDialog(calendar.SelectedDate);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } value) return;
        SetBusy(true);
        var saved = false;
        try
        {
            using var response = await Authorized(HttpMethod.Post, "rest/v1/agenda_appointments", new
            {
                company_id = companyId, kind = value.Kind, client = value.Client,
                responsible = value.Responsible, phone = value.Phone, email = value.Email,
                address = value.Address, service_ids = value.ServiceIds,
                primary_service = value.PrimaryService, date = value.Date,
                time = value.Time, duration = value.Duration,
                status = "agendado", technician = value.Technician, notes = value.Notes,
                source = "modux", created_by = currentUserId, updated_by = currentUserId
            });
            RequireSuccess(response);
            saved = true;
            Status("Agendamento salvo na nuvem. Nenhum WhatsApp ou evento Google foi enviado.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Status("Não foi possível salvar: " + error.Message); }
        finally { SetBusy(false); }
        if (saved)
        {
            filterDate = null;
            var date = DateOnly.ParseExact(value.Date, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
            if (calendar.DisplayedMonth.Month != date.Month ||
                calendar.DisplayedMonth.Year != date.Year)
                calendar.SetMonth(date);
            else await LoadAppointments();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            googleRefreshTimer.Stop();
            googleRefreshTimer.Dispose();
            googleFeed.Dispose();
            googleApi.Dispose();
            googleOAuth.Dispose();
            http.Dispose();
        }
        base.Dispose(disposing);
    }
}
