using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentralWhatsApp.WebView2;

public sealed class MainForm : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly Button installButton = new()
    {
        Text = "Instalar WaSeller",
        AutoSize = true,
        Margin = new Padding(8),
        Padding = new Padding(12, 4, 12, 4)
    };
    private readonly Button reloadButton = new()
    {
        Text = "Recarregar",
        AutoSize = true,
        Margin = new Padding(8),
        Padding = new Padding(12, 4, 12, 4)
    };
    private readonly Label statusLabel = new()
    {
        Text = "Iniciando Microsoft Edge WebView2…",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
        Margin = new Padding(12, 15, 8, 8)
    };

    public MainForm()
    {
        Text = "Central WhatsApp — Teste WebView2 + WaSeller";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 24, 39);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(8, 4, 8, 4)
        };

        toolbar.Controls.Add(installButton);
        toolbar.Controls.Add(reloadButton);
        toolbar.Controls.Add(statusLabel);
        Controls.Add(browser);
        Controls.Add(toolbar);

        installButton.Click += InstallExtension;
        reloadButton.Click += (_, _) => browser.CoreWebView2?.Reload();
        Shown += async (_, _) => await InitializeBrowser();
    }

    private async Task InitializeBrowser()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CentralWhatsApp",
                "WebView2Prototype",
                "waseller-account");

            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true,
                Language = "pt-BR"
            };
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: options);

            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                browser.CoreWebView2.Navigate(args.Uri);
            };

            statusLabel.Text = "WebView2 pronto — abra o WhatsApp e depois instale o WaSeller";
            browser.CoreWebView2.Navigate("https://web.whatsapp.com/");
        }
        catch (Exception error)
        {
            statusLabel.Text = "Falha ao iniciar o WebView2";
            MessageBox.Show(
                $"Não foi possível iniciar o Microsoft Edge WebView2.\n\n{error.Message}",
                "Central WhatsApp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void InstallExtension(object? sender, EventArgs e)
    {
        if (browser.CoreWebView2 is null)
        {
            MessageBox.Show("Aguarde o navegador terminar de iniciar.", "Central WhatsApp");
            return;
        }

        using var picker = new FolderBrowserDialog
        {
            Description = "Selecione a pasta 7.4.3.83_0 do WaSeller, que contém o manifest.json.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        if (!File.Exists(Path.Combine(picker.SelectedPath, "manifest.json")))
        {
            MessageBox.Show(
                "A pasta escolhida não contém o arquivo manifest.json.",
                "Pasta inválida",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            installButton.Enabled = false;
            statusLabel.Text = "Instalando WaSeller…";
            var extension = await browser.CoreWebView2.Profile.AddBrowserExtensionAsync(picker.SelectedPath);
            statusLabel.Text = $"Extensão instalada: {extension.Name}";
            browser.CoreWebView2.Reload();
            MessageBox.Show(
                $"A extensão “{extension.Name}” foi instalada neste perfil.\n\nTeste o login e os recursos do WaSeller no WhatsApp.",
                "WaSeller instalado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            statusLabel.Text = "Falha ao instalar a extensão";
            MessageBox.Show(
                $"O WebView2 não conseguiu instalar o WaSeller.\n\n{error.Message}",
                "Erro na instalação",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            installButton.Enabled = true;
        }
    }
}
