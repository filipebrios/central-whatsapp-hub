using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentralWhatsApp.WebView2;

public sealed class MainForm : Form
{
    private sealed record AccountInfo(string Id, string Name, string? AvatarData = null);

    private readonly string appDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CentralWhatsApp",
        "WebView2Prototype");

    private const int ExpandedSidebarWidth = 245;
    private const int CollapsedSidebarWidth = 72;
    private readonly Panel sidebar = new();
    private readonly Label brand = new();
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
        Text = "Central WhatsApp — WebView2 + WaSeller";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 24, 39);

        sidebar.Dock = DockStyle.Left;
        sidebar.Width = ExpandedSidebarWidth;
        sidebar.BackColor = Color.FromArgb(7, 13, 18);
        sidebar.Padding = new Padding(0, 10, 0, 0);
        brand.Dock = DockStyle.Top;
        brand.Height = 55;
        brand.Text = "  Central WhatsApp";
        brand.ForeColor = Color.White;
        brand.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        brand.TextAlign = ContentAlignment.MiddleLeft;
        var sidebarBottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(10) };
        addAccountButton.Dock = DockStyle.Fill;
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
            BackColor = Color.FromArgb(17, 24, 39),
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
        addAccountButton.Click += AddAccount;
        installButton.Click += InstallExtension;
        reloadButton.Click += (_, _) => ActiveBrowser()?.CoreWebView2?.Reload();
        Shown += async (_, _) => await Start();
    }

    private static Button CreateToolbarButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = Color.FromArgb(31, 41, 55),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(8),
        Padding = new Padding(12, 4, 12, 4)
    };

    private string AccountsFile => Path.Combine(appDataFolder, "accounts.json");

    private async Task Start()
    {
        Directory.CreateDirectory(appDataFolder);
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
        brand.Text = sidebarCollapsed ? " CW" : "  Central WhatsApp";
        addAccountButton.Text = sidebarCollapsed ? "+" : "+ Adicionar conta";
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
                Image = GetAccountAvatar(account)
            };
            button.FlatAppearance.BorderColor = activeAccount?.Id == account.Id ? Color.FromArgb(34, 197, 94) : Color.FromArgb(31, 41, 55);
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
                MessageBox.Show($"Não foi possível abrir esta conta.\n\n{error.Message}", "Central WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        browser.Visible = true;
        browser.BringToFront();
        statusLabel.Text = $"{account.Name} — sessão independente";
    }

    private Image GetAccountAvatar(AccountInfo account)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(account.AvatarData))
            {
                var comma = account.AvatarData.IndexOf(',');
                var bytes = Convert.FromBase64String(comma >= 0 ? account.AvatarData[(comma + 1)..] : account.AvatarData);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(Image.FromStream(stream), new Size(38, 38));
            }
        }
        catch { }
        var bitmap = new Bitmap(38, 38);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(5, 150, 105));
        graphics.FillEllipse(brush, 0, 0, 37, 37);
        var initials = string.Concat(account.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        TextRenderer.DrawText(graphics, initials, font, new Rectangle(0, 0, 38, 38), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bitmap;
    }

    private async Task CaptureAvatarWithRetries(string accountId, Microsoft.Web.WebView2.WinForms.WebView2 browser)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(attempt == 0 ? 2500 : 4000);
            if (browser.IsDisposed || browser.CoreWebView2 is null) return;
            try
            {
                var result = await browser.CoreWebView2.ExecuteScriptAsync(AvatarCaptureScript);
                var data = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrWhiteSpace(data) || !data.StartsWith("data:image/")) continue;
                var index = accounts.FindIndex(item => item.Id == accountId);
                if (index < 0 || accounts[index].AvatarData == data) return;
                accounts[index] = accounts[index] with { AvatarData = data };
                SaveAccounts();
                if (!IsDisposed) BeginInvoke(RenderAccountButtons);
                return;
            }
            catch { }
        }
    }

    private const string AvatarCaptureScript = """
        (async () => {
          const candidates = [
            document.querySelector('[aria-label="Perfil"] img'),
            document.querySelector('[title="Perfil"] img'),
            document.querySelector('[data-testid="menu-bar-profile"] img'),
            ...document.querySelectorAll('header img')
          ].filter(Boolean);
          let img = candidates.find(x => x.naturalWidth >= 24) ||
            [...document.images].filter(x => x.naturalWidth >= 24 && x.naturalWidth <= 512)
              .sort((a,b) => (a.getBoundingClientRect().left - b.getBoundingClientRect().left) ||
                             (b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom))[0];
          if (!img || !img.src) return null;
          if (!img.complete) await new Promise(r => { img.addEventListener('load', r, {once:true}); setTimeout(r, 1500); });
          const canvas = document.createElement('canvas');
          canvas.width = canvas.height = 128;
          canvas.getContext('2d').drawImage(img, 0, 0, 128, 128);
          return canvas.toDataURL('image/png');
        })()
        """;

    private Microsoft.Web.WebView2.WinForms.WebView2? ActiveBrowser()
        => activeAccount is not null && browsers.TryGetValue(activeAccount.Id, out var browser) ? browser : null;

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
            MessageBox.Show("Aguarde a conta terminar de iniciar.", "Central WhatsApp");
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
