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
        Text = "▦  Agenda São Bento",
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.FromArgb(0, 103, 44),
        AccessibleName = "Abrir Agenda São Bento",
        Cursor = Cursors.Hand
    };
    private AgendaPanel? agendaPanel;
    private bool showingAgenda;

    private void ConfigureAgendaFeature()
    {
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
        agendaShortcutButton.Text = sidebarCollapsed ? "▦" : "▦  Agenda São Bento";
        agendaShortcutButton.Font = CreateInterfaceFont(sidebarCollapsed ? 16f : 10f, FontStyle.Bold);
        agendaShortcutButton.BackColor = showingAgenda
            ? Color.FromArgb(0, 130, 51)
            : Color.FromArgb(0, 103, 44);
        agendaShortcutButton.FlatAppearance.BorderColor =
            showingAgenda ? Color.FromArgb(168, 207, 69) : Color.FromArgb(0, 103, 44);
        agendaShortcutPanel.BackColor = IsLightTheme()
            ? Color.FromArgb(226, 232, 240)
            : Color.FromArgb(6, 12, 28);
    }

    private void ShowAgendaFeature()
    {
        // Sobrepõe o painel existente, sem navegar nem descartar os WebView2.
        if (agendaPanel is null)
        {
            agendaPanel = new AgendaPanel();
            agendaPanel.ComposeRequested += QueueCompose;
        }
        if (!browserHost.Controls.Contains(agendaPanel))
            browserHost.Controls.Add(agendaPanel);
        showingAgenda = true;
        agendaPanel.Visible = true;
        agendaPanel.BringToFront();
        installButton.Enabled = false;
        reloadButton.Enabled = false;
        clearCacheButton.Enabled = false;
        UpdateAgendaSidebar();
        statusLabel.Text = "Agenda São Bento • acesso protegido • dados compartilhados";
    }

    private void ShowWhatsAppFeature()
    {
        if (!showingAgenda) return;
        showingAgenda = false;
        if (agendaPanel is not null) agendaPanel.Visible = false;
        installButton.Enabled = true;
        reloadButton.Enabled = true;
        clearCacheButton.Enabled = true;
        UpdateAgendaSidebar();
    }
}
