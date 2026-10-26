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
    private string? accessToken, refreshToken, companyId, currentUserId, currentRole;
    private bool busy;
    private readonly SemaphoreSlim sessionRenewal = new(1, 1);

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
    private readonly AgendaCalendarLinkStore linkStore;
    private readonly AgendaAppointmentStore appointmentStore;
    private readonly AgendaServiceCatalog serviceCatalog;
    internal event Action<ComposeRequest>? ComposeRequested;
    private readonly AgendaWorkflowStore workflowStore;
    private readonly Button workflowButton = MakeButton("Pendências e pós-venda");
    private readonly Button catalogButton = MakeButton("Cores dos serviços");
    private readonly Button editLocalButton = MakeButton("Editar atendimento");
    private sealed record AgendaRowContext(GoogleEventSnapshot? Google, AgendaLocalSnapshot? Local);
    private readonly Button reconcileButton = MakeButton("Conferir vínculo");
    private readonly Button completeGoogleButton = MakeButton("Completar Google");
    private readonly Button linkButton = MakeButton("Vincular atendimento");
    private readonly List<AgendaLocalSnapshot> localAppointments = [];
    private IReadOnlyList<AgendaCalendarLink> calendarLinks = [];
    private bool linksReady;
    private readonly Button googleNewButton = MakeButton("+ Evento Google");
    private readonly Button googleButton = MakeButton("Conectar Google Agenda");
    private readonly System.Windows.Forms.Timer googleRefreshTimer = new() { Interval = 15 * 60 * 1000 };
    private int googleEventCount;
    private int googleTestEventCount;
    private int supabaseEventCount;
    private readonly List<(string[] Values, AgendaServiceCategory Category, GoogleEventSnapshot? Google, AgendaLocalSnapshot? Local)> cachedRows = [];
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
        linkStore = new AgendaCalendarLinkStore(Authorized);
        appointmentStore = new AgendaAppointmentStore(Authorized);
        serviceCatalog = new AgendaServiceCatalog(Authorized);
        workflowStore = new AgendaWorkflowStore(Authorized);
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
            Dock = DockStyle.Top, MinimumSize = new Size(0, 65), WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
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
        add.Text = "+ Agendamento";
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
        bar.Controls.Add(editLocalButton);
        bar.Controls.Add(catalogButton);
        bar.Controls.Add(workflowButton);
        workflowButton.Click += async (_, _) => await ShowWorkflow();
        catalogButton.Click += async (_, _) => await ConfigureCatalog();
        editLocalButton.Click += async (_, _) => await EditSelectedAppointment();
        bar.Controls.Add(googleNewButton);
        bar.Controls.Add(linkButton);
        bar.Controls.Add(completeGoogleButton);
        bar.Controls.Add(reconcileButton);
        reconcileButton.Click += async (_, _) => await ReconcileSelectedEvent();
        completeGoogleButton.Click += async (_, _) => await CompleteSelectedGoogleEvent();
        linkButton.Click += async (_, _) => await LinkSelectedGoogleEvent();
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
        grid.SelectionChanged += (_, _) => UpdateLinkButton();
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
        catalogButton.Enabled = !value && currentRole == "admin";
        workflowButton.Enabled = !value && companyId is not null;
        googleNewButton.Enabled = !value && googleOAuth.CanWriteSelectedCalendar;
        UpdateLinkButton();
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
            currentRole = members.RootElement[0].GetProperty("role").GetString();
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
        accessToken = refreshToken = companyId = currentUserId = currentRole = null;
        password.Clear();
        grid.Rows.Clear(); cachedRows.Clear(); calendar.SetEntries([]);
        localAppointments.Clear(); calendarLinks = []; linksReady = false;
        serviceCatalog.Clear(); calendar.SetLegend(AgendaServiceColors.Legend);
        googleEventCount = googleTestEventCount = supabaseEventCount = 0;
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
        async Task<HttpResponseMessage> Send(string? token)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("apikey", PublicKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (method == HttpMethod.Post || method == HttpMethod.Patch)
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (data is not null) request.Content = Json(data);
            return await http.SendAsync(request);
        }
        var observedToken = accessToken;
        var response = await Send(observedToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await sessionRenewal.WaitAsync();
            try
            {
                if (accessToken == observedToken && !await RenewToken())
                    throw new InvalidOperationException("Sessão expirada. Entre novamente.");
            }
            finally { sessionRenewal.Release(); }
            response = await Send(accessToken);
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
            await serviceCatalog.Load(companyId);
            calendar.SetLegend(AgendaServiceColors.Legend.Select(serviceCatalog.Apply).ToArray());
            var chosen = DateOnly.FromDateTime(startDate.Value);
            var first = period.SelectedIndex == 2
                ? new DateOnly(chosen.Year, chosen.Month, 1) : chosen;
            var last = period.SelectedIndex switch
            {
                0 => first,
                1 => first.AddDays(6),
                _ => first.AddMonths(1).AddDays(-1)
            };
            var query = "rest/v1/agenda_appointments?select=id,revision,client,phone,primary_service,service_ids,date,time,status,address,duration,notes,responsible,email,kind,technician,service_completed_date,feedback_score,feedback_note" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) + "&deleted_at=is.null" +
                $"&date=gte.{first:yyyy-MM-dd}&date=lte.{last:yyyy-MM-dd}" +
                "&order=date.asc,time.asc&limit=500";
            using var response = await Authorized(HttpMethod.Get, query);
            RequireSuccess(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            cachedRows.Clear();
            var calendarRows = new List<AgendaCalendarEntry>();
            supabaseEventCount = 0;
            googleEventCount = googleTestEventCount = 0;
            string? googleWarning = null;
            localAppointments.Clear();
            localAppointments.AddRange(doc.RootElement.EnumerateArray().Select(AgendaLocalSnapshot.Parse));
            supabaseEventCount = localAppointments.Count;
            calendarLinks = [];
            linksReady = false;
            IReadOnlyList<GoogleEventSnapshot> testEvents = [];
            // No piloto, manter a agenda operacional iCal (somente leitura)
            // SEMPRE visível, mesmo depois de autorizar a API OAuth para MODUX - TESTES.
            // O Google API nunca substitui nem importa automaticamente eventos do iCal.
            if (googleFeed.HasUrl)
            {
                try
                {
                    var googleEvents = await googleFeed.Read(first, last);
                    foreach (var item in googleEvents)
                    {
                        var category = serviceCatalog.Apply(AgendaServiceColors.Classify(item.Title, item.Description));
                        cachedRows.Add(([
                            item.Date.ToString("yyyy-MM-dd"), item.Time,
                            item.Title, category.Label, "Google • leitura", "",
                            item.Location
                        ], category, null, null));

                        googleEventCount++;
                    }
                }
                catch (Exception)
                {
                    // Não expor URL secreta do iCal em mensagens ou logs.
                    googleWarning = "Falha ao ler a agenda operacional iCal. Verifique a conexão.";
                }
            }

            if (googleOAuth.CanWriteSelectedCalendar)
            {
                // Apenas eventos do calendário SECUNDÁRIO MODUX - TESTES entram
                // no modo editável. Operacional continua identificado como leitura.
                try
                {
                    testEvents = await googleApi.Events(first, last);
                    googleTestEventCount = testEvents.Count;
                    calendarLinks = await linkStore.List(companyId, googleOAuth.CalendarId!);
                    linksReady = true;
                    // Um evento pode ter mudado de mês no Google. Buscar os dados
                    // operacionais vinculados pelo ID, sem depender da data antiga.
                    var missing = calendarLinks.Where(link => testEvents.Any(e => e.Id == link.EventId))
                        .Select(link => link.AppointmentId).Except(localAppointments.Select(a => a.Id)).ToArray();
                    foreach (var group in missing.Chunk(50))
                    {
                        if (group.Any(id => !Guid.TryParse(id, out _)))
                            throw new InvalidOperationException("Vínculo contém identificação inválida.");
                        using var extra = await Authorized(HttpMethod.Get,
                            "rest/v1/agenda_appointments?select=id,revision,client,phone,primary_service,service_ids,date,time,status,address,duration,notes,responsible,email,kind,technician,service_completed_date,feedback_score,feedback_note" +
                            "&company_id=eq." + Uri.EscapeDataString(companyId) +
                            "&deleted_at=is.null&id=in.(" + string.Join(",", group) + ")");
                        RequireSuccess(extra);
                        using var extraRows = JsonDocument.Parse(await extra.Content.ReadAsStringAsync());
                        localAppointments.AddRange(extraRows.RootElement.EnumerateArray().Select(AgendaLocalSnapshot.Parse));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or
                    HttpRequestException or TaskCanceledException or JsonException)
                {
                    linksReady = false;
                    googleWarning = "Google TESTES / vínculos: " + ex.Message;
                }
            }
            var unified = AgendaCalendarProjection.Merge(companyId, googleOAuth.CalendarId ?? "",
                localAppointments, testEvents, calendarLinks);
            foreach (var row in unified)
            {
                var local = row.Local;
                var google = row.Google;
                var category = local is not null
                    ? serviceCatalog.Resolve(local)
                    : serviceCatalog.Apply(AgendaServiceColors.Classify(google!.Title, google.Description));
                var state = google is not null
                    ? row.Link is null ? "Google • TESTES"
                        : local is null ? "TESTES • vínculo pendente"
                        : row.Link.ETag != google.ETag ? "TESTES • conferir alteração" : "TESTES • " + local.Status
                    : row.Link is null || row.Link.SyncStatus == "excluido" ? local!.Status : "Vínculo • conferir Google";
                cachedRows.Add(([
                    google?.Date.ToString("yyyy-MM-dd") ?? local!.Date,
                    google?.Time ?? local!.Time,
                    local?.Client ?? google!.Title,
                    local?.Service ?? category.Label, state, local?.Phone ?? "",
                    local?.Address ?? google!.Location
                ], category, google, local));
            }
            foreach (var row in cachedRows)
                if (DateOnly.TryParseExact(row.Values[0], "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var day))
                    calendarRows.Add(new AgendaCalendarEntry(day, row.Values[1], row.Values[2],
                        row.Values[3], row.Values[4], row.Category));
            cachedRows.Sort((left, right) =>
                string.CompareOrdinal(left.Values[0] + left.Values[1],
                    right.Values[0] + right.Values[1]));
            calendar.SetEntries(calendarRows);
            ShowCachedRows();
            Status($"{supabaseEventCount} cadastros locais • {googleEventCount} Google iCal (leitura) " +
                $"• {googleTestEventCount} TESTES (editável) • {first:dd/MM} a {last:dd/MM}. " +
                (googleWarning ?? (googleOAuth.CanWriteSelectedCalendar
                    ? "Duplo clique apenas em evento TESTES para editar. Clientes reais protegidos."
                    : "Agenda operacional iCal: somente leitura. Sem envio WhatsApp.")));
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
        foreach (var (record, category, google, local) in cachedRows)
        {
            if (filterDate is not null && record[0] != filterDate.Value.ToString("yyyy-MM-dd"))
                continue;
            var index = grid.Rows.Add(record);
            grid.Rows[index].Tag = new AgendaRowContext(google, local);
            grid.Rows[index].Cells[2].ToolTipText = google is not null
                ? "MODUX - TESTES: duplo clique para editar evento fictício"
                : "Operacional: agendamento local ou iCal somente leitura";
            var state = record[4];
            var serviceCell = grid.Rows[index].Cells[3];
            serviceCell.Style.BackColor = category.Color;
            serviceCell.Style.ForeColor = category.TextColor;
            serviceCell.ToolTipText = category.Label + " • " + category.GoogleColorName
                + (state == "Google • leitura" ? " (classificado pelo texto do evento)" : "");
            if (state == "Google • leitura")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(229, 241, 255);
            else if (state == "Google • TESTES" || state.StartsWith("TESTES •", StringComparison.Ordinal))
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 221);
            else if (state is "cancelado" or "cancelar")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(164, 42, 42);
            else if (state is "confirmado")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(0, 103, 44);
            else if (state is "revisao" or "reagendar")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 223);
        }
    }

    private void UpdateLinkButton()
    {
        editLocalButton.Enabled = !busy && grid.SelectedRows.Count == 1 &&
            grid.SelectedRows[0].Tag is AgendaRowContext { Local: not null };
        linkButton.Enabled = !busy && linksReady && googleOAuth.CanWriteSelectedCalendar &&
            grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Tag is AgendaRowContext { Google: not null };
        completeGoogleButton.Enabled = !busy && linksReady && googleOAuth.CanWriteSelectedCalendar &&
            grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Tag is AgendaRowContext { Google: null, Local: { } local } &&
            !calendarLinks.Any(link => link.AppointmentId == local.Id && link.CalendarId == googleOAuth.CalendarId);
        reconcileButton.Enabled = !busy && linksReady && googleOAuth.CanWriteSelectedCalendar &&
            grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Tag is AgendaRowContext { Local: { } linkedLocal } &&
            calendarLinks.Any(link => link.AppointmentId == linkedLocal.Id && link.CalendarId == googleOAuth.CalendarId);
    }

    private async Task ShowWorkflow()
    {
        if (busy || companyId is null) return;
        using var dialog = new AgendaWorkflowDialog(workflowStore, companyId, currentRole == "admin");
        dialog.ShowDialog(this);
        if (dialog.EditRequested is { } appointment) await EditAppointment(appointment);
        else if (dialog.Prepared is { } message) ComposeRequested?.Invoke(message);
    }

    private async Task ConfigureCatalog()
    {
        if (busy || companyId is null || currentRole != "admin") return;
        using var dialog = new Form
        {
            Text = "Cores compartilhadas da equipe", Width = 620, Height = 340,
            StartPosition = FormStartPosition.CenterParent, Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown, WrapContents = false
        };
        var choice = new ComboBox { Width = 540, DropDownStyle = ComboBoxStyle.DropDownList };
        choice.Items.AddRange(serviceCatalog.Items.Cast<object>().ToArray());
        var label = new TextBox { Width = 540, MaxLength = 120 };
        var color = MakeButton("Escolher cor");
        var saveColor = MakeButton("Salvar para a equipe");
        var hint = new Label { Width = 540, Height = 45,
            Text = "Atualiza a configuração do MODUX para a equipe. Os eventos existentes no Google não serão recoloridos." };
        var selectedHex = "#FFFFFF";
        choice.SelectedIndexChanged += (_, _) =>
        {
            if (choice.SelectedItem is not AgendaServiceDefinition item) return;
            label.Text = item.Label; selectedHex = item.ColorHex;
            color.BackColor = item.Color;
        };
        color.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = ColorTranslator.FromHtml(selectedHex), FullOpen = true };
            if (picker.ShowDialog(dialog) == DialogResult.OK)
            { selectedHex = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; color.BackColor = picker.Color; }
        };
        var saving = false;
        var changed = false;
        dialog.FormClosing += (_, e) => { if (saving) e.Cancel = true; };
        saveColor.Click += async (_, _) =>
        {
            if (saving || choice.SelectedItem is not AgendaServiceDefinition item) return;
            saving = true; choice.Enabled = label.Enabled = color.Enabled = saveColor.Enabled = false;
            try
            {
                await serviceCatalog.SaveAppearance(companyId, item, label.Text, selectedHex);
                changed = true;
            }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
            { MessageBox.Show(dialog, error.Message, "Catálogo não confirmado", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally
            {
                saving = false;
                choice.Enabled = label.Enabled = color.Enabled = saveColor.Enabled = true;
                if (changed) dialog.DialogResult = DialogResult.OK;
            }
        };
        layout.Controls.AddRange([hint, choice, label, color, saveColor]);
        dialog.Controls.Add(layout);
        if (choice.Items.Count > 0) choice.SelectedIndex = 0;
        dialog.ShowDialog(this);
        if (changed) await LoadAppointments();
    }

    private async Task EditSelectedAppointment()
    {
        if (busy || companyId is null || grid.SelectedRows.Count != 1 ||
            grid.SelectedRows[0].Tag is not AgendaRowContext { Local: { } original }) return;
        await EditAppointment(original);
    }

    private async Task EditAppointment(AgendaLocalSnapshot original)
    {
        if (companyId is null || busy) return;
        bool linked;
        SetBusy(true);
        try { linked = await appointmentStore.HasCalendarLink(companyId, original.Id); }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        { MessageBox.Show(this, error.Message, "Conferir vínculos"); return; }
        finally { SetBusy(false); }
        using var editor = new AgendaAppointmentDialog(calendar.SelectedDate, source: original, lockSchedule: linked, sharedServices: serviceCatalog.Items);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is not { } proposed) return;
        SetBusy(true);
        try
        {
            await appointmentStore.Update(companyId, original, proposed, linked);
            Status("Cadastro operacional atualizado para a equipe. Nenhum evento Google ou WhatsApp foi enviado.");
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        { MessageBox.Show(this, error.Message, "Conferir atualização local", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task ReconcileSelectedEvent()
    {
        if (busy || !linksReady || companyId is null || !googleOAuth.CanWriteSelectedCalendar ||
            grid.SelectedRows.Count != 1 || grid.SelectedRows[0].Tag is not AgendaRowContext { Local: { } local }) return;
        var link = calendarLinks.FirstOrDefault(l => l.AppointmentId == local.Id && l.CalendarId == googleOAuth.CalendarId);
        if (link is null) return;
        SetBusy(true);
        try
        {
            GoogleEventSnapshot? current = null;
            try { current = await googleApi.Event(link.EventId); }
            catch (GoogleEventUnavailableException)
            {
                if (MessageBox.Show(this, "O evento não foi localizado no Google. Confira no Google Agenda se ele foi excluído. " +
                    "Deseja marcar somente este atendimento vinculado como cancelado?", "Conferir cancelamento",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            if (current is not null && MessageBox.Show(this,
                $"Atualizar o atendimento de {local.Client} com o horário atual do Google: {current.Date:dd/MM/yyyy} {current.Time}? " +
                "Contatos, serviços, equipe e observações serão preservados.", "Conferir vínculo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await linkStore.Reconcile(companyId, link, local, current, cancelled: current is null);
            Status("Horário/status do atendimento reconciliado com o Google.");
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        { MessageBox.Show(this, error.Message, "Conferência não concluída", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task CompleteSelectedGoogleEvent()
    {
        if (busy || companyId is null || !linksReady || !googleOAuth.CanWriteSelectedCalendar ||
            grid.SelectedRows.Count != 1 ||
            grid.SelectedRows[0].Tag is not AgendaRowContext { Google: null, Local: { } local }) return;
        if (calendarLinks.Any(link => link.AppointmentId == local.Id && link.CalendarId == googleOAuth.CalendarId))
        { Status("O atendimento já possui vínculo. Confira o evento original antes de tentar criar outro."); return; }
        if (MessageBox.Show(this, $"Criar ou recuperar no calendário MODUX - TESTES o evento do atendimento de {local.Client}? " +
            "O identificador do atendimento será reutilizado para evitar duplicação.", "Completar Google",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            await CompleteGoogle(local);
            Status("Evento Google e vínculo confirmados. Nenhum WhatsApp foi enviado.");
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException
            or TaskCanceledException or JsonException)
        { MessageBox.Show(this, "O atendimento permanece salvo. A conclusão Google/vínculo está pendente: " +
            error.Message, "Google pendente", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task CompleteGoogle(AgendaLocalSnapshot local)
    {
        if (companyId is null || !googleOAuth.CanWriteSelectedCalendar)
            throw new InvalidOperationException("Conecte primeiro o calendário MODUX - TESTES.");
        var calendarId = googleOAuth.CalendarId!;
        var draft = local.AsGoogleDraft(companyId);
        _ = draft.AsGoogleFields();
        var saved = await googleApi.EnsureOperationalEvent(draft);
        var link = await linkStore.Link(companyId, local, calendarId, saved);
        await linkStore.Reconcile(companyId, link, local, saved);
    }

    private async Task LinkSelectedGoogleEvent()
    {
        if (busy || !linksReady || companyId is null || !googleOAuth.CanWriteSelectedCalendar ||
            grid.SelectedRows.Count != 1 || grid.SelectedRows[0].Tag is not AgendaRowContext { Google: { } google }) return;
        var calendarId = googleOAuth.CalendarId!;
        if (calendarLinks.Any(link => link.EventId == google.Id && link.CalendarId == calendarId))
        {
            Status("Este evento já tem vínculo. A associação existente foi preservada.");
            return;
        }
        var choices = localAppointments.Where(local =>
            !calendarLinks.Any(link => link.CalendarId == calendarId && link.AppointmentId == local.Id)).ToArray();
        if (choices.Length == 0)
        {
            MessageBox.Show(this, "Cadastre primeiro o atendimento local correspondente. " +
                "Nenhum cadastro será importado ou criado automaticamente.", "Vincular atendimento");
            return;
        }
        using var chooser = new Form
        {
            Text = "Vincular evento Google a atendimento existente", Width = 720, Height = 260,
            StartPosition = FormStartPosition.CenterParent, Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var selectLocal = new ComboBox
        {
            Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList
        };
        selectLocal.Items.AddRange(choices.Cast<object>().ToArray());
        selectLocal.SelectedIndex = -1;
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 95, Padding = new Padding(12),
            Text = $"Evento Google: {google.Title}\n{google.Date:dd/MM/yyyy} {google.Time}\n" +
                "Escolha o atendimento correspondente. A data/hora exibida virá do Google; os contatos e serviços serão preservados."
        };
        var confirm = MakeButton("Confirmar vínculo");
        confirm.Dock = DockStyle.Bottom; confirm.Enabled = false;
        selectLocal.SelectedIndexChanged += (_, _) => confirm.Enabled = selectLocal.SelectedItem is AgendaLocalSnapshot;
        confirm.Click += (_, _) => chooser.DialogResult = DialogResult.OK;
        chooser.Controls.Add(selectLocal); chooser.Controls.Add(hint); chooser.Controls.Add(confirm);
        if (chooser.ShowDialog(this) != DialogResult.OK || selectLocal.SelectedItem is not AgendaLocalSnapshot local) return;
        if (MessageBox.Show(this, $"Vincular o atendimento de {local.Client} ao evento “{google.Title}”? " +
            "Isso registra a associação, sem criar, editar ou excluir eventos no Google.",
            "Confirmar associação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            // Verificar a versão Google imediatamente antes de persistir o vínculo.
            var current = await googleApi.Event(google.Id);
            if (current.ETag != google.ETag)
                throw new InvalidOperationException("O evento mudou no Google. Atualize antes de vincular.");
            await linkStore.Link(companyId, local, calendarId, google);
            Status("Vínculo salvo para a equipe. Nenhum evento Google foi criado ou alterado.");
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException
            or TaskCanceledException or JsonException)
        { MessageBox.Show(this, error.Message, "Vínculo não confirmado", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task EditSelectedGoogleEvent(int rowIndex)
    {
        if (busy || googleOAuth.CalendarId is null || rowIndex >= grid.Rows.Count) return;
        if (!googleOAuth.CanWriteSelectedCalendar)
        {
            Status("Edição bloqueada: selecione a agenda isolada MODUX - TESTES.");
            return;
        }
        if (grid.Rows[rowIndex].Tag is not AgendaRowContext { Google: { } google })
        {
            Status("Esse compromisso ainda não é editável no Google. Eventos iCal são apenas leitura.");
            return;
        }
        if (google.SpansMultipleDays || google.Duration > 1440)
        {
            Status("Eventos de vários dias ainda devem ser editados no Google Agenda original.");
            return;
        }
        var local = (grid.Rows[rowIndex].Tag as AgendaRowContext)?.Local;
        var link = calendarLinks.FirstOrDefault(l => l.CalendarId == googleOAuth.CalendarId && l.EventId == google.Id);
        using var editor = new GoogleCalendarEventDialog(google, google.Date, local is not null && link is not null);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true);
        var changedOnGoogle = false;
        try
        {
            if (editor.Action == GoogleEventUserAction.Save && editor.Draft is { } draft)
            {
                var updated = await googleApi.Update(google, draft);
                changedOnGoogle = true;
                if (local is not null && link is not null && companyId is not null)
                    await linkStore.Reconcile(companyId, link, local, updated);
                Status(local is not null && link is not null
                    ? "Compromisso atualizado no Google e atendimento vinculado reconciliado."
                    : "Compromisso atualizado no Google Agenda.");
            }
            else if (editor.Action == GoogleEventUserAction.Delete)
            {
                await googleApi.Delete(google);
                changedOnGoogle = true;
                if (local is not null && link is not null && companyId is not null)
                    await linkStore.Reconcile(companyId, link, local, google, cancelled: true);
                Status(local is not null && link is not null
                    ? "Evento excluído do Google. Atendimento vinculado marcado como cancelado."
                    : "Evento excluído do Google Agenda.");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or
            HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, (changedOnGoogle
                ? "A operação Google foi confirmada, mas o cadastro vinculado ficou pendente. Use Conferir vínculo. " : "") +
                error.Message, "Google Agenda", MessageBoxButtons.OK,
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
        using var dialog = new AgendaAppointmentDialog(calendar.SelectedDate, googleOAuth.CanWriteSelectedCalendar, sharedServices: serviceCatalog.Items);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } value) return;
        SetBusy(true);
        var saved = false;
        var appointmentId = Guid.NewGuid();
        try
        {
            // Validar o rascunho Google antes de criar o atendimento, quando solicitado.
            if (dialog.CreateGoogleEvent)
                _ = new AgendaLocalSnapshot(appointmentId.ToString("D"), 1, value.Client,
                    value.PrimaryService, value.ServiceIds, value.Date, value.Time[..5], "agendado",
                    value.Phone, value.Address, value.Duration, value.Notes, value.Responsible,
                    value.Email, value.Kind, value.Technician).AsGoogleDraft(companyId).AsGoogleFields();
            using var response = await Authorized(HttpMethod.Post, "rest/v1/agenda_appointments", new
            {
                id = appointmentId,
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
            if (dialog.CreateGoogleEvent)
            {
                using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (created.RootElement.ValueKind != JsonValueKind.Array || created.RootElement.GetArrayLength() != 1)
                    throw new InvalidOperationException("Atendimento salvo, mas resposta incompleta. Atualize e use Completar Google.");
                await CompleteGoogle(AgendaLocalSnapshot.Parse(created.RootElement[0]));
                Status("Atendimento, evento Google e vínculo confirmados. Nenhum WhatsApp foi enviado.");
            }
            else Status("Agendamento salvo na nuvem, sem evento Google nem envio WhatsApp.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            var message = saved ? "Atendimento salvo. Google/vínculo pendente: "
                : "Salvamento não confirmado. Em caso de falha de conexão, atualize antes de cadastrar novamente: ";
            Status(message + error.Message);
            MessageBox.Show(this, message + error.Message, "Conferir agendamento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
