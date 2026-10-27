using System.Text.Json;
using System.Drawing.Drawing2D;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentralWhatsApp.WebView2;

public sealed class MainForm : Form
{
    private sealed record UserPreferences(
        string Theme = "Claro",
        string FontFamily = "Segoe UI",
        int InterfaceSize = 100,
        int WhatsAppZoom = 100);

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListCom { }

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
        [PreserveSig] int RegisterTab(IntPtr tab, IntPtr mdi);
        [PreserveSig] int UnregisterTab(IntPtr tab);
        [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr insertBefore);
        [PreserveSig] int SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
        [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly string appDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CentralWhatsApp",
        "WebView2Prototype");

    private const string WaSellerExtensionId = "illemhbijpiebjfilfmgebahaakajkpe";
    private const int ExpandedSidebarWidth = 245;
    private const int CollapsedSidebarWidth = 72;
    private readonly Panel sidebar = new();
    private readonly Label brand = new();
    private readonly FlowLayoutPanel toolbar = new();
    private readonly Panel sidebarBottom = new();
    private readonly System.Windows.Forms.Timer unreadTimer = new() { Interval = 5000 };
    private bool refreshingIndicators;
    private bool exitRequested;
    private bool trayNoticeShown;
    private string? detectedWaSellerPath;
    private readonly NotifyIcon trayIcon = new();
    private readonly Image? brandLogo;
    private readonly HashSet<string> notificationBaselines = [];
    private ITaskbarList3? taskbar;
    private IntPtr taskbarOverlayIcon;
    private string? lastNotifiedAccountId;
    private bool sidebarCollapsed;
    private bool composeReady;
    private bool handlingCompose;
    private readonly Queue<ComposeRequest> composeRequests = new();
    private UserPreferences preferences = new();

    private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : "1.5.2";

    private readonly FlowLayoutPanel accountsPanel = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(10)
    };

    private readonly Panel browserHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White
    };

    private readonly Button addAccountButton = CreateToolbarButton("+ Adicionar conta");
    private readonly Button manageAccountsButton = CreateToolbarButton("⚙ Gerenciar contas");
    private readonly Button preferencesButton = CreateToolbarButton("◈ Preferências");
    private readonly Button aboutButton = CreateToolbarButton("Sobre o MODUX");
    private readonly Panel welcomePanel = new() { Dock = DockStyle.Fill, Tag = "modux-content" };
    private readonly Button collapseButton = CreateToolbarButton("◀");
    private readonly Button installButton = CreateToolbarButton("Instalar WaSeller nesta conta");
    private readonly Button reloadButton = CreateToolbarButton("Recarregar");
    private readonly Button clearCacheButton = CreateToolbarButton("Limpar cache");
    private readonly PictureBox activeAccountPicture = new()
    {
        Width = 42,
        Height = 42,
        SizeMode = PictureBoxSizeMode.Zoom,
        Margin = new Padding(12, 3, 4, 3),
        BackColor = Color.Transparent
    };
    private readonly Panel activeAccountCard = new()
    {
        Width = 310,
        Height = 48,
        Margin = new Padding(8, 3, 8, 3),
        BackColor = Color.FromArgb(16, 29, 58)
    };
    private readonly Label activeMarkerLabel = new()
    {
        Text = "EM USO AGORA",
        AutoSize = true,
        ForeColor = Color.FromArgb(6, 182, 212),
        Font = new Font("Segoe UI", 7f, FontStyle.Bold),
        Left = 50,
        Top = 4
    };
    private readonly Label activeNameLabel = new()
    {
        Text = "Aguardando conta…",
        AutoEllipsis = true,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Left = 50,
        Top = 17,
        Width = 245,
        Height = 18
    };
    private readonly Label activeDetailsLabel = new()
    {
        Text = "",
        AutoEllipsis = true,
        ForeColor = Color.FromArgb(156, 163, 175),
        Font = new Font("Segoe UI", 7.5f),
        Left = 50,
        Top = 34,
        Width = 245,
        Height = 13
    };
    private readonly Label activeChevronLabel = new()
    {
        Text = "⌄",
        ForeColor = Color.FromArgb(6, 182, 212),
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        Left = 286,
        Top = 15,
        Width = 18,
        Height = 24,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.Transparent
    };
    private readonly Label statusLabel = new()
    {
        Text = "Iniciando…",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
        Margin = new Padding(12, 15, 8, 8)
    };

    private readonly List<ModuxAccountInfo> accounts = [];
    private readonly Dictionary<string, Microsoft.Web.WebView2.WinForms.WebView2> browsers = [];
    private ModuxAccountInfo? activeAccount;

    public MainForm() : this(null, true) { }

    // Isolated Windows smoke tests supply a temporary profile and skip browser/tray services.
    internal MainForm(string? dataFolder, bool initializeServices)
    {
        if (dataFolder is not null) appDataFolder = dataFolder;
        Text = $"MODUX {AppVersion} — Gestão Multicontas";
        Width = Math.Min(1440, Screen.PrimaryScreen?.WorkingArea.Width ?? 1440);
        Height = Math.Min(900, Screen.PrimaryScreen?.WorkingArea.Height ?? 900);
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 19, 43);
        brandLogo = LoadBrandLogo();
        var appIcon = LoadBrandIcon();
        if (appIcon is not null) Icon = appIcon;
        if (initializeServices) ConfigureTrayIcon();

        sidebar.Dock = DockStyle.Left;
        sidebar.Width = ExpandedSidebarWidth;
        sidebar.BackColor = Color.FromArgb(6, 12, 28);
        sidebar.Padding = new Padding(0, 10, 0, 0);
        brand.Dock = DockStyle.Top;
        brand.Height = 76;
        brand.Text = "";
        brand.ForeColor = Color.White;
        brand.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        brand.TextAlign = ContentAlignment.MiddleLeft;
        brand.Paint += DrawBrand;
        sidebarBottom.Dock = DockStyle.Bottom;
        sidebarBottom.Height = 206;
        sidebarBottom.Padding = new Padding(10, 4, 10, 8);
        manageAccountsButton.Dock = DockStyle.Top;
        manageAccountsButton.Height = 42;
        preferencesButton.Dock = DockStyle.Top;
        preferencesButton.Height = 42;
        aboutButton.Dock = DockStyle.Top;
        aboutButton.Height = 42;
        addAccountButton.Dock = DockStyle.Bottom;
        addAccountButton.Height = 42;
        sidebarBottom.Controls.Add(preferencesButton);
        sidebarBottom.Controls.Add(manageAccountsButton);
        sidebarBottom.Controls.Add(aboutButton);
        sidebarBottom.Controls.Add(addAccountButton);
        sidebar.Controls.Add(accountsPanel);
        sidebar.Controls.Add(sidebarBottom);
        sidebar.Controls.Add(brand);

        toolbar.Dock = DockStyle.Top;
        toolbar.Height = 76;
        toolbar.FlowDirection = FlowDirection.LeftToRight;
        toolbar.WrapContents = false;
        toolbar.BackColor = Color.FromArgb(11, 19, 43);
        toolbar.Padding = new Padding(12, 10, 12, 8);
        toolbar.AutoScroll = true;
        collapseButton.Width = 44;
        collapseButton.Padding = new Padding(4);
        collapseButton.AccessibleName = "Recolher ou abrir menu de contas";
        activeAccountPicture.Location = new Point(4, 3);
        activeAccountPicture.Margin = new Padding(0);
        activeAccountCard.Controls.Add(activeAccountPicture);
        activeAccountCard.Controls.Add(activeMarkerLabel);
        activeAccountCard.Controls.Add(activeNameLabel);
        activeAccountCard.Controls.Add(activeDetailsLabel);
        activeAccountCard.Controls.Add(activeChevronLabel);
        toolbar.Controls.Add(collapseButton);
        toolbar.Controls.Add(activeAccountCard);
        toolbar.Controls.Add(installButton);
        toolbar.Controls.Add(reloadButton);
        toolbar.Controls.Add(clearCacheButton);
        statusLabel.AutoSize = false;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 30;
        statusLabel.Padding = new Padding(18, 6, 10, 0);
        foreach (var button in new[] { collapseButton, installButton, reloadButton, clearCacheButton })
            ModuxAppearance.Role(button, "chrome");
        foreach (var button in new[] { addAccountButton, manageAccountsButton, preferencesButton, aboutButton })
            ModuxAppearance.Role(button, "sidebar");
        foreach (var label in new[] { activeMarkerLabel, activeNameLabel, activeDetailsLabel, activeChevronLabel, statusLabel })
            label.Tag = "chrome-text";
        BuildWelcome();
        browserHost.Controls.Add(welcomePanel);

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(browserHost);
        rightPanel.Controls.Add(statusLabel);
        rightPanel.Controls.Add(toolbar);

        Controls.Add(rightPanel);
        Controls.Add(sidebar);

        collapseButton.Click += (_, _) => ToggleSidebar();
        activeAccountCard.Cursor = Cursors.Hand;
        foreach (Control control in activeAccountCard.Controls)
        {
            control.Cursor = Cursors.Hand;
            control.Click += ShowAccountSwitcher;
        }
        activeAccountCard.Click += ShowAccountSwitcher;
        manageAccountsButton.Click += ManageAccounts;
        preferencesButton.Click += ShowPreferences;
        aboutButton.Click += (_, _) => ShowAbout();
        addAccountButton.Click += AddAccount;
        installButton.Click += InstallExtension;
        reloadButton.Click += (_, _) => ActiveBrowser()?.CoreWebView2?.Reload();
        clearCacheButton.Click += ClearCache;
        unreadTimer.Tick += async (_, _) =>
        {
            try { await RefreshUnreadCounts(); }
            catch (Exception error) { Log($"Falha não fatal ao atualizar contadores: {error}"); }
        };
        Shown += async (_, _) =>
        {
            if (!initializeServices) return;
            try
            {
                await Start();
                composeReady = true;
                await DrainComposeRequests();
                // O botão da janela precisa existir na barra do Windows antes de receber o selo.
                await Task.Delay(750);
                InitializeTaskbar();
                UpdateTaskbarBadge();
                unreadTimer.Start();
            }
            catch (Exception error)
            {
                Log($"Falha durante a inicialização: {error}");
                MessageBox.Show("O MODUX encontrou um problema ao iniciar. Consulte o arquivo modux.log.",
                    "MODUX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Activated += (_, _) => UpdateTaskbarBadge();
        FormClosing += HandleFormClosing;
        HandleCreated += (_, _) => { if (initializeServices) InitializeTaskbar(); };
    }

    private static Button CreateToolbarButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = Color.FromArgb(29, 78, 216),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(8),
        Padding = new Padding(12, 4, 12, 4)
    };

    private void ConfigureTrayIcon()
    {
        trayIcon.Text = $"MODUX {AppVersion}";
        trayIcon.Icon = Icon;
        trayIcon.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir MODUX", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Encerrar", null, (_, _) =>
        {
            exitRequested = true;
            trayIcon.Visible = false;
            Close();
        });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        trayIcon.BalloonTipClicked += async (_, _) =>
        {
            RestoreFromTray();
            if (lastNotifiedAccountId is not null &&
                accounts.FirstOrDefault(item => item.Id == lastNotifiedAccountId) is { } account)
                await ActivateAccount(account);
        };
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (exitRequested || e.CloseReason == CloseReason.WindowsShutDown) return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        if (!trayNoticeShown)
        {
            trayNoticeShown = true;
            trayIcon.BalloonTipTitle = "MODUX continua ativo";
            trayIcon.BalloonTipText = "O programa ficou perto do relógio. Clique duas vezes no ícone para abrir.";
            trayIcon.ShowBalloonTip(4000);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        BeginInvoke(async () =>
        {
            await Task.Delay(300);
            InitializeTaskbar();
            UpdateTaskbarBadge();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            unreadTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            brandLogo?.Dispose();
            activeAccountPicture.Image?.Dispose();
            if (taskbar is not null && IsHandleCreated)
                taskbar.SetOverlayIcon(Handle, IntPtr.Zero, string.Empty);
            if (taskbarOverlayIcon != IntPtr.Zero)
                DestroyIcon(taskbarOverlayIcon);
        }
        base.Dispose(disposing);
    }

    internal void QueueCompose(ComposeRequest request)
    {
        if (composeRequests.Count >= 10) return;
        composeRequests.Enqueue(request);
        if (composeReady) _ = DrainComposeRequests();
    }

    private async Task DrainComposeRequests()
    {
        if (!composeReady || handlingCompose) return;
        handlingCompose = true;
        try
        {
            while (composeRequests.TryDequeue(out var request))
            {
                RestoreFromTray();
                if (accounts.Count == 0)
                {
                    MessageBox.Show("Adicione uma conta do WhatsApp ao MODUX antes de abrir a mensagem.", "MODUX");
                    continue;
                }
                var agendaOrigin = request.Source == "saobento";
                var origin = agendaOrigin ? "Agenda" : "Integração Criatta";
                var preferencePath = Path.Combine(appDataFolder, agendaOrigin ? "saobento-account-v1.txt" : "criatta-account-v2.txt");
                string? preferred = null;
                try { if (File.Exists(preferencePath)) preferred = File.ReadAllText(preferencePath); } catch { }
                using var preview = new Form { Text = origin + " · Revisar mensagem", Width = 620, Height = 560,
                    StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
                var description = new Label { Text = $"Escolha o WhatsApp da {origin}. Esta conta ficará vinculada aos próximos rascunhos desta origem.", Dock = DockStyle.Fill };
                var accountBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
                foreach (var account in accounts.ToArray()) accountBox.Items.Add(account);
                var selectedId = ComposeAccountSelection.Resolve(preferred, accounts.Select(a => a.Id));
                accountBox.SelectedIndex = -1;
                if (selectedId is not null)
                    accountBox.SelectedItem = accounts.First(a => a.Id == selectedId);
                var accountLabel = new Label { Text = $"Conta da {origin} (obrigatório):", Dock = DockStyle.Fill };
                var recipient = new Label { Text = "Destinatário: +" + request.Phone, Dock = DockStyle.Fill };
                var text = new TextBox { Text = request.Text, Multiline = true, ReadOnly = false, MaxLength = ComposeRequest.MaxTextLength,
                    ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
                var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var open = new Button { Text = "Selecione a conta", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
                void UpdateComposeAccount()
                {
                    open.Enabled = accountBox.SelectedItem is ModuxAccountInfo;
                    open.Text = accountBox.SelectedItem is ModuxAccountInfo choice ? $"Preparar em {choice.Name}" : "Selecione a conta";
                }
                accountBox.SelectedIndexChanged += (_, _) => UpdateComposeAccount();
                UpdateComposeAccount();
                actions.Controls.Add(open); actions.Controls.Add(cancel);
                layout.Controls.Add(description); layout.Controls.Add(accountLabel); layout.Controls.Add(accountBox); layout.Controls.Add(recipient);
                layout.Controls.Add(text); layout.Controls.Add(actions);
                preview.Controls.Add(layout); preview.CancelButton = cancel;
                if (preview.ShowDialog(this) != DialogResult.OK || accountBox.SelectedItem is not ModuxAccountInfo selected) continue;
                if (string.IsNullOrWhiteSpace(text.Text) || text.Text.Length > ComposeRequest.MaxTextLength ||
                    text.Text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                { MessageBox.Show("Confira o texto da mensagem.", "MODUX"); continue; }
                await ActivateAccount(selected);
                if (!browsers.TryGetValue(selected.Id, out var browser) || browser.CoreWebView2 is null) continue;
                try { File.WriteAllText(preferencePath, selected.Id); } catch { }
                browser.CoreWebView2.Navigate((request with { Text = text.Text }).WhatsAppUrl);
                statusLabel.Text = $"{selected.Name} — abrindo o rascunho. O WhatsApp pode recarregar; o envio é manual.";
            }
        }
        catch (Exception error)
        {
            Log("Falha ao preparar rascunho: " + error.GetType().Name);
            MessageBox.Show("Não foi possível preparar a mensagem. O envio não foi executado pelo MODUX.", "MODUX");
        }
        finally { handlingCompose = false; }
    }

    private string AccountsFile => Path.Combine(appDataFolder, "accounts.json");
    private string PreferencesFile => Path.Combine(appDataFolder, "preferences.json");
    private string CacheFlagFile => Path.Combine(appDataFolder, "clear-cache.flag");
    private string LogFile => Path.Combine(appDataFolder, "modux.log");

    private async Task Start()
    {
        Directory.CreateDirectory(appDataFolder);
        Log("Inicialização iniciada.");
        if (File.Exists(CacheFlagFile))
        {
            Log("Limpeza de cache pendente encontrada.");
            DeleteCacheFolders();
            try { File.Delete(CacheFlagFile); } catch { }
        }
        detectedWaSellerPath = FindWaSellerFolder();
        Log(detectedWaSellerPath is null ? "WaSeller não encontrado nos navegadores." : "WaSeller localizado no computador.");
        LoadAccounts();
        LoadPreferences();
        ApplyPreferences();
        RenderAccountButtons();
        UpdateEmptyState();
        if (accounts.Count > 0 && composeRequests.Count == 0) await ActivateAccount(accounts[0]);
        Log("Inicialização concluída.");
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void BuildWelcome()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
            Padding = new Padding(28), BackColor = Color.Transparent, Tag = "canvas"
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var card = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 620,
            MinimumSize = new Size(620, 0), MaximumSize = new Size(620, 0),
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(40), Margin = Padding.Empty, Tag = "card"
        };
        card.Controls.Add(new Label { Text = "M O D U X", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ModuxAppearance.Muted, Tag = "muted", Margin = new Padding(0, 0, 0, 20) });
        card.Controls.Add(new Label { Text = "Tudo no seu lugar.", AutoSize = true, Font = new Font("Segoe UI", 28, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 16) });
        card.Controls.Add(new Label { Text = "Suas conversas e sua rotina, em um só espaço.\nAdicione a primeira conta para começar.",
            AutoSize = true, Font = new Font("Segoe UI", 12), Margin = new Padding(0, 0, 0, 28) });
        var begin = CreateToolbarButton("+  Adicionar minha primeira conta");
        begin.MinimumSize = new Size(290, 46); begin.Margin = new Padding(0, 0, 0, 24);
        ModuxAppearance.Role(begin, "primary");
        begin.Click += AddAccount;
        card.Controls.Add(begin);
        card.Controls.Add(new Label { Text = "Contas independentes. Organização sem distrações.", AutoSize = true,
            ForeColor = ModuxAppearance.Muted, Tag = "muted", Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 28) });
        var credit = new LinkLabel { Text = "Desenvolvido e disponibilizado por ARANY  ↗", AutoSize = true,
            LinkColor = Color.FromArgb(100, 117, 186), ActiveLinkColor = Color.FromArgb(100, 117, 186),
            LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font("Segoe UI", 9), Margin = Padding.Empty };
        credit.LinkClicked += (_, _) => OpenArany();
        card.Controls.Add(credit);
        layout.Controls.Add(card, 1, 1);
        welcomePanel.Controls.Add(layout);
    }

    private void UpdateEmptyState()
    {
        var empty = accounts.Count == 0;
        welcomePanel.Visible = empty && !showingAgenda;
        installButton.Enabled = reloadButton.Enabled = clearCacheButton.Enabled = !empty && !showingAgenda;
        installButton.Visible = reloadButton.Visible = clearCacheButton.Visible = !empty;
        if (!empty) return;
        activeAccount = null;
        activeAccountPicture.Image?.Dispose(); activeAccountPicture.Image = null;
        activeMarkerLabel.Text = "SEU ESPAÇO DE TRABALHO";
        activeNameLabel.Text = "Bem-vindo ao MODUX";
        activeDetailsLabel.Text = "Adicione sua primeira conta";
        statusLabel.Text = "Pronto para começar";
        Text = $"MODUX {AppVersion} — Gestão Multicontas";
        if (welcomePanel.Visible) welcomePanel.BringToFront();
    }

    private static void OpenArany()
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.arany.com.br") { UseShellExecute = true });

    private void ShowAbout()
    {
        using var about = CreateAboutDialog();
        about.ShowDialog(this);
    }

    private Form CreateAboutDialog()
    {
        var about = new Form
        {
            Text = "Sobre o MODUX", Width = 580, Height = 500,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = CreateInterfaceFont(10)
        };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(36), AutoScroll = true
        };
        content.Controls.Add(new Label { Text = "MODUX", AutoSize = true, Font = CreateInterfaceFont(30, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) });
        content.Controls.Add(new Label { Text = $"Versão {AppVersion}  ·  Windows", AutoSize = true, Margin = new Padding(0, 0, 0, 24) });
        content.Controls.Add(new Label { Text = "Um espaço para organizar suas conversas,\nsuas contas e sua rotina de trabalho.",
            AutoSize = true, Margin = new Padding(0, 0, 0, 28) });
        content.Controls.Add(new Label { Text = "Desenvolvido e disponibilizado por ARANY", AutoSize = true,
            Font = CreateInterfaceFont(11, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) });
        var website = new LinkLabel { Text = "www.arany.com.br  ↗", AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = Color.FromArgb(100, 117, 186), Margin = new Padding(0, 0, 0, 28), AccessibleName = "Visitar o site da ARANY" };
        website.LinkClicked += (_, _) => OpenArany();
        content.Controls.Add(website);
        content.Controls.Add(new Label { Text = "Tecnologia com clareza. Mais espaço para o que importa.",
            AutoSize = true, Font = CreateInterfaceFont(9), Margin = Padding.Empty });
        var close = CreateToolbarButton("Fechar"); close.DialogResult = DialogResult.OK;
        ModuxAppearance.Role(close, "primary");
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(18, 8, 18, 8), FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(close);
        about.Controls.Add(content); about.Controls.Add(footer); about.AcceptButton = close; about.CancelButton = close;
        return about;
    }

    private void LoadAccounts()
    {
        try { accounts.AddRange(ModuxAccountStorage.Load(appDataFolder)); }
        catch (Exception error)
        {
            Log("Falha ao ler contas: " + error.Message);
            MessageBox.Show(error.Message, "Contas do MODUX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveAccounts() => ModuxAccountStorage.Save(appDataFolder, accounts);

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(PreferencesFile))
                preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(PreferencesFile)) ?? new();
        }
        catch (Exception error) { Log($"Preferências inválidas; usando padrão: {error.Message}"); }
    }

    private void SavePreferences()
    {
        Directory.CreateDirectory(appDataFolder);
        File.WriteAllText(PreferencesFile, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ToggleSidebar()
    {
        sidebarCollapsed = !sidebarCollapsed;
        sidebar.Width = sidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
        brand.Invalidate();
        addAccountButton.Text = sidebarCollapsed ? "+" : "+ Adicionar conta";
        manageAccountsButton.Text = sidebarCollapsed ? "⚙" : "⚙ Gerenciar contas";
        preferencesButton.Text = sidebarCollapsed ? "◈" : "◈ Preferências";
        aboutButton.Text = sidebarCollapsed ? "i" : "Sobre o MODUX";
        aboutButton.AccessibleName = "Sobre o MODUX e a ARANY";
        collapseButton.Text = sidebarCollapsed ? "☰" : "◀";
        RenderAccountButtons();
    }

    private async void ShowAccountSwitcher(object? sender, EventArgs e)
    {
        if (accounts.Count == 0) return;
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            BackColor = IsLightTheme() ? Color.White : Color.FromArgb(16, 29, 58),
            ForeColor = IsLightTheme() ? Color.FromArgb(11, 19, 43) : Color.White,
            Font = CreateInterfaceFont(10f),
            Padding = new Padding(6),
            AutoSize = true
        };
        menu.Items.Add(new ToolStripLabel("ESCOLHA O WHATSAPP")
        {
            ForeColor = Color.FromArgb(6, 182, 212),
            Font = CreateInterfaceFont(8f, FontStyle.Bold),
            Margin = new Padding(8, 4, 8, 7)
        });
        foreach (var account in accounts.ToArray())
        {
            var countText = account.UnreadCount > 0 ? $"   •   {account.UnreadCount} não lidas" : "   •   sem novas mensagens";
            var item = new ToolStripMenuItem($"{account.Name}{countText}", BuildAccountIcon(account))
            {
                Tag = account,
                Checked = activeAccount?.Id == account.Id,
                CheckOnClick = false,
                AutoSize = false,
                Width = 350,
                Height = 54,
                ImageScaling = ToolStripItemImageScaling.None,
                BackColor = activeAccount?.Id == account.Id
                    ? (IsLightTheme() ? Color.FromArgb(224, 242, 254) : Color.FromArgb(25, 45, 70))
                    : menu.BackColor,
                ForeColor = menu.ForeColor
            };
            item.Click += async (_, _) =>
            {
                menu.Close();
                if (item.Tag is ModuxAccountInfo selected) await ActivateAccount(selected);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var manage = menu.Items.Add("Gerenciar contas…");
        manage.Click += ManageAccounts;
        menu.Show(activeAccountCard, new Point(0, activeAccountCard.Height + 2));
        await Task.CompletedTask;
    }

    private void RenderAccountButtons()
    {
        foreach (Control old in accountsPanel.Controls.Cast<Control>().ToArray()) old.Dispose();
        accountsPanel.Controls.Clear();
        foreach (var account in accounts.ToArray())
        {
            var button = new Button
            {
                Text = sidebarCollapsed ? "" : account.Name,
                Width = sidebarCollapsed ? 48 : 205,
                Height = 58,
                TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = sidebarCollapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft,
                TextImageRelation = sidebarCollapsed ? TextImageRelation.Overlay : TextImageRelation.ImageBeforeText,
                Padding = sidebarCollapsed ? new Padding(0) : new Padding(8, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                Font = CreateInterfaceFont(9f, FontStyle.Bold),
                ForeColor = ModuxAppearance.Text,
                BackColor = activeAccount?.Id == account.Id
                    ? ModuxAppearance.Selection : ModuxAppearance.Surface,
                Margin = new Padding(0, 0, 0, 8),
                Tag = account,
                Image = BuildAccountIcon(account)
            };
            ModuxAppearance.Role(button, "account");
            button.FlatAppearance.BorderColor = activeAccount?.Id == account.Id ? ModuxAppearance.Accent : ModuxAppearance.Border;
            button.Click += async (_, _) => await ActivateAccount(account);
            accountsPanel.Controls.Add(button);
        }
    }

    private async Task ActivateAccount(ModuxAccountInfo account)
    {
        activeAccount = account;
        welcomePanel.Visible = false;
        RenderAccountButtons();

        foreach (var existing in browsers.Values.ToArray()) existing.Visible = false;

        if (!browsers.TryGetValue(account.Id, out var browser))
        {
            browser = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false
            };
            browsers[account.Id] = browser;
            browserHost.Controls.Add(browser);
            browser.BringToFront();
            statusLabel.Text = $"Iniciando {account.Name}…";

            try
            {
                var userDataFolder = account.Id == "waseller-account"
                    ? Path.Combine(appDataFolder, "waseller-account")
                    : Path.Combine(appDataFolder, "profiles", account.Id);
                Directory.CreateDirectory(userDataFolder);

                var options = new CoreWebView2EnvironmentOptions
                {
                    AreBrowserExtensionsEnabled = true,
                    Language = "pt-BR"
                };
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await browser.EnsureCoreWebView2Async(environment);
                browser.ZoomFactor = preferences.WhatsAppZoom / 100d;
                browser.CoreWebView2.IsMuted = false;
                browser.CoreWebView2.PermissionRequested += (_, args) =>
                {
                    if (args.PermissionKind == CoreWebView2PermissionKind.Notifications &&
                        Uri.TryCreate(args.Uri, UriKind.Absolute, out var permissionUri) &&
                        permissionUri.Host.Equals("web.whatsapp.com", StringComparison.OrdinalIgnoreCase))
                    {
                        args.State = CoreWebView2PermissionState.Allow;
                        args.SavesInProfile = true;
                    }
                };
                browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                browser.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    OpenExternalLink(args.Uri);
                };
                browser.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (!IsWhatsAppWebAddress(args.Uri))
                    {
                        args.Cancel = true;
                        OpenExternalLink(args.Uri);
                    }
                };
                browser.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    if (args.IsSuccess) await CaptureAvatarWithRetries(account.Id, browser);
                };
                browser.CoreWebView2.Navigate("https://web.whatsapp.com/");
            }
            catch (Exception error)
            {
                MessageBox.Show($"Não foi possível abrir esta conta.\n\n{error.Message}", "MODUX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        browser.Visible = true;
        browser.BringToFront();
        UpdateActiveAccountHeader();
        _ = CaptureAvatarWithRetries(account.Id, browser);
    }

    private Image BuildAccountIcon(ModuxAccountInfo account)
    {
        using var avatar = LoadAvatar(account);
        var icon = new Bitmap(52, 52);
        using var graphics = Graphics.FromImage(icon);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        var sourceSide = Math.Min(avatar.Width, avatar.Height);
        var source = new Rectangle(
            (avatar.Width - sourceSide) / 2,
            (avatar.Height - sourceSide) / 2,
            sourceSide,
            sourceSide);
        var destination = new Rectangle(4, 8, 40, 40);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(destination);
            graphics.SetClip(path);
            graphics.DrawImage(avatar, destination, source, GraphicsUnit.Pixel);
            graphics.ResetClip();
        }
        using (var border = new Pen(Color.FromArgb(6, 182, 212), 1.5f))
            graphics.DrawEllipse(border, destination);

        if (account.UnreadCount > 0)
        {
            var badgeText = account.UnreadCount > 99 ? "99+" : account.UnreadCount.ToString();
            var badgeWidth = account.UnreadCount > 99 ? 25 : 20;
            var badgeRect = new Rectangle(52 - badgeWidth, 0, badgeWidth, 20);
            using var badgeBrush = new SolidBrush(Color.FromArgb(6, 182, 212));
            graphics.FillEllipse(badgeBrush, badgeRect);
            using var font = new Font("Segoe UI", account.UnreadCount > 99 ? 7 : 8, FontStyle.Bold);
            TextRenderer.DrawText(graphics, badgeText, font, badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return icon;
    }

    private Image LoadAvatar(ModuxAccountInfo account)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(account.AvatarData))
            {
                var comma = account.AvatarData.IndexOf(',');
                var bytes = Convert.FromBase64String(comma >= 0 ? account.AvatarData[(comma + 1)..] : account.AvatarData);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(Image.FromStream(stream), new Size(64, 64));
            }
        }
        catch { }

        var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(5, 150, 105));
        graphics.FillEllipse(brush, 0, 0, 63, 63);
        var initials = string.Concat(account.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(word => char.ToUpperInvariant(word[0])));
        using var font = new Font("Segoe UI", 16, FontStyle.Bold);
        TextRenderer.DrawText(graphics, initials, font, new Rectangle(0, 0, 64, 64), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bitmap;
    }

    private static Icon? LoadBrandIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "modux.ico");
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch { return null; }
    }

    private static Image? LoadBrandLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "modux-icon.png");
            return File.Exists(path) ? Image.FromFile(path) : null;
        }
        catch { return null; }
    }

    private void DrawBrand(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var logoSize = 43;
        var logoX = sidebarCollapsed ? (brand.Width - logoSize) / 2 : 10;
        var logoY = (brand.Height - logoSize) / 2;
        if (brandLogo is not null)
            e.Graphics.DrawImage(brandLogo, new Rectangle(logoX, logoY, logoSize, logoSize));

        if (!sidebarCollapsed)
        {
            using var titleFont = CreateInterfaceFont(13f, FontStyle.Bold);
            using var subtitleFont = CreateInterfaceFont(7.5f, FontStyle.Regular);
            using var titleBrush = new SolidBrush(IsLightTheme() ? Color.FromArgb(11, 19, 43) : Color.White);
            e.Graphics.DrawString("MODUX", titleFont, titleBrush, logoX + 53, logoY + 4);
            using var cyan = new SolidBrush(ModuxAppearance.Muted);
            e.Graphics.DrawString($"GESTÃO MULTICONTAS  •  v{AppVersion}", subtitleFont, cyan, logoX + 54, logoY + 27);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private async Task CaptureAvatarWithRetries(string accountId, Microsoft.Web.WebView2.WinForms.WebView2 browser)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(attempt == 0 ? 2500 : 3500);
            if (browser.IsDisposed || browser.CoreWebView2 is null) return;
            if (!browser.Visible || activeAccount?.Id != accountId) return;
            try
            {
                var result = await browser.CoreWebView2.ExecuteScriptAsync(ProfileInfoScript);
                using var json = JsonDocument.Parse(result);
                if (json.RootElement.ValueKind != JsonValueKind.Object) continue;

                string? data = null;
                if (json.RootElement.TryGetProperty("data", out var dataNode) &&
                    dataNode.ValueKind == JsonValueKind.String)
                    data = dataNode.GetString();

                if (string.IsNullOrWhiteSpace(data) || !data.StartsWith("data:image/"))
                {
                    var x = json.RootElement.GetProperty("x").GetDouble();
                    var y = json.RootElement.GetProperty("y").GetDouble();
                    var width = json.RootElement.GetProperty("width").GetDouble();
                    var height = json.RootElement.GetProperty("height").GetDouble();
                    if (width < 20 || height < 20) continue;

                    await using var preview = new MemoryStream();
                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
                    preview.Position = 0;
                    using var screenshot = new Bitmap(preview);
                    var scaleX = screenshot.Width / Math.Max(1d, browser.ClientSize.Width);
                    var scaleY = screenshot.Height / Math.Max(1d, browser.ClientSize.Height);
                    var side = Math.Min(width, height);
                    var crop = Rectangle.Intersect(
                        new Rectangle((int)((x + (width - side) / 2) * scaleX),
                            (int)((y + (height - side) / 2) * scaleY),
                            Math.Max(1, (int)(side * scaleX)), Math.Max(1, (int)(side * scaleY))),
                        new Rectangle(0, 0, screenshot.Width, screenshot.Height));
                    if (crop.Width < 10 || crop.Height < 10) continue;
                    using var cropped = screenshot.Clone(crop, screenshot.PixelFormat);
                    using var avatar = new Bitmap(cropped, new Size(256, 256));
                    using var encoded = new MemoryStream();
                    avatar.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                    data = "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
                }

                if (!browser.Visible || activeAccount?.Id != accountId) return;
                var index = accounts.FindIndex(item => item.Id == accountId);
                if (index < 0) return;
                accounts[index] = accounts[index] with { AvatarData = data };
                SaveAccounts();
                if (!IsDisposed) BeginInvoke(() =>
                {
                    RenderAccountButtons();
                    UpdateActiveAccountHeader();
                });
                return;
            }
            catch { }
        }
    }

    private async Task RefreshUnreadCounts()
    {
        if (refreshingIndicators) return;
        refreshingIndicators = true;
        var changed = false;
        try
        {
            // ExecuteScriptAsync devolve o controle ao formulário. Uma conta pode ser aberta ou
            // removida durante essa espera, então percorremos uma fotografia estável do dicionário.
            foreach (var pair in browsers.ToArray())
            {
                if (pair.Value.IsDisposed || pair.Value.CoreWebView2 is null) continue;
                try
                {
                    var result = await pair.Value.CoreWebView2.ExecuteScriptAsync(UnreadCountScript);
                    var count = JsonSerializer.Deserialize<int>(result);
                    var index = accounts.FindIndex(item => item.Id == pair.Key);
                    if (index >= 0 && accounts[index].UnreadCount != count)
                    {
                        var previous = accounts[index].UnreadCount;
                        accounts[index] = accounts[index] with { UnreadCount = count };
                        changed = true;

                        // A primeira leitura estabelece a base e não toca som ao iniciar o programa.
                        if (notificationBaselines.Add(pair.Key)) continue;
                        if (count > previous)
                        {
                            SystemSounds.Asterisk.Play();
                            lastNotifiedAccountId = pair.Key;
                            var accountName = accounts[index].Name;
                            trayIcon.BalloonTipTitle = $"Nova mensagem — {accountName}";
                            trayIcon.BalloonTipText = count == 1
                                ? "Há 1 conversa não lida."
                                : $"Há {count} conversas não lidas.";
                            trayIcon.ShowBalloonTip(3500);
                        }
                    }
                    else if (index >= 0)
                    {
                        notificationBaselines.Add(pair.Key);
                    }
                }
                catch { }
            }
            if (changed && !IsDisposed)
            {
                RenderAccountButtons();
                UpdateActiveAccountHeader();
                UpdateTaskbarBadge();
            }
        }
        finally { refreshingIndicators = false; }
    }

    private void UpdateActiveAccountHeader()
    {
        if (activeAccount is null) { UpdateEmptyState(); return; }
        var current = accounts.FirstOrDefault(item => item.Id == activeAccount.Id) ?? activeAccount;
        activeAccount = current;
        activeMarkerLabel.Text = "EM USO AGORA";
        installButton.Visible = reloadButton.Visible = clearCacheButton.Visible = true;
        activeAccountPicture.Image?.Dispose();
        activeAccountPicture.Image = BuildAccountIcon(current);
        activeNameLabel.Text = current.Name;
        activeDetailsLabel.Text = current.UnreadCount > 0
            ? $"{current.UnreadCount} não lidas  •  sessão ativa"
            : "Conectado  •  sessão ativa";
        statusLabel.Text = current.UnreadCount > 0
            ? $"{current.Name}  •  {current.UnreadCount} não lidas  •  sessão independente"
            : $"{current.Name}  •  conectado  •  sessão independente";
        var total = accounts.Sum(item => item.UnreadCount);
        Text = total > 0
            ? $"({total}) MODUX {AppVersion} — {current.Name}"
            : $"MODUX {AppVersion} — {current.Name}";
    }

    private Font CreateInterfaceFont(float baseSize, FontStyle style = FontStyle.Regular)
    {
        var family = FontFamily.Families.Any(item => item.Name.Equals(preferences.FontFamily, StringComparison.OrdinalIgnoreCase))
            ? preferences.FontFamily
            : "Segoe UI";
        return new Font(family, baseSize * preferences.InterfaceSize / 100f, style);
    }

    private bool IsLightTheme()
    {
        if (preferences.Theme.Equals("Claro", StringComparison.OrdinalIgnoreCase)) return true;
        if (preferences.Theme.Equals("Escuro", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 0)) != 0;
        }
        catch { return false; }
    }

    private void ApplyPreferences()
    {
        var light = IsLightTheme();
        ModuxAppearance.Configure(!light);
        var chrome = ModuxAppearance.Header;
        var sidebarColor = ModuxAppearance.Field;
        var card = ModuxAppearance.HeaderField;
        var primaryText = Color.FromArgb(245, 245, 247);
        var secondaryText = Color.FromArgb(179, 184, 202);

        BackColor = chrome;
        toolbar.BackColor = chrome;
        sidebar.BackColor = sidebarColor;
        sidebarBottom.BackColor = sidebarColor;
        accountsPanel.BackColor = sidebarColor;
        activeAccountCard.BackColor = card;
        activeNameLabel.ForeColor = primaryText;
        activeDetailsLabel.ForeColor = secondaryText;
        statusLabel.ForeColor = ModuxAppearance.Muted;
        statusLabel.BackColor = ModuxAppearance.Canvas;
        activeMarkerLabel.ForeColor = Color.FromArgb(198, 206, 233);
        activeChevronLabel.ForeColor = Color.FromArgb(198, 206, 233);
        browserHost.BackColor = ModuxAppearance.Canvas;

        Font = CreateInterfaceFont(9f);
        activeMarkerLabel.Font = CreateInterfaceFont(7f, FontStyle.Bold);
        activeNameLabel.Font = CreateInterfaceFont(10f, FontStyle.Bold);
        activeDetailsLabel.Font = CreateInterfaceFont(7.5f);
        statusLabel.Font = CreateInterfaceFont(9f);
        activeChevronLabel.Font = CreateInterfaceFont(12f, FontStyle.Bold);

        foreach (var button in new[] { collapseButton, installButton, reloadButton, clearCacheButton, addAccountButton, manageAccountsButton, preferencesButton, aboutButton })
        {
            button.Font = CreateInterfaceFont(9f, FontStyle.Regular);
            button.MinimumSize = new Size(0, 38);
        }
        foreach (var browser in browsers.Values.ToArray())
            browser.ZoomFactor = preferences.WhatsAppZoom / 100d;

        brand.Invalidate();
        UpdateAgendaSidebar();
        RenderAccountButtons();
        UpdateActiveAccountHeader();
    }

    private void ShowPreferences(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Preferências do MODUX",
            Width = 520,
            Height = 470,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = IsLightTheme() ? Color.FromArgb(248, 250, 252) : Color.FromArgb(11, 19, 43),
            ForeColor = IsLightTheme() ? Color.FromArgb(11, 19, 43) : Color.White,
            Font = CreateInterfaceFont(9f)
        };
        var title = new Label
        {
            Text = "Aparência e leitura",
            Left = 24, Top = 20, Width = 430, Height = 30,
            Font = CreateInterfaceFont(14f, FontStyle.Bold), ForeColor = dialog.ForeColor
        };
        var description = new Label
        {
            Text = "Personalize o MODUX sem alterar suas contas ou sessões.",
            Left = 24, Top = 52, Width = 440, Height = 25, ForeColor = IsLightTheme() ? Color.DimGray : Color.Silver
        };
        var themeLabel = new Label { Text = "Tema", Left = 24, Top = 96, Width = 180, Height = 24 };
        var theme = new ComboBox
        {
            Left = 230, Top = 92, Width = 245, DropDownStyle = ComboBoxStyle.DropDownList
        };
        theme.Items.AddRange(["Sistema", "Claro", "Escuro"]);
        theme.SelectedItem = preferences.Theme;

        var fontLabel = new Label { Text = "Fonte da interface", Left = 24, Top = 145, Width = 180, Height = 24 };
        var font = new ComboBox
        {
            Left = 230, Top = 141, Width = 245, DropDownStyle = ComboBoxStyle.DropDownList
        };
        var recommendedFonts = new[] { "Segoe UI", "Inter", "Arial", "Tahoma", "Verdana", "Trebuchet MS" }
            .Where(name => FontFamily.Families.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToArray();
        font.Items.AddRange(recommendedFonts);
        font.SelectedItem = recommendedFonts.Contains(preferences.FontFamily) ? preferences.FontFamily : "Segoe UI";

        var sizeLabel = new Label { Text = "Tamanho da interface", Left = 24, Top = 194, Width = 180, Height = 24 };
        var size = new ComboBox
        {
            Left = 230, Top = 190, Width = 245, DropDownStyle = ComboBoxStyle.DropDownList
        };
        size.Items.AddRange(["Compacto — 90%", "Padrão — 100%", "Confortável — 110%", "Grande — 125%"]);
        size.SelectedIndex = preferences.InterfaceSize switch { 90 => 0, 110 => 2, 125 => 3, _ => 1 };

        var zoomLabel = new Label { Text = "Zoom do WhatsApp", Left = 24, Top = 243, Width = 180, Height = 24 };
        var zoomValue = new Label
        {
            Text = $"{preferences.WhatsAppZoom}%", Left = 417, Top = 243, Width = 58, Height = 24,
            TextAlign = ContentAlignment.MiddleRight
        };
        var zoom = new TrackBar
        {
            Left = 224, Top = 232, Width = 190, Minimum = 80, Maximum = 150,
            TickFrequency = 10, Value = Math.Clamp(preferences.WhatsAppZoom, 80, 150)
        };
        zoom.ValueChanged += (_, _) => zoomValue.Text = $"{zoom.Value}%";

        var note = new Label
        {
            Text = "O tema e a fonte alteram as áreas do MODUX. O zoom controla o conteúdo do WhatsApp em todas as contas.",
            Left = 24, Top = 292, Width = 450, Height = 48,
            ForeColor = IsLightTheme() ? Color.DimGray : Color.Silver
        };
        var restore = new Button { Text = "Restaurar padrões", Left = 24, Top = 365, Width = 145, Height = 38 };
        var cancel = new Button { Text = "Cancelar", Left = 274, Top = 365, Width = 95, Height = 38, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Salvar", Left = 379, Top = 365, Width = 96, Height = 38, DialogResult = DialogResult.OK };
        restore.Click += (_, _) =>
        {
            theme.SelectedItem = "Sistema";
            font.SelectedItem = "Segoe UI";
            size.SelectedIndex = 1;
            zoom.Value = 100;
        };
        dialog.Controls.AddRange([title, description, themeLabel, theme, fontLabel, font, sizeLabel, size,
            zoomLabel, zoom, zoomValue, note, restore, cancel, save]);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var scale = size.SelectedIndex switch { 0 => 90, 2 => 110, 3 => 125, _ => 100 };
        preferences = new UserPreferences(theme.SelectedItem?.ToString() ?? "Sistema",
            font.SelectedItem?.ToString() ?? "Segoe UI", scale, zoom.Value);
        SavePreferences();
        ApplyPreferences();
        Log($"Preferências salvas: tema={preferences.Theme}, fonte={preferences.FontFamily}, interface={preferences.InterfaceSize}, zoom={preferences.WhatsAppZoom}.");
    }

    private static bool IsWhatsAppWebAddress(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return true;
        return uri.Scheme is "about" or "data" or "blob" ||
               uri.Host.Equals("web.whatsapp.com", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenExternalLink(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            Log($"Link aberto externamente: {uri.Host}");
        }
        catch (Exception error)
        {
            Log($"Falha ao abrir link externo: {error.Message}");
            MessageBox.Show("Não foi possível abrir o link no navegador padrão.", "MODUX",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void InitializeTaskbar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        try
        {
            if (taskbar is not null) return;
            taskbar = (ITaskbarList3)new TaskbarListCom();
            taskbar.HrInit();
            UpdateTaskbarBadge();
        }
        catch (Exception error) { Log($"Falha ao iniciar contador da barra de tarefas: {error.Message}"); }
    }

    private void UpdateTaskbarBadge()
    {
        if (taskbar is null || !IsHandleCreated) return;
        try
        {
            var total = accounts.Sum(item => item.UnreadCount);
            if (taskbarOverlayIcon != IntPtr.Zero)
            {
                taskbar.SetOverlayIcon(Handle, IntPtr.Zero, string.Empty);
                DestroyIcon(taskbarOverlayIcon);
                taskbarOverlayIcon = IntPtr.Zero;
            }
            if (total <= 0) return;

            using var badge = new Bitmap(32, 32);
            using var graphics = Graphics.FromImage(badge);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            // Vermelho de alerta: deve contrastar com o ícone azul do MODUX na barra de tarefas.
            using var background = new SolidBrush(Color.FromArgb(220, 38, 38));
            graphics.FillEllipse(background, 1, 1, 30, 30);
            var text = total > 99 ? "99+" : total.ToString();
            using var font = new Font("Segoe UI", total > 99 ? 10f : 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, text, font, new Rectangle(1, 1, 30, 30), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            taskbarOverlayIcon = badge.GetHicon();
            taskbar.SetOverlayIcon(Handle, taskbarOverlayIcon,
                total == 1 ? "1 mensagem não lida" : $"{total} mensagens não lidas no total");
        }
        catch (Exception error) { Log($"Falha ao atualizar contador da barra de tarefas: {error.Message}"); }
    }

    private const string ProfileInfoScript = """
        (async () => {
          const rectOk = el => {
            if (!el) return false;
            const r = el.getBoundingClientRect();
            return r.width >= 24 && r.width <= 100 && r.height >= 24 && r.height <= 100 &&
                   r.top >= 0 && r.bottom <= innerHeight;
          };

          let selfAvatar = null;
          const selfLabels = [...document.querySelectorAll('span, div')]
            .filter(el => {
              const text = (el.textContent || '').trim();
              const r = el.getBoundingClientRect();
              return /\(você\)$/i.test(text) && text.length < 100 &&
                     r.width > 20 && r.width < 500 && r.height > 10 && r.height < 80;
            })
            .sort((a,b) => a.getBoundingClientRect().width - b.getBoundingClientRect().width);

          for (const label of selfLabels) {
            let row = label;
            for (let level = 0; level < 8 && row; level++, row = row.parentElement) {
              const rr = row.getBoundingClientRect();
              const images = [...row.querySelectorAll('img')].filter(rectOk);
              const candidate = images.find(img => {
                const ir = img.getBoundingClientRect();
                return ir.left < label.getBoundingClientRect().left && ir.left < 260;
              });
              if (candidate && rr.height >= 45 && rr.height <= 130) {
                selfAvatar = candidate;
                break;
              }
            }
            if (selfAvatar) break;
          }

          const unwrap = el => el?.tagName === 'IMG' ? el : el?.querySelector?.('img') || null;
          const explicit = [
            '[aria-label="Perfil"]', '[aria-label="Profile"]',
            '[title="Perfil"]', '[title="Profile"]',
            '[data-testid="menu-bar-profile"]'
          ].map(selector => unwrap(document.querySelector(selector))).filter(rectOk);

          const lowerLeft = [...document.querySelectorAll('img')]
            .filter(el => {
              const r = el.getBoundingClientRect();
              return rectOk(el) && r.left >= 0 && r.left < 75 && r.top > innerHeight * .65;
            })
            .sort((a,b) => b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom);

          const el = selfAvatar || explicit[0] || lowerLeft[0];
          if (!el) return null;
          const r = el.getBoundingClientRect();
          let data = null;
          try {
            const source = el.currentSrc || el.src;
            if (source) {
              const response = await fetch(source);
              const blob = await response.blob();
              data = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = reject;
                reader.readAsDataURL(blob);
              });
            }
          } catch {}
          return { x:r.left, y:r.top, width:r.width, height:r.height, data };
        })()
        """;

    private const string UnreadCountScript = """
        (() => {
          const titleMatch = document.title.match(/^\((\d+)\)/);
          if (titleMatch) return Number(titleMatch[1]);
          const nodes = [...document.querySelectorAll('button, [role="button"], span, div')];
          const matches = nodes.map(el => (el.innerText || '').trim())
            .filter(text => /^Não lidas\s+\d+$/.test(text))
            .sort((a,b) => a.length-b.length);
          const match = matches[0]?.match(/(\d+)$/);
          return match ? Number(match[1]) : 0;
        })()
        """;

    private string? FindWaSellerFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var browserRoots = new[]
        {
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(roaming, "Opera Software", "Opera Stable")
        };

        var candidates = new List<DirectoryInfo>();
        foreach (var root in browserRoots.Where(Directory.Exists))
        {
            try
            {
                IEnumerable<string> profiles;
                if (Path.GetFileName(root).Equals("Opera Stable", StringComparison.OrdinalIgnoreCase))
                    profiles = new[] { root };
                else
                    profiles = Directory.EnumerateDirectories(root)
                        .Where(path =>
                        {
                            var name = Path.GetFileName(path);
                            return name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                                   name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                        });

                foreach (var profile in profiles)
                {
                    var extensionRoot = Path.Combine(profile, "Extensions", WaSellerExtensionId);
                    if (!Directory.Exists(extensionRoot)) continue;
                    foreach (var version in Directory.EnumerateDirectories(extensionRoot))
                        if (File.Exists(Path.Combine(version, "manifest.json")))
                            candidates.Add(new DirectoryInfo(version));
                }
            }
            catch { }
        }

        return candidates.OrderByDescending(item => item.LastWriteTimeUtc)
            .Select(item => item.FullName).FirstOrDefault();
    }

    private async void ClearCache(object? sender, EventArgs e)
    {
        clearCacheButton.Enabled = false;
        statusLabel.Text = "Limpando cache…";
        try
        {
            Directory.CreateDirectory(appDataFolder);
            File.WriteAllText(CacheFlagFile, DateTime.Now.ToString("O"));
            foreach (var browser in browsers.Values.ToArray())
            {
                if (browser.CoreWebView2 is not null)
                    await browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            }
            Log("Limpeza de cache solicitada pelo usuário.");
            statusLabel.Text = "Cache limpo — reinicie o MODUX";
            MessageBox.Show(
                "O cache das contas abertas foi limpo.\n\nFeche o MODUX pelo ícone perto do relógio e abra novamente para concluir a limpeza dos arquivos antigos. Seus logins serão preservados.",
                "Limpeza concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            Log($"Falha ao limpar cache: {error.Message}");
            MessageBox.Show($"Não foi possível concluir a limpeza.\n\n{error.Message}", "MODUX",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { clearCacheButton.Enabled = true; }
    }

    private void DeleteCacheFolders()
    {
        var exactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "GrShaderCache", "ShaderCache"
        };
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(appDataFolder, "*", SearchOption.AllDirectories)
                         .Where(path => exactNames.Contains(Path.GetFileName(path)))
                         .OrderByDescending(path => path.Length))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
        catch (Exception error) { Log($"Falha parcial na limpeza de cache: {error.Message}"); }
    }

    private Microsoft.Web.WebView2.WinForms.WebView2? ActiveBrowser()
        => activeAccount is not null && browsers.TryGetValue(activeAccount.Id, out var browser) ? browser : null;

    private async void ManageAccounts(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Gerenciar contas", Width = 650, Height = 410,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Color.FromArgb(11, 19, 43), ForeColor = Color.White
        };
        var title = new Label
        {
            Text = "Contas conectadas ao MODUX", Left = 20, Top = 18, Width = 560, Height = 28,
            Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White
        };
        var list = new ListBox
        {
            Left = 20, Top = 52, Width = 595, Height = 220,
            Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(6, 12, 28),
            ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
        };
        var rename = new Button { Text = "Renomear", Left = 20, Top = 292, Width = 105, Height = 38 };
        var changePhoto = new Button { Text = "Escolher foto", Left = 135, Top = 292, Width = 125, Height = 38 };
        var refreshPhoto = new Button { Text = "Buscar foto", Left = 270, Top = 292, Width = 110, Height = 38 };
        var remove = new Button { Text = "Remover", Left = 390, Top = 292, Width = 100, Height = 38 };
        var close = new Button { Text = "Fechar", Left = 510, Top = 292, Width = 105, Height = 38, DialogResult = DialogResult.OK };
        void RefreshList()
        {
            var selectedId = list.SelectedItem is ModuxAccountInfo selected ? selected.Id : null;
            list.DataSource = null;
            list.DataSource = accounts.ToList();
            list.DisplayMember = nameof(ModuxAccountInfo.Name);
            if (selectedId is not null)
                list.SelectedItem = accounts.FirstOrDefault(item => item.Id == selectedId);
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not ModuxAccountInfo selected) return;
            var newName = PromptForText("Renomear conta", "Novo nome da conta:", selected.Name);
            if (string.IsNullOrWhiteSpace(newName)) return;
            var index = accounts.FindIndex(item => item.Id == selected.Id);
            accounts[index] = accounts[index] with { Name = newName.Trim() };
            if (activeAccount?.Id == selected.Id) activeAccount = accounts[index];
            SaveAccounts(); RefreshList(); RenderAccountButtons();
        };
        changePhoto.Click += (_, _) =>
        {
            if (list.SelectedItem is not ModuxAccountInfo selected) return;
            using var picker = new OpenFileDialog
            {
                Title = "Escolher foto da conta",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.jfif;*.webp;*.bmp"
            };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            try
            {
                using var original = Image.FromFile(picker.FileName);
                var side = Math.Min(original.Width, original.Height);
                using var square = new Bitmap(512, 512);
                using var graphics = Graphics.FromImage(square);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(original, new Rectangle(0, 0, 512, 512),
                    new Rectangle((original.Width - side) / 2, (original.Height - side) / 2, side, side),
                    GraphicsUnit.Pixel);
                using var encoded = new MemoryStream();
                square.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                var data = "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
                var index = accounts.FindIndex(item => item.Id == selected.Id);
                accounts[index] = accounts[index] with { AvatarData = data };
                if (activeAccount?.Id == selected.Id) activeAccount = accounts[index];
                SaveAccounts(); RefreshList(); RenderAccountButtons();
            }
            catch (Exception error)
            {
                MessageBox.Show($"Não foi possível usar esta foto.\n\n{error.Message}", "MODUX",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        refreshPhoto.Click += async (_, _) =>
        {
            if (list.SelectedItem is not ModuxAccountInfo selected) return;
            if (activeAccount?.Id != selected.Id)
            {
                MessageBox.Show("Abra esta conta no menu lateral e tente novamente.", "MODUX");
                return;
            }
            if (browsers.TryGetValue(selected.Id, out var browser))
                await CaptureAvatarWithRetries(selected.Id, browser);
            RefreshList(); RenderAccountButtons();
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is not ModuxAccountInfo selected) return;
            var confirmation = MessageBox.Show(
                $"Remover “{selected.Name}” do MODUX?\n\nA conta sairá do menu. Os dados locais da sessão serão preservados.",
                "Remover conta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes) return;
            if (browsers.Remove(selected.Id, out var removedBrowser))
            {
                browserHost.Controls.Remove(removedBrowser);
                removedBrowser.Dispose();
            }
            accounts.RemoveAll(item => item.Id == selected.Id);
            if (activeAccount?.Id == selected.Id) activeAccount = null;
            SaveAccounts(); RefreshList(); RenderAccountButtons();
            if (activeAccount is null && accounts.Count > 0) await ActivateAccount(accounts[0]);
            else if (accounts.Count == 0) UpdateEmptyState();
        };
        dialog.Controls.AddRange([title, list, rename, changePhoto, refreshPhoto, remove, close]);
        dialog.AcceptButton = close;
        RefreshList();
        dialog.ShowDialog(this);
    }

    private string? PromptForText(string title, string labelText, string initialValue)
    {
        using var dialog = new Form
        {
            Text = title, Width = 420, Height = 180,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var label = new Label { Text = labelText, Left = 20, Top = 20, Width = 350 };
        var input = new TextBox { Left = 20, Top = 50, Width = 360, Text = initialValue };
        var confirm = new Button { Text = "Salvar", DialogResult = DialogResult.OK, Left = 275, Top = 88, Width = 105 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 170, Top = 88, Width = 95 };
        dialog.Controls.AddRange([label, input, cancel, confirm]);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    private async void AddAccount(object? sender, EventArgs e)
    {
        var name = PromptForAccountName();
        if (string.IsNullOrWhiteSpace(name)) return;

        var account = new ModuxAccountInfo($"account-{Guid.NewGuid():N}", name.Trim());
        accounts.Add(account);
        SaveAccounts();
        RenderAccountButtons();
        await ActivateAccount(account);
    }

    private string? PromptForAccountName()
    {
        using var dialog = new Form
        {
            Text = "Adicionar conta",
            Width = 420,
            Height = 180,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        var label = new Label { Text = "Nome para identificar a conta:", Left = 20, Top = 20, Width = 350 };
        var input = new TextBox { Left = 20, Top = 50, Width = 360, PlaceholderText = "Ex.: Atendimento ou Pessoal" };
        var confirm = new Button { Text = "Adicionar", DialogResult = DialogResult.OK, Left = 275, Top = 88, Width = 105 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 170, Top = 88, Width = 95 };
        dialog.Controls.AddRange([label, input, cancel, confirm]);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    private async void InstallExtension(object? sender, EventArgs e)
    {
        var browser = ActiveBrowser();
        if (browser?.CoreWebView2 is null || activeAccount is null)
        {
            MessageBox.Show("Aguarde a conta terminar de iniciar.", "MODUX");
            return;
        }

        var extensionPath = detectedWaSellerPath;
        if (string.IsNullOrWhiteSpace(extensionPath) || !File.Exists(Path.Combine(extensionPath, "manifest.json")))
        {
            using var picker = new FolderBrowserDialog
            {
                Description = "O WaSeller não foi localizado automaticamente. Selecione a pasta que contém manifest.json.",
                UseDescriptionForTitle = true, ShowNewFolderButton = false
            };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            extensionPath = picker.SelectedPath;
        }
        if (!File.Exists(Path.Combine(extensionPath, "manifest.json")))
        {
            MessageBox.Show("A pasta escolhida não contém manifest.json.", "Pasta inválida",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var installed = await browser.CoreWebView2.Profile.GetBrowserExtensionsAsync();
            if (installed.Any(extension => extension.Name.Contains("WaSeller", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"O WaSeller já está instalado somente na conta “{activeAccount.Name}”.",
                    "WaSeller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                $"Instalar o WaSeller somente na conta “{activeAccount.Name}”?",
                "Confirmar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes) return;

            installButton.Enabled = false;
            statusLabel.Text = $"Instalando WaSeller em {activeAccount.Name}…";
            var extension = await browser.CoreWebView2.Profile.AddBrowserExtensionAsync(extensionPath);
            statusLabel.Text = $"{activeAccount.Name} — {extension.Name} instalado";
            browser.CoreWebView2.Reload();
            Log($"WaSeller instalado manualmente em {activeAccount.Name}.");
            MessageBox.Show($"WaSeller instalado somente na conta “{activeAccount.Name}”.",
                "Instalação concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            statusLabel.Text = "Falha ao instalar a extensão";
            Log($"Falha ao instalar WaSeller: {error.Message}");
            MessageBox.Show($"Não foi possível instalar o WaSeller.\n\n{error.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { installButton.Enabled = true; }
    }

}

