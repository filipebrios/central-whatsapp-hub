using System.Drawing;

namespace CentralWhatsApp.WebView2;

public sealed partial class MainForm
{
    private readonly Panel agendaShortcutPanel = new()
    {
        Dock = DockStyle.Bottom,
        Height = 78,
        Padding = new Padding(10, 8, 10, 8),
        BackColor = Color.FromArgb(6, 12, 28)
    };
    private readonly Button agendaShortcutButton = new()
    {
        Dock = DockStyle.Fill,
        Text = "▦  Agenda",
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.FromArgb(0, 103, 44),
        AccessibleName = "Abrir Agenda",
        Cursor = Cursors.Hand
    };
    private AgendaPanel? agendaPanel;
    private bool showingAgenda;

    private void ConfigureAgendaFeature()
    {
        ModuxAppearance.Role(agendaShortcutButton, "sidebar");
        agendaShortcutPanel.Controls.Add(agendaShortcutButton);
        agendaShortcutButton.Click += (_, _) => ShowAgendaFeature();
        collapseButton.Click += (_, _) => UpdateAgendaSidebar();
        preferencesButton.Click += (_, _) => UpdateAgendaSidebar();
        UpdateAgendaSidebar();
    }

    private void UpdateAgendaSidebar()
    {
        agendaShortcutPanel.Height = sidebarCollapsed ? 70 : 78;
        agendaShortcutPanel.Padding = sidebarCollapsed
            ? new Padding(7, 10, 7, 10)
            : new Padding(10, 8, 10, 8);
        agendaShortcutButton.Text = sidebarCollapsed ? "▦" : "▦  Agenda";
        agendaShortcutButton.Font = CreateInterfaceFont(sidebarCollapsed ? 16f : 10f, FontStyle.Bold);
        agendaShortcutButton.BackColor = showingAgenda ? ModuxAppearance.Selection : ModuxAppearance.Surface;
        agendaShortcutButton.ForeColor = ModuxAppearance.Text;
        agendaShortcutButton.FlatAppearance.BorderColor =
            showingAgenda ? ModuxAppearance.Accent : ModuxAppearance.Border;
        agendaShortcutPanel.BackColor = ModuxAppearance.Field;
    }

    private void ShowAgendaFeature()
    {
        // Sobrepõe o painel existente, sem navegar nem descartar os WebView2.
        if (agendaPanel is null)
        {
            agendaPanel = new AgendaPanel();
            agendaPanel.ComposeRequested += QueueCompose;
            agendaPanel.OpenConfirmationDraft = QueueAgendaCompose;
        }
        if (!browserHost.Controls.Contains(agendaPanel))
            browserHost.Controls.Add(agendaPanel);
        showingAgenda = true;
        welcomePanel.Visible = false;
        agendaPanel.Visible = true;
        agendaPanel.BringToFront();
        installButton.Enabled = false;
        reloadButton.Enabled = false;
        clearCacheButton.Enabled = false;
        UpdateAgendaSidebar();
        statusLabel.Text = "Agenda • acesso protegido • dados da sua equipe";
    }

    private void ShowWhatsAppFeature()
    {
        if (!showingAgenda) return;
        showingAgenda = false;
        if (agendaPanel is not null) agendaPanel.Visible = false;
        UpdateEmptyState();
        UpdateAgendaSidebar();
    }
}
