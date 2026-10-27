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
        CheckPublicFirstRun(assembly);
        form.Close();
        Console.WriteLine("PASS: painel Windows inicia, aplica temas, preserva cor de serviço e mantém grade operacional.");
        return 0;
    }

    private static void CheckPublicFirstRun(Assembly assembly)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "modux-public-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var storage = assembly.GetType("CentralWhatsApp.WebView2.ModuxAccountStorage", true)!;
            var load = storage.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
            object[] Read(string path) => ((System.Collections.IEnumerable)load.Invoke(null, [path])!).Cast<object>().ToArray();
            if (Read(temporary).Length != 0 || File.Exists(Path.Combine(temporary, "accounts.json")))
                throw new Exception("Instalação nova criou conta ou nome predefinido.");
            var legacy = Path.Combine(temporary, "legacy");
            Directory.CreateDirectory(Path.Combine(legacy, "waseller-account"));
            File.WriteAllText(Path.Combine(legacy, "waseller-account", "session-sentinel"), "preserve");
            var recovered = Read(legacy);
            if (recovered.Length != 1 || recovered[0].GetType().GetProperty("Id")!.GetValue(recovered[0]) as string != "waseller-account")
                throw new Exception("Sessão da versão antiga não pode ser recuperada.");
            var emptyFile = Path.Combine(legacy, "accounts.json");
            File.WriteAllText(emptyFile, "[]");
            if (Read(legacy).Length != 0) throw new Exception("Conta removida reapareceu a partir do perfil preservado.");
            var existing = Path.Combine(temporary, "existing"); Directory.CreateDirectory(existing);
            var file = Path.Combine(existing, "accounts.json");
            const string accounts = """[{"Id":"waseller-account","Name":"Atendimento"},{"Id":"account-fixture","Name":"Pessoal"}]""";
            File.WriteAllText(file, accounts);
            if (Read(existing).Length != 2 || File.ReadAllText(file) != accounts)
                throw new Exception("Atualização perdeu ou reescreveu as contas existentes.");
            var corrupt = Path.Combine(temporary, "corrupt"); Directory.CreateDirectory(corrupt);
            var corruptFile = Path.Combine(corrupt, "accounts.json"); File.WriteAllText(corruptFile, "{broken");
            try { Read(corrupt); throw new Exception("Cadastro corrompido foi aceito."); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
            if (File.ReadAllText(corruptFile) != "{broken" || Directory.GetFiles(corrupt, "accounts-recovery-*.json").Length != 1)
                throw new Exception("Cadastro corrompido não foi preservado para recuperação.");

            using var main = (Form)Activator.CreateInstance(typeof(MainForm), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [temporary, false], null)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetMethod("LoadAccounts", flags)!.Invoke(main, null);
            typeof(MainForm).GetMethod("ApplyPreferences", flags)!.Invoke(main, null);
            main.Show(); Pump();
            typeof(MainForm).GetMethod("ApplyPreferences", flags)!.Invoke(main, null); Pump();
            var welcome = (Control)typeof(MainForm).GetField("welcomePanel", flags)!.GetValue(main)!;
            var aboutButton = (Control)typeof(MainForm).GetField("aboutButton", flags)!.GetValue(main)!;
            if (!welcome.Visible || !aboutButton.Visible || !aboutButton.Enabled ||
                Descendants(main).Any(c => c.Text.Contains("São Bento", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Primeira abertura não ficou neutra ou o botão Sobre desapareceu.");
            if (aboutButton.BackColor != Color.FromArgb(240, 240, 243))
                throw new Exception("Tema ARANY não alcançou os botões da janela principal.");
            Capture(main, "primeira-abertura-arany");
            var initialSize = main.Size;
            main.Size = new Size(1024, 640); Pump(); Capture(main, "primeira-abertura-1024");
            main.Size = initialSize;
            typeof(MainForm).GetMethod("ToggleSidebar", flags)!.Invoke(main, null); Pump();
            if (!aboutButton.Visible || aboutButton.Width < 35) throw new Exception("Sobre não está acessível no menu recolhido.");
            Capture(main, "primeira-abertura-menu-recolhido");
            using var about = (Form)typeof(MainForm).GetMethod("CreateAboutDialog", flags)!.Invoke(main, null)!;
            about.Show(); Pump();
            assembly.GetType("CentralWhatsApp.WebView2.ModuxAppearance", true)!
                .GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false]); Pump();
            var text = string.Join("\n", Descendants(about).Select(c => c.Text));
            if (!text.Contains("Desenvolvido e disponibilizado por ARANY") || !text.Contains("www.arany.com.br") || !text.Contains("Versão " + assembly.GetName().Version!.ToString(3)))
                throw new Exception("Créditos, site ou versão ausentes do Sobre.");
            Capture(about, "sobre-arany"); about.Close();
            if (File.Exists(Path.Combine(temporary, "accounts.json"))) throw new Exception("Primeira abertura salvou conta predefinida.");
            Console.WriteLine("PASS: instalação nova vazia, atualização preserva contas, perfil legado recuperável, lista removida não reaparece, Sobre/ARANY e layout 1024.");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Capture(Form form, string name)
    {
        form.BringToFront(); form.Activate(); Pump();
        using var image = new Bitmap(form.Width, form.Height);
        using (var graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
        image.Save("ui-smoke-output/" + name + ".png");
    }
    private static void Pump()
    {
        for (var i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(30); }
    }
}
