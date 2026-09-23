using System.Text.Json;
using System.Drawing.Drawing2D;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentralWhatsApp.WebView2;

public sealed class MainForm : Form
{
    private sealed record AccountInfo(string Id, string Name, string? AvatarData = null, int UnreadCount = 0);

    private readonly string appDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CentralWhatsApp",
        "WebView2Prototype");

    private const string WaSellerExtensionId = "illemhbijpiebjfilfmgebahaakajkpe";
    private const int ExpandedSidebarWidth = 245;
    private const int CollapsedSidebarWidth = 72;
    private readonly Panel sidebar = new();
    private readonly Label brand = new();
    private readonly System.Windows.Forms.Timer unreadTimer = new() { Interval = 5000 };
    private bool refreshingIndicators;
    private bool exitRequested;
    private bool trayNoticeShown;
    private string? detectedWaSellerPath;
    private readonly NotifyIcon trayIcon = new();
    private readonly Image? brandLogo;
    private bool sidebarCollapsed;

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
    private readonly Button collapseButton = CreateToolbarButton("◀");
    private readonly Button installButton = CreateToolbarButton("Instalar WaSeller nesta conta");
    private readonly Button reloadButton = CreateToolbarButton("Recarregar");
    private readonly Label statusLabel = new()
    {
        Text = "Iniciando…",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
        Margin = new Padding(12, 15, 8, 8)
    };

    private readonly List<AccountInfo> accounts = [];
    private readonly Dictionary<string, Microsoft.Web.WebView2.WinForms.WebView2> browsers = [];
    private AccountInfo? activeAccount;

    public MainForm()
    {
        Text = "MODUX — WebView2 + WaSeller";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 19, 43);
        brandLogo = LoadBrandLogo();
        ConfigureTrayIcon();

        sidebar.Dock = DockStyle.Left;
        sidebar.Width = ExpandedSidebarWidth;
        sidebar.BackColor = Color.FromArgb(6, 12, 28);
        sidebar.Padding = new Padding(0, 10, 0, 0);
        brand.Dock = DockStyle.Top;
        brand.Height = 55;
        brand.Text = "";
        brand.ForeColor = Color.White;
        brand.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        brand.TextAlign = ContentAlignment.MiddleLeft;
        brand.Paint += DrawBrand;
        var sidebarBottom = new Panel { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(10, 4, 10, 8) };
        manageAccountsButton.Dock = DockStyle.Top;
        manageAccountsButton.Height = 42;
        addAccountButton.Dock = DockStyle.Bottom;
        addAccountButton.Height = 42;
        sidebarBottom.Controls.Add(manageAccountsButton);
        sidebarBottom.Controls.Add(addAccountButton);
        sidebar.Controls.Add(accountsPanel);
        sidebar.Controls.Add(sidebarBottom);
        sidebar.Controls.Add(brand);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(11, 19, 43),
            Padding = new Padding(8, 4, 8, 4)
        };
        collapseButton.Width = 44;
        collapseButton.Padding = new Padding(4);
        collapseButton.AccessibleName = "Recolher ou abrir menu de contas";
        toolbar.Controls.Add(collapseButton);
        toolbar.Controls.Add(installButton);
        toolbar.Controls.Add(reloadButton);
        toolbar.Controls.Add(statusLabel);

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(browserHost);
        rightPanel.Controls.Add(toolbar);

        Controls.Add(rightPanel);
        Controls.Add(sidebar);

        collapseButton.Click += (_, _) => ToggleSidebar();
        manageAccountsButton.Click += ManageAccounts;
        addAccountButton.Click += AddAccount;
        installButton.Click += InstallExtension;
        reloadButton.Click += (_, _) => ActiveBrowser()?.CoreWebView2?.Reload();
        unreadTimer.Tick += async (_, _) => await RefreshUnreadCounts();
        Shown += async (_, _) =>
        {
            await Start();
            unreadTimer.Start();
        };
        FormClosing += HandleFormClosing;
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
        trayIcon.Text = "MODUX";
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            unreadTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            brandLogo?.Dispose();
        }
        base.Dispose(disposing);
    }

    private string AccountsFile => Path.Combine(appDataFolder, "accounts.json");

    private async Task Start()
    {
        Directory.CreateDirectory(appDataFolder);
        detectedWaSellerPath = FindWaSellerFolder();
        LoadAccounts();
        RenderAccountButtons();
        if (accounts.Count > 0) await ActivateAccount(accounts[0]);
    }

    private void LoadAccounts()
    {
        try
        {
            if (File.Exists(AccountsFile))
            {
                var saved = JsonSerializer.Deserialize<List<AccountInfo>>(File.ReadAllText(AccountsFile));
                if (saved is { Count: > 0 }) accounts.AddRange(saved);
            }
        }
        catch
        {
            // Se o arquivo local estiver corrompido, preservamos a sessão original como ponto de recuperação.
        }

        if (accounts.Count == 0)
        {
            accounts.Add(new AccountInfo("waseller-account", "São Bento + WaSeller"));
            SaveAccounts();
        }
    }

    private void SaveAccounts()
    {
        Directory.CreateDirectory(appDataFolder);
        File.WriteAllText(AccountsFile, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ToggleSidebar()
    {
        sidebarCollapsed = !sidebarCollapsed;
        sidebar.Width = sidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
        brand.Invalidate();
        addAccountButton.Text = sidebarCollapsed ? "+" : "+ Adicionar conta";
        manageAccountsButton.Text = sidebarCollapsed ? "⚙" : "⚙ Gerenciar contas";
        collapseButton.Text = sidebarCollapsed ? "☰" : "◀";
        RenderAccountButtons();
    }

    private void RenderAccountButtons()
    {
        accountsPanel.Controls.Clear();
        foreach (var account in accounts)
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
                ForeColor = Color.White,
                BackColor = activeAccount?.Id == account.Id ? Color.FromArgb(25, 45, 55) : Color.FromArgb(13, 22, 29),
                Margin = new Padding(0, 0, 0, 8),
                Tag = account,
                Image = BuildAccountIcon(account)
            };
            button.FlatAppearance.BorderColor = activeAccount?.Id == account.Id ? Color.FromArgb(6, 182, 212) : Color.FromArgb(29, 78, 216);
            button.Click += async (_, _) => await ActivateAccount(account);
            accountsPanel.Controls.Add(button);
        }
    }

    private async Task ActivateAccount(AccountInfo account)
    {
        activeAccount = account;
        RenderAccountButtons();

        foreach (var existing in browsers.Values) existing.Visible = false;

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
                await TryAutoInstallWaSeller(browser, account);
                browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                browser.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    browser.CoreWebView2.Navigate(args.Uri);
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
        statusLabel.Text = $"{account.Name} — sessão independente";
        _ = CaptureAvatarWithRetries(account.Id, browser);
    }

    private Image BuildAccountIcon(AccountInfo account)
    {
        using var avatar = LoadAvatar(account);
        var icon = new Bitmap(48, 48);
        using var graphics = Graphics.FromImage(icon);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(3, 7, 38, 38);
            graphics.SetClip(path);
            graphics.DrawImage(avatar, new Rectangle(3, 7, 38, 38));
            graphics.ResetClip();
        }
        using (var border = new Pen(Color.FromArgb(45, 212, 191), 1.5f))
            graphics.DrawEllipse(border, 3, 7, 38, 38);

        if (account.UnreadCount > 0)
        {
            var badgeText = account.UnreadCount > 99 ? "99+" : account.UnreadCount.ToString();
            var badgeWidth = account.UnreadCount > 99 ? 24 : 19;
            var badgeRect = new Rectangle(48 - badgeWidth, 0, badgeWidth, 19);
            using var badgeBrush = new SolidBrush(Color.FromArgb(6, 182, 212));
            graphics.FillEllipse(badgeBrush, badgeRect);
            using var font = new Font("Segoe UI", account.UnreadCount > 99 ? 7 : 8, FontStyle.Bold);
            TextRenderer.DrawText(graphics, badgeText, font, badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return icon;
    }

    private Image LoadAvatar(AccountInfo account)
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
            using var titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
            using var subtitleFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            e.Graphics.DrawString("MODUX", titleFont, Brushes.White, logoX + 53, logoY + 4);
            using var cyan = new SolidBrush(Color.FromArgb(6, 182, 212));
            e.Graphics.DrawString("GESTÃO MULTICONTAS", subtitleFont, cyan, logoX + 54, logoY + 27);
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
                if (!IsDisposed) BeginInvoke(RenderAccountButtons);
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
            foreach (var pair in browsers)
            {
                if (pair.Value.IsDisposed || pair.Value.CoreWebView2 is null) continue;
                try
                {
                    var result = await pair.Value.CoreWebView2.ExecuteScriptAsync(UnreadCountScript);
                    var count = JsonSerializer.Deserialize<int>(result);
                    var index = accounts.FindIndex(item => item.Id == pair.Key);
                    if (index >= 0 && accounts[index].UnreadCount != count)
                    {
                        accounts[index] = accounts[index] with { UnreadCount = count };
                        changed = true;
                    }
                }
                catch { }
            }
            if (changed && !IsDisposed) RenderAccountButtons();
        }
        finally { refreshingIndicators = false; }
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

    private async Task TryAutoInstallWaSeller(Microsoft.Web.WebView2.WinForms.WebView2 browser, AccountInfo account)
    {
        if (browser.CoreWebView2 is null || string.IsNullOrWhiteSpace(detectedWaSellerPath)) return;
        try
        {
            var installed = await browser.CoreWebView2.Profile.GetBrowserExtensionsAsync();
            if (installed.Any(extension => extension.Name.Contains("WaSeller", StringComparison.OrdinalIgnoreCase)))
            {
                statusLabel.Text = $"{account.Name} — WaSeller pronto";
                return;
            }

            var extension = await browser.CoreWebView2.Profile.AddBrowserExtensionAsync(detectedWaSellerPath);
            statusLabel.Text = $"{account.Name} — {extension.Name} instalado automaticamente";
        }
        catch
        {
            // A instalação manual continua disponível se o navegador bloquear esta cópia.
        }
    }

    private Microsoft.Web.WebView2.WinForms.WebView2? ActiveBrowser()
        => activeAccount is not null && browsers.TryGetValue(activeAccount.Id, out var browser) ? browser : null;

    private async void ManageAccounts(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Gerenciar contas", Width = 520, Height = 390,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Color.FromArgb(11, 19, 43),
            ForeColor = Color.White
        };
        var title = new Label
        {
            Text = "Contas conectadas ao Central", Left = 20, Top = 18, Width = 450, Height = 28,
            Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White
        };
        var list = new ListBox
        {
            Left = 20, Top = 52, Width = 465, Height = 210,
            Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(6, 12, 28),
            ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
        };
        var rename = new Button { Text = "Renomear", Left = 20, Top = 280, Width = 115, Height = 38 };
        var remove = new Button { Text = "Remover do Central", Left = 145, Top = 280, Width = 165, Height = 38 };
        var close = new Button { Text = "Fechar", Left = 370, Top = 280, Width = 115, Height = 38, DialogResult = DialogResult.OK };
        void RefreshList()
        {
            var selectedId = list.SelectedItem is AccountInfo selected ? selected.Id : null;
            list.DataSource = null;
            list.DataSource = accounts.ToList();
            list.DisplayMember = nameof(AccountInfo.Name);
            if (selectedId is not null)
                list.SelectedItem = accounts.FirstOrDefault(item => item.Id == selectedId);
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not AccountInfo selected) return;
            var newName = PromptForText("Renomear conta", "Novo nome da conta:", selected.Name);
            if (string.IsNullOrWhiteSpace(newName)) return;
            var index = accounts.FindIndex(item => item.Id == selected.Id);
            accounts[index] = accounts[index] with { Name = newName.Trim() };
            if (activeAccount?.Id == selected.Id) activeAccount = accounts[index];
            SaveAccounts();
            RefreshList();
            RenderAccountButtons();
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is not AccountInfo selected) return;
            var confirmation = MessageBox.Show(
                $"Remover “{selected.Name}” do MODUX?\n\nA conta sairá do menu. Os dados locais da sessão serão preservados como segurança.",
                "Remover conta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes) return;
            if (browsers.Remove(selected.Id, out var removedBrowser))
            {
                browserHost.Controls.Remove(removedBrowser);
                removedBrowser.Dispose();
            }
            accounts.RemoveAll(item => item.Id == selected.Id);
            if (activeAccount?.Id == selected.Id) activeAccount = null;
            SaveAccounts();
            RefreshList();
            RenderAccountButtons();
            if (activeAccount is null && accounts.Count > 0) await ActivateAccount(accounts[0]);
            else if (accounts.Count == 0) statusLabel.Text = "Nenhuma conta adicionada";
        };
        dialog.Controls.AddRange([title, list, rename, remove, close]);
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

        var account = new AccountInfo($"account-{Guid.NewGuid():N}", name.Trim());
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
        var input = new TextBox { Left = 20, Top = 50, Width = 360, PlaceholderText = "Ex.: São Bento Mobile" };
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

        using var picker = new FolderBrowserDialog
        {
            Description = "Selecione a pasta da versão do WaSeller que contém o manifest.json.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (!File.Exists(Path.Combine(picker.SelectedPath, "manifest.json")))
        {
            MessageBox.Show("A pasta escolhida não contém manifest.json.", "Pasta inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            installButton.Enabled = false;
            statusLabel.Text = $"Instalando WaSeller em {activeAccount.Name}…";
            var extension = await browser.CoreWebView2.Profile.AddBrowserExtensionAsync(picker.SelectedPath);
            statusLabel.Text = $"{activeAccount.Name} — {extension.Name} instalado";
            browser.CoreWebView2.Reload();
            MessageBox.Show($"WaSeller instalado somente na conta “{activeAccount.Name}”.", "Instalação concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            statusLabel.Text = "Falha ao instalar a extensão";
            MessageBox.Show($"Não foi possível instalar o WaSeller.\n\n{error.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            installButton.Enabled = true;
        }
    }
}
