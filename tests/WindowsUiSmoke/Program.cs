using System.Reflection;
using CentralWhatsApp.WebView2;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        var assembly = typeof(MainForm).Assembly;
        var appearance = assembly.GetType("CentralWhatsApp.WebView2.ModuxAppearance", true)!;
        var configure = appearance.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!;
        var panelType = assembly.GetType("CentralWhatsApp.WebView2.AgendaPanel", true)!;
        using var form = new Form { Width = 1280, Height = 900, StartPosition = FormStartPosition.CenterScreen, Text = "MODUX • UI smoke • dados fictícios" };
        using var panel = (Control)Activator.CreateInstance(panelType, true)!;
        panel.Dock = DockStyle.Fill; form.Controls.Add(panel);
        var login = (Control)panelType.GetField("loginPane", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        var agenda = (Control)panelType.GetField("appointmentsPane", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        var grid = (DataGridView)panelType.GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        form.Show();
        Directory.CreateDirectory("ui-smoke-output");
        foreach (var dark in new[] { false, true })
        {
            configure.Invoke(null, [dark]);
            Pump();
            if (panel.BackColor != (Color)appearance.GetProperty("Surface", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
                throw new Exception("Tema não alcançou o painel integrado.");
            login.Visible = false; agenda.Visible = true; agenda.BringToFront();
            grid.Rows.Clear();
            var row = grid.Rows.Add("26/10/2026", "10:00", "Cliente fictício", "Desinsetização", "agendado", "Contato de teste", "Endereço de teste");
            var serviceColor = Color.FromArgb(3, 155, 229);
            grid.Rows[row].Cells[3].Style.BackColor = serviceColor;
            configure.Invoke(null, [dark]);
            Pump();
            if (grid.Rows[row].Cells[3].Style.BackColor != serviceColor)
                throw new Exception("Tema sobrescreveu cor do serviço.");
            Console.WriteLine($"LAYOUT: form={form.ClientSize}, module={panel.ClientSize}, agenda={agenda.ClientSize}, grid={grid.Size}");
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(image, form.ClientRectangle);
            image.Save("ui-smoke-output/agenda-" + (dark ? "escura" : "clara") + ".png");
            if (grid.Width < 500 || grid.Height < 40) throw new Exception("Grade operacional ficou sem espaço.");
        }
        var editorType = assembly.GetType("CentralWhatsApp.WebView2.GoogleCalendarEventDialog", true)!;
        var choiceType = assembly.GetType("CentralWhatsApp.WebView2.GoogleEventTypeChoice", true)!;
        var choices = Array.CreateInstance(choiceType, 2);
        choices.SetValue(Activator.CreateInstance(choiceType, ["desratizacao", "Desratização", "9"]), 0);
        choices.SetValue(Activator.CreateInstance(choiceType, ["orcamento", "Orçamento", "5"]), 1);
        using var editor = (Form)Activator.CreateInstance(editorType,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [null, new DateOnly(2026, 10, 27), false, choices], null)!;
        editor.Show(); Pump();
        var selector = (ComboBox)editorType.GetField("serviceType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        if (!selector.Visible || !selector.Enabled || selector.Items.Count != 3 ||
            selector.DropDownStyle != ComboBoxStyle.DropDownList)
            throw new Exception("Seletor do tipo de evento não está acessível.");
        selector.SelectedIndex = 2;
        foreach (var dark in new[] { false, true })
        {
            configure.Invoke(null, [dark]); Pump();
            using var capture = new Bitmap(editor.Width, editor.Height);
            editor.DrawToBitmap(capture, new Rectangle(Point.Empty, editor.Size));
            capture.Save("ui-smoke-output/editor-tipo-" + (dark ? "escuro" : "claro") + ".png");
        }
        editor.Close();
        form.Close();
        Console.WriteLine("PASS: painel Windows inicia, aplica temas, preserva cor de serviço e mantém grade operacional.");
        return 0;
    }
    private static void Pump()
    {
        for (var i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(30); }
    }
}
