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
    private readonly TextBox search = new() { Width = 250, PlaceholderText = "Buscar neste período: cliente, telefone, serviço..." };
    private readonly ComboBox filterState = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox filterOrigin = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox filterService = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button exportButton = MakeButton("Exportar CSV");
    private readonly Button workflowButton = MakeButton("Pendências e pós-venda");
    private readonly Button catalogButton = MakeButton("Cores dos serviços");
    private readonly Button editLocalButton = MakeButton("Editar atendimento");
    private sealed record AgendaRowContext(GoogleEventSnapshot? Google, AgendaLocalSnapshot? Local);
    private readonly Button inviteGoogleButton = MakeButton("Convidar por e-mail");
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
    private AgendaCalendarSpan? loadedRange;
    private string? loadedCompany, loadedCalendar;
    private DateTimeOffset loadedAt;
    private readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 160 };
    private AgendaCalendarMode SelectedMode => period.SelectedIndex switch { 0 => AgendaCalendarMode.Day, 1 => AgendaCalendarMode.Week, _ => AgendaCalendarMode.Month };
    private static string EventKey(GoogleEventSnapshot? google, AgendaLocalSnapshot? local)
        => google is not null ? "g:" + google.Id : local is not null ? "l:" + local.Id : "";
    private static AgendaCalendarEntry CalendarEntry(string[] record, AgendaServiceCategory category, GoogleEventSnapshot? google, AgendaLocalSnapshot? local)
        => new(DateOnly.Parse(record[0]), record[1], record[2], record[3], record[4], category,
            google?.Duration ?? local?.Duration ?? 60, google?.AllDay ?? false, EventKey(google, local), google?.ExclusiveEndDate);

    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 54, AutoEllipsis = true, ForeColor = Color.FromArgb(59, 74, 92), Padding = new Padding(12, 9, 12, 0) };
    private readonly DateTimePicker startDate = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 125 };
    private readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
    private sealed class BufferedGrid : DataGridView { public BufferedGrid() { DoubleBuffered = true; } }
    private readonly DataGridView grid = new BufferedGrid()
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
        Text = text, AutoSize = true, Height = 38, Padding = new Padding(14, 6, 14, 6),
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
        Padding = new Padding(20, 12, 20, 0);
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 10);
        var card = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Height = 350, Padding = new Padding(28),
            BackColor = Color.White
        };
        card.Controls.Add(new Label { Text = "Agenda Inteligente • MODUX",
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
            Text = "Peça ao administrador da sua equipe para liberar seu acesso.",
            AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(5, 15, 5, 0)
        });
        loginPane.Controls.Add(card);
        signIn.Click += async (_, _) => await Login();
        password.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Login(); }
        };

        period.Items.AddRange(["Dia", "Semana", "Mês"]);
        period.SelectedIndex = 2;
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, MinimumSize = new Size(0, 65), WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 13, 5, 5), BackColor = Color.White
        };
        bar.Controls.Add(new Label { Text = "Período", AutoSize = true, Margin = new Padding(6, 10, 5, 0) });
        bar.Controls.Add(startDate); bar.Controls.Add(period);
        googleButton.BackColor = Color.FromArgb(24, 82, 139);
        async Task ConnectFeed()
        {
            if (busy) return;
            using var dialog = new GoogleCalendarConnectDialog(googleFeed);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                UpdateGoogleLabel();
                await LoadAppointments();
            }
        }
        googleButton.Click += async (_, _) => await ConnectFeed();
        add.Text = "+ Agendamento";
        googleApiButton.BackColor = Color.FromArgb(3, 91, 54);
        async Task ManageGoogle()
        {
            if (busy) return;
            using var manager = new GoogleCalendarManageDialog(googleOAuth, googleApi);
            manager.ShowDialog(this);
            if (!manager.Changed) return;
            googleApi.InvalidateColors();
            UpdateGoogleLabel();
            await LoadAppointments();
        }
        googleApiButton.Click += async (_, _) => await ManageGoogle();
        googleNewButton.BackColor = Color.FromArgb(7, 106, 63);
        googleNewButton.Click += async (_, _) => await CreateGoogleEvent();
        bar.Controls.Add(refresh); bar.Controls.Add(add);
        bar.Controls.Add(workflowButton);
        workflowButton.Click += async (_, _) => await ShowWorkflow();
        catalogButton.Click += async (_, _) => await ConfigureCatalog();
        editLocalButton.Click += async (_, _) => await EditSelectedAppointment();
        inviteGoogleButton.Click += async (_, _) => await InviteSelectedGoogleEvent();
        reconcileButton.Click += async (_, _) => await ReconcileSelectedEvent();
        completeGoogleButton.Click += async (_, _) => await CompleteSelectedGoogleEvent();
        linkButton.Click += async (_, _) => await LinkSelectedGoogleEvent();
        var more = MakeButton("Mais ações ▾");
        var tools = new ContextMenuStrip();
        void AddAction(Button source, Func<Task> action)
        {
            var item = new ToolStripMenuItem(source.Text) { Tag = source };
            item.Click += async (_, _) => { if (!busy && source.Enabled) await action(); };
            tools.Items.Add(item);
        }
        AddAction(editLocalButton, EditSelectedAppointment);
        AddAction(googleNewButton, CreateGoogleEvent);
        AddAction(linkButton, LinkSelectedGoogleEvent);
        AddAction(completeGoogleButton, CompleteSelectedGoogleEvent);
        AddAction(reconcileButton, ReconcileSelectedEvent);
        AddAction(inviteGoogleButton, InviteSelectedGoogleEvent);
        tools.Items.Add(new ToolStripSeparator());
        AddAction(catalogButton, ConfigureCatalog);
        AddAction(googleApiButton, ManageGoogle);
        AddAction(googleButton, ConnectFeed);
        tools.Items.Add(new ToolStripSeparator());
        AddAction(signOut, () => { Logout(); return Task.CompletedTask; });
        tools.Opening += (_, _) =>
        {
            tools.BackColor = ModuxAppearance.Surface;
            tools.ForeColor = ModuxAppearance.Text;
            foreach (ToolStripItem item in tools.Items)
            {
                item.BackColor = ModuxAppearance.Surface; item.ForeColor = ModuxAppearance.Text;
                if (item.Tag is Button source) { item.Enabled = source.Enabled && !busy; item.Text = source.Text; }
            }
        };
        more.Click += (_, _) => { if (!busy) tools.Show(more, new Point(0, more.Height)); };
        more.Disposed += (_, _) => tools.Dispose();
        bar.Controls.Add(more);
        Resize += (_, _) => calendar.Height = Math.Clamp(ClientSize.Height - 290, 230, 620);
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
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight,
                MinimumWidth = name switch { "Data" => 100, "Hora" => 70, "Cliente" => 140, "Serviço" => 110, "Situação" => 130, _ => 100 } });
        grid.ColumnHeadersHeight = 40; grid.RowTemplate.Height = 38;
        grid.SelectionChanged += (_, _) => UpdateLinkButton();
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && !busy)
                await EditSelectedGoogleEvent(e.RowIndex);
        };
        filterState.Items.AddRange(["Todas as situações", "A confirmar", "Agendado", "Confirmado", "Concluído", "Cancelado / reagendar", "Vínculo pendente"]);
        filterOrigin.Items.AddRange(["Todas as origens", "Cadastros da equipe", "Google API", "Google iCal", "Vinculados"]);
        filterService.Items.Add("Todos os serviços");
        filterState.SelectedIndex = filterOrigin.SelectedIndex = filterService.SelectedIndex = 0;
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); ShowCachedRows(); };
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        filterState.SelectedIndexChanged += (_, _) => ShowCachedRows();
        filterOrigin.SelectedIndexChanged += (_, _) => ShowCachedRows();
        filterService.SelectedIndexChanged += (_, _) => ShowCachedRows();
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 5, 10, 8), BackColor = Color.White
        };
        filters.Controls.AddRange([search, filterState, filterOrigin, filterService, exportButton]);
        exportButton.Click += (_, _) => ExportVisibleRows();
        appointmentsPane.Controls.Add(grid);
        appointmentsPane.Controls.Add(filters);
        appointmentsPane.Controls.Add(calendar);
        appointmentsPane.Controls.Add(bar);
        calendar.DateSelected += selected =>
        {
            filterDate = selected;
            suspendRefresh = true; startDate.Value = selected.ToDateTime(TimeOnly.MinValue); suspendRefresh = false;
            ShowCachedRows();
        };
        calendar.EventActivated += async key =>
        {
            for (var i = 0; i < grid.Rows.Count; i++)
                if (grid.Rows[i].Tag is AgendaRowContext row && EventKey(row.Google, row.Local) == key)
                { grid.CurrentCell = grid.Rows[i].Cells[2]; await EditSelectedGoogleEvent(i); break; }
        };
        calendar.MonthChanged += async day =>
        {
            suspendRefresh = true; startDate.Value = day.ToDateTime(TimeOnly.MinValue); suspendRefresh = false;
            filterDate = null; await LoadAppointments(false);
        };
        refresh.Click += async (_, _) => { filterDate = null; googleApi.InvalidateColors(); serviceCatalog.Invalidate(); await LoadAppointments(); };
        startDate.ValueChanged += async (_, _) =>
        {
            if (suspendRefresh) return;
            filterDate = null; calendar.SetView(DateOnly.FromDateTime(startDate.Value), SelectedMode);
            if (companyId is not null) await LoadAppointments(false);
        };
        period.SelectedIndexChanged += async (_, _) =>
        {
            if (suspendRefresh) return;
            filterDate = null; calendar.SetView(DateOnly.FromDateTime(startDate.Value), SelectedMode);
            if (companyId is not null) await LoadAppointments(false);
        };
        add.Click += async (_, _) => await CreateAppointment();
        signOut.Click += (_, _) => Logout();
        Controls.Add(loginPane); Controls.Add(appointmentsPane); Controls.Add(status);
        Status("Entre para consultar a agenda da equipe. Mensagens de WhatsApp exigem revisão e envio manual.");
    }

    private void Status(string message) => status.Text = message;
    private void SetBusy(bool value)
    {
        busy = value;
        UseWaitCursor = value;
        signIn.Enabled = refresh.Enabled = add.Enabled = signOut.Enabled = !value;
        calendar.Enabled = startDate.Enabled = period.Enabled = !value;
        exportButton.Enabled = !value && companyId is not null;
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
                Status("Conta autenticada, mas sem permissão ativa para a Agenda MODUX.");
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
        loadedRange = null; loadedCompany = loadedCalendar = null; searchDelay.Stop();
        password.Clear();
        grid.Rows.Clear(); cachedRows.Clear(); calendar.SetEntries([]);
        localAppointments.Clear(); calendarLinks = []; linksReady = false;
        serviceCatalog.Clear(); calendar.SetLegend(AgendaServiceColors.Legend);
        search.Clear(); filterState.SelectedIndex = filterOrigin.SelectedIndex = 0;
        filterService.Items.Clear(); filterService.Items.Add("Todos os serviços"); filterService.SelectedIndex = 0;
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
    private async Task LoadAppointments(bool force = true)
    {
        if (busy || companyId is null) return;
        var chosen = DateOnly.FromDateTime(startDate.Value);
        calendar.SetView(chosen, SelectedMode);
        var visibleRange = AgendaCalendarLayout.Range(chosen, SelectedMode);
        if (!force && loadedCompany == companyId && loadedCalendar == googleOAuth.CalendarId &&
            loadedRange?.Covers(visibleRange) == true && DateTimeOffset.UtcNow - loadedAt < TimeSpan.FromSeconds(90))
        { ShowCachedRows(); Status($"{grid.Rows.Count} eventos na lista • {visibleRange.First:dd/MM} a {visibleRange.Last:dd/MM}. Período carregado • Atualizar consulta as alterações da equipe."); return; }
        var envelope = AgendaCalendarLayout.MonthEnvelope(chosen);
        var first = envelope.First; var last = envelope.Last;
        loadedRange = null;
        Status("Atualizando a agenda…");
        SetBusy(true);
        try
        {
            await serviceCatalog.Load(companyId);
            calendar.SetLegend(AgendaServiceColors.Legend.Select(serviceCatalog.Apply).ToArray());
            var selectedService = (filterService.SelectedItem as AgendaServiceDefinition)?.Id;
            filterService.Items.Clear(); filterService.Items.Add("Todos os serviços");
            filterService.Items.AddRange(serviceCatalog.Items.Cast<object>().ToArray());
            filterService.SelectedIndex = 0;
            if (selectedService is not null && serviceCatalog.Items.FirstOrDefault(x => x.Id == selectedService) is { } previous)
                filterService.SelectedItem = previous;
            var query = "rest/v1/agenda_appointments?select=id,revision,client,phone,primary_service,service_ids,date,time,status,address,duration,notes,responsible,email,kind,technician,service_completed_date,feedback_score,feedback_note" +
                "&company_id=eq." + Uri.EscapeDataString(companyId) + "&deleted_at=is.null" +
                $"&date=gte.{first.AddDays(-1):yyyy-MM-dd}&date=lte.{last:yyyy-MM-dd}" +
                "&order=date.asc,time.asc,id.asc&limit=250";
            var loadedAppointments = new List<AgendaLocalSnapshot>();
            for (var offset = 0; offset < 10000; offset += 250)
            {
                using var response = await Authorized(HttpMethod.Get, query + "&offset=" + offset);
                RequireSuccess(response);
                using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                loadedAppointments.AddRange(page.RootElement.EnumerateArray().Select(AgendaLocalSnapshot.Parse));
                if (page.RootElement.GetArrayLength() < 250) break;
                if (offset == 9750) throw new InvalidOperationException("Atendimentos demais neste período. Reduza a consulta.");
            }
            cachedRows.Clear();
            googleEventCount = googleTestEventCount = 0;
            string? googleWarning = null;
            localAppointments.Clear(); localAppointments.AddRange(loadedAppointments);
            supabaseEventCount = localAppointments.Count;
            calendarLinks = []; linksReady = false;
            IReadOnlyList<GoogleEventSnapshot> testEvents = [];
            var apiReadSucceeded = false;
            // No piloto, manter a agenda operacional iCal (somente leitura)
            // SEMPRE visível, mesmo depois de autorizar a API OAuth para MODUX - TESTES.
            // O Google API nunca substitui nem importa automaticamente eventos do iCal.

            if (googleOAuth.CanWriteSelectedCalendar)
            {
                // Apenas eventos do calendário SECUNDÁRIO MODUX - TESTES entram
                // no modo editável. Operacional continua identificado como leitura.
                try
                {
                    try { await googleApi.LoadColors(); }
                    catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                    { googleWarning = "Cores Google indisponíveis nesta atualização; usando classificação local."; }
                    testEvents = await googleApi.Events(first, last);
                    apiReadSucceeded = true;
                    googleTestEventCount = testEvents.Count;
                    calendarLinks = await linkStore.List(companyId, googleOAuth.CalendarId!);
                    linksReady = true;
                    // Um evento pode ter mudado de mês no Google. Buscar os dados
                    // operacionais vinculados pelo ID, sem depender da data antiga.
                    var eventIds = testEvents.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                    var missing = calendarLinks.Where(link => eventIds.Contains(link.EventId))
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
                    googleWarning = "Google / vínculos: " + ex.Message;
                }
            }
            if (googleFeed.HasUrl && !(apiReadSucceeded && googleFeed.MatchesCalendar(googleOAuth.CalendarId)))
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

            var googleOrigin = googleOAuth.IsTestCalendarSelected ? "TESTES" : "Google";
            var unified = AgendaCalendarProjection.Merge(companyId, googleOAuth.CalendarId ?? "",
                localAppointments, testEvents, calendarLinks);
            foreach (var row in unified)
            {
                var local = row.Local;
                var google = row.Google;
                var category = local is not null
                    ? serviceCatalog.Resolve(local)
                    : serviceCatalog.Apply(google!.PrivateProperties.ContainsKey("modux_service_ids")
                        ? AgendaServiceColors.FromServiceIds(google.ServiceTypeIds, null, null)
                        : google.ServiceTypeId is { } typeId ? AgendaServiceColors.FromServiceIds([typeId], google.Title, null)
                        : AgendaServiceColors.Classify(google.Title, google.Description));
                if (local is null && google is not null && google.ServiceTypeIds.Length > 0)
                    category = category with { Label = string.Join(" + ", google.ServiceTypeIds.Select(id =>
                        serviceCatalog.Items.FirstOrDefault(item => item.Id == id)?.Label ?? id)) };
                if (google is not null && googleApi.EventColorHex(google) is { } googleHex)
                {
                    var color = ColorTranslator.FromHtml(googleHex);
                    var brightness = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
                    category = category with { Color = color, TextColor = brightness > 145 ? Color.Black : Color.White,
                        GoogleColorName = "Cor original do Google" };
                }
                var state = google is not null
                    ? row.Link is null ? googleOrigin + " • editável"
                        : local is null ? googleOrigin + " • vínculo pendente"
                        : row.Link.ETag != google.ETag ? googleOrigin + " • conferir alteração" : googleOrigin + " • " + local.Status
                    : row.Link is null || row.Link.SyncStatus == "excluido" ? local!.Status : "Vínculo • conferir Google";
                cachedRows.Add(([
                    google?.Date.ToString("yyyy-MM-dd") ?? local!.Date,
                    google?.Time ?? local!.Time,
                    local?.Client ?? google!.Title,
                    local?.Service ?? category.Label, state, local?.Phone ?? "",
                    local?.Address ?? google!.Location
                ], category, google, local));
            }
            cachedRows.Sort((left, right) =>
                string.CompareOrdinal(left.Values[0] + left.Values[1],
                    right.Values[0] + right.Values[1]));
            loadedRange = envelope; loadedCompany = companyId; loadedCalendar = googleOAuth.CalendarId; loadedAt = googleWarning is null ? DateTimeOffset.UtcNow : DateTimeOffset.MinValue;
            ShowCachedRows();
            string? backupWarning = null;
            try
            {
                if (currentUserId is not null)
                    AgendaLocalBackup.Save(companyId, currentUserId, first, last, localAppointments,
                        calendarLinks, googleWarning is not null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or InvalidOperationException)
            { backupWarning = "Não foi possível gerar o checkpoint local do período."; }
            Status($"{grid.Rows.Count} eventos na lista • {visibleRange.First:dd/MM} a {visibleRange.Last:dd/MM}. " +
                (googleWarning ?? backupWarning ?? (googleOAuth.CanWriteSelectedCalendar
                    ? "Duplo clique para editar no calendário Google selecionado. Alterações exigem confirmação."
                    : "Agenda operacional iCal: somente leitura. Sem envio WhatsApp.")));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { Status("Não foi possível atualizar: " + error.Message); }
        finally { SetBusy(false); }
    }

    private void UpdateGoogleLabel()
    {
        googleButton.Text = googleFeed.HasUrl ? "iCal conectado ✓" : "Google iCal";
        var name = googleOAuth.CalendarName ?? "selecionado";
        if (name.Length > 22) name = name[..22] + "…";
        googleApiButton.Text = googleOAuth.CanWriteSelectedCalendar
            ? "Google: " + name + " ✓"
            : googleOAuth.Connected ? "Google • escolher agenda" : "Google (configurar)";
        googleNewButton.Enabled = googleOAuth.CanWriteSelectedCalendar;
    }

    private void ShowCachedRows()
    {
        searchDelay.Stop();
        grid.SuspendLayout();
        try
        {
        grid.Rows.Clear();
        var selectedService = (filterService.SelectedItem as AgendaServiceDefinition)?.Id;
        var visible = cachedRows.Where(row => DateOnly.TryParse(row.Values[0], out var date) && CalendarEntry(row.Values, row.Category, row.Google, row.Local).Start < calendar.DisplayRange.Last.AddDays(1).ToDateTime(TimeOnly.MinValue) && CalendarEntry(row.Values, row.Category, row.Google, row.Local).End > calendar.DisplayRange.First.ToDateTime(TimeOnly.MinValue) && AgendaViewFilter.Matches(row.Values, row.Local, row.Google,
            row.Category.Id, search.Text, filterState.SelectedIndex, filterOrigin.SelectedIndex, selectedService)).ToArray();
        var entries = new List<AgendaCalendarEntry>();
        foreach (var (record, category, google, local) in visible)
        {
            var entry = CalendarEntry(record, category, google, local);
            entries.Add(entry);
            if (entry.Start >= calendar.VisibleRange.Last.AddDays(1).ToDateTime(TimeOnly.MinValue) ||
                entry.End <= calendar.VisibleRange.First.ToDateTime(TimeOnly.MinValue)) continue;
            if (filterDate is { } chosen && (entry.Start >= chosen.AddDays(1).ToDateTime(TimeOnly.MinValue) ||
                entry.End <= chosen.ToDateTime(TimeOnly.MinValue))) continue;
            var index = grid.Rows.Add(record);
            grid.Rows[index].Tag = new AgendaRowContext(google, local);
            grid.Rows[index].Cells[2].ToolTipText = google is not null
                ? "Duplo clique para editar no calendário Google selecionado"
                : "Operacional: agendamento local ou iCal somente leitura";
            var state = record[4];
            var serviceCell = grid.Rows[index].Cells[3];
            var past = entry.IsPast(AgendaCalendarLayout.Now);
            serviceCell.Style.BackColor = AgendaCalendarLayout.Fade(category.Color, ModuxAppearance.Surface, past);
            serviceCell.Style.ForeColor = past ? ModuxAppearance.Muted : category.TextColor;
            serviceCell.ToolTipText = category.Label + " • " + category.GoogleColorName
                + (state == "Google • leitura" ? " (classificado pelo texto do evento)" : "");
            if (state == "Google • leitura")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(229, 241, 255);
            else if ((google is not null && state.StartsWith("Google •", StringComparison.Ordinal)) ||
                state.StartsWith("TESTES •", StringComparison.Ordinal))
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 221);
            else if (state is "cancelado" or "cancelar")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(164, 42, 42);
            else if (state is "confirmado")
                grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(0, 103, 44);
            else if (state is "revisao" or "reagendar")
                grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 223);
        }
        calendar.SetEntries(entries);
        }
        finally { grid.ResumeLayout(); }
    }

    private void ExportVisibleRows()
    {
        if (busy || companyId is null) return;
        using var chooser = new SaveFileDialog
        {
            Title = "Exportar linhas visíveis da agenda", Filter = "Planilha CSV (*.csv)|*.csv",
            FileName = "Agenda-MODUX-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv"
        };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rows = grid.Rows.Cast<DataGridViewRow>().Select(row =>
                (IReadOnlyList<string>)row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "").ToArray());
            File.WriteAllText(chooser.FileName, AgendaExport.Csv(rows), new UTF8Encoding(true));
            Status("Linhas visíveis exportadas. Nenhum dado da agenda foi alterado.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "Não foi possível salvar o arquivo. Confira a pasta de destino.", "Exportação"); }
    }

    private void UpdateLinkButton()
    {
        inviteGoogleButton.Enabled = !busy && googleOAuth.CanWriteSelectedCalendar &&
            grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Tag is AgendaRowContext { Google: not null };
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

    private async Task InviteSelectedGoogleEvent()
    {
        if (busy || !googleOAuth.CanWriteSelectedCalendar || grid.SelectedRows.Count != 1 ||
            grid.SelectedRows[0].Tag is not AgendaRowContext { Google: { } google } context) return;
        using var prompt = new Form
        {
            Text = "Convite Google • " + googleOAuth.CalendarName, Width = 660, Height = 310,
            StartPosition = FormStartPosition.CenterParent, Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var address = new TextBox { Dock = DockStyle.Top, MaxLength = 254, Text = context.Local?.Email ?? "" };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 145, Padding = new Padding(12),
            Text = "Evento: " + google.Title + "\n" + google.Date.ToString("dd/MM/yyyy") + " • " + google.Time +
                "\n\nConfira o e-mail do cliente. O Google enviará o convite e poderá notificar todos os convidados existentes." +
                (string.IsNullOrEmpty(google.RecurringEventId) ? "" : "\nSomente esta ocorrência será alterada.")
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
        var next = new Button { Text = "Revisar convite", AutoSize = true, DialogResult = DialogResult.OK };
        var close = new Button { Text = "Voltar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([close, next]);
        prompt.Controls.Add(address); prompt.Controls.Add(hint); prompt.Controls.Add(buttons);
        prompt.CancelButton = close;
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var target = address.Text.Trim();
        if (MessageBox.Show(this, "Enviar convite Google para " + target + "?\n\n" +
            "Os demais convidados também podem receber atualização. Se o e-mail já estiver convidado, nenhum novo envio será solicitado.",
            "Confirmar convite", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            await googleApi.Invite(google, target);
            Status("Lista de convidados confirmada pelo Google. Nenhuma mensagem de WhatsApp foi enviada.");
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        { MessageBox.Show(this, error.Message, "Conferir convite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        await LoadAppointments();
    }

    private async Task ShowWorkflow()
    {
        if (busy || companyId is null) return;
        using var dialog = new AgendaWorkflowDialog(workflowStore, companyId, currentRole == "admin",
            work => workflowStore.ValidateSchedule(companyId!, work, googleOAuth.CalendarId, id => googleApi.Event(id)));
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
        if (MessageBox.Show(this, $"Criar ou recuperar no calendário Google “{googleOAuth.CalendarName}” o evento do atendimento de {local.Client}? " +
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
            throw new InvalidOperationException("Selecione e confirme primeiro o calendário Google.");
        var calendarId = googleOAuth.CalendarId!;
        var draft = local.AsGoogleDraft(companyId);
        var type = GoogleEventTypes().FirstOrDefault(candidate => local.ServiceIds.Contains(candidate.Id));
        if (type is not null) draft = draft with { ServiceTypeIds = local.ServiceIds, SelectedColorId = type.ColorId };
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
            Status("Edição bloqueada: selecione e confirme o calendário Google.");
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
        using var editor = new GoogleCalendarEventDialog(google, google.Date, local is not null && link is not null,
            GoogleEventTypes(), local?.ServiceIds);
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
                {
                    if (draft.ServiceTypeIds is { } ids)
                        local = await appointmentStore.UpdateServices(companyId, local, ids,
                            string.Join(" + ", ids.Select(id => serviceCatalog.Items.FirstOrDefault(item => item.Id == id)?.Label ?? id)));
                    await linkStore.Reconcile(companyId, link, local, updated);
                }
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

    private IEnumerable<GoogleEventTypeChoice> GoogleEventTypes()
        => serviceCatalog.Items.Where(item => item.Id != "outros")
            .Select(item => new GoogleEventTypeChoice(item.Id, item.Label, googleApi.ClosestColor(item.ColorHex)));

    private async Task CreateGoogleEvent()
    {
        if (busy || !googleOAuth.CanWriteSelectedCalendar)
        {
            Status("Para criar eventos, selecione e confirme o calendário Google.");
            return;
        }
        using var editor = new GoogleCalendarEventDialog(null, calendar.SelectedDate, eventTypes: GoogleEventTypes());
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
        using var dialog = new AgendaAppointmentDialog(calendar.SelectedDate, googleOAuth.CanWriteSelectedCalendar,
            sharedServices: serviceCatalog.Items, calendarName: googleOAuth.CalendarName);
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
            suspendRefresh = true; startDate.Value = date.ToDateTime(TimeOnly.MinValue); suspendRefresh = false;
            calendar.SetView(date, SelectedMode);
            await LoadAppointments();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            googleRefreshTimer.Stop();
            googleRefreshTimer.Dispose();
            searchDelay.Dispose();
            googleFeed.Dispose();
            googleApi.Dispose();
            googleOAuth.Dispose();
            http.Dispose();
        }
        base.Dispose(disposing);
    }
}
