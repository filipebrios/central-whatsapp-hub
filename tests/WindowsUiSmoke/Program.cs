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
            [null, new DateOnly(2026, 10, 27), false, choices, null, null, null], null)!;
        editor.Show(); Pump();
        var selector = (CheckedListBox)editorType.GetField("serviceType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        if (!selector.Visible || !selector.Enabled || selector.Items.Count != 2 || !selector.CheckOnClick)
            throw new Exception("Seletor do tipo de evento não está acessível.");
        selector.SetItemChecked(0, true); selector.SetItemChecked(1, true);
        if (selector.CheckedItems.Count != 2) throw new Exception("Não permitiu dois serviços no editor.");
        foreach (var dark in new[] { false, true })
        {
            configure.Invoke(null, [dark]); Pump();
            using var capture = new Bitmap(editor.Width, editor.Height);
            editor.DrawToBitmap(capture, new Rectangle(Point.Empty, editor.Size));
            capture.Save("ui-smoke-output/editor-tipo-" + (dark ? "escuro" : "claro") + ".png");
        }
        editor.Close();
        CheckCalendarViews(assembly, panel, form, configure);
        CheckGoogleContactEditor(assembly, form, configure);
        CheckConfirmations(assembly, form, configure);
        CheckPublicFirstRun(assembly);
        form.Close();
        Console.WriteLine("PASS: painel Windows inicia, aplica temas, preserva cor de serviço e mantém grade operacional.");
        return 0;
    }

    private static void CheckGoogleContactEditor(Assembly assembly, Form owner, MethodInfo configure)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sourceType = assembly.GetType("CentralWhatsApp.WebView2.GoogleEventSnapshot", true)!;
        var source = Activator.CreateInstance(sourceType,
            ["event-contact-ui", "v1", "Cliente fictício", "", "Endereço fictício", new DateOnly(2026,10,27), "14:00", false, 60, null, null]);
        var editorType = assembly.GetType("CentralWhatsApp.WebView2.GoogleCalendarEventDialog", true)!;
        var contactChoiceType = assembly.GetType("CentralWhatsApp.WebView2.GoogleEventTypeChoice", true)!;
        var contactChoices = Array.CreateInstance(contactChoiceType, 2);
        contactChoices.SetValue(Activator.CreateInstance(contactChoiceType, ["desinsetizacao", "Desinsetização", "0"]), 0);
        contactChoices.SetValue(Activator.CreateInstance(contactChoiceType, ["reservatorio", "Higienização de reservatório", "7"]), 1);
        var writes = 0; string savedPhone = "";
        Func<string, string[], Task> saveContact = (phone, ids) => { writes++; savedPhone = phone; return Task.CompletedTask; };
        using var editor = (Form)Activator.CreateInstance(editorType, flags, null,
            [source, new DateOnly(2026,10,27), false, contactChoices, new[]{"desinsetizacao","reservatorio"}, "", saveContact], null)!;
        editor.Show(owner); Pump();
        if (!Screen.FromControl(editor).WorkingArea.Contains(editor.Bounds)) throw new Exception("Editor e botões ficaram fora da área útil.");
        var phoneBox = (TextBox)editorType.GetField("phone", flags)!.GetValue(editor)!;
        var savePhone = (Button)editorType.GetField("savePhone", flags)!.GetValue(editor)!;
        if (!phoneBox.Visible || !phoneBox.Enabled || !savePhone.Enabled) throw new Exception("WhatsApp não está editável no evento Google.");
        phoneBox.Text = "123"; savePhone.PerformClick(); Pump();
        if (writes != 0) throw new Exception("Telefone inválido foi salvo pela tela.");
        phoneBox.Text = "(35) 99999-1111"; savePhone.PerformClick(); Pump();
        if (writes != 1 || savedPhone != "5535999991111" || !(bool)editorType.GetProperty("ContactSaved", flags)!.GetValue(editor)!)
            throw new Exception("Salvar WhatsApp não persistiu telefone válido.");
        foreach (var dark in new[] { false, true })
        { configure.Invoke(null, [dark]); Pump(); Capture(editor, "editor-whatsapp-" + (dark ? "escuro" : "claro")); }
        editor.Close();
        Console.WriteLine("PASS: editor Google oferece telefone/Salvar WhatsApp, valida DDD e mantém sucesso ao voltar; zero envio.");
    }

    private static bool allowDraft;
    private static readonly List<(string Phone, string Text)> drafts = [];
    private static Task<T> FixtureValidate<T>(T work) => Task.FromResult(work);
    private static Task<bool> FixtureDraft<T>(T request)
    {
        if (allowDraft)
        {
            var type = request!.GetType();
            drafts.Add(((string)type.GetProperty("Phone")!.GetValue(request)!, (string)type.GetProperty("Text")!.GetValue(request)!));
        }
        return Task.FromResult(allowDraft);
    }

    private static void CheckConfirmations(Assembly assembly, Form owner, MethodInfo configure)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var storeType = assembly.GetType("CentralWhatsApp.WebView2.AgendaWorkflowStore", true)!;
        var dialogType = assembly.GetType("CentralWhatsApp.WebView2.AgendaConfirmationDialog", true)!;
        var workType = assembly.GetType("CentralWhatsApp.WebView2.AgendaWorkItem", true)!;
        var requestType = assembly.GetType("CentralWhatsApp.WebView2.ComposeRequest", true)!;
        var today = new DateOnly(2026, 10, 27);
        var company = "11111111-1111-1111-1111-111111111111";
        var first = "22222222-2222-2222-2222-222222222222";
        var second = "33333333-3333-3333-3333-333333333333";
        var third = "44444444-4444-4444-4444-444444444444";
        var states = new Dictionary<string, string>();
        var marks = 0;
        HttpResponseMessage Json(object value) => new(System.Net.HttpStatusCode.OK)
        { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(value)) };
        Func<HttpMethod, string, object?, Task<HttpResponseMessage>> transport = (method, path, data) =>
        {
            if (method == HttpMethod.Get && path.StartsWith("rest/v1/agenda_appointments?"))
                return Task.FromResult(Json(new[] {
                    new { id=first, revision=1, client="Cliente Alfa", phone="(35) 99999-1111", primary_service="Desinsetização + Limpeza de reservatório", service_ids=new[]{"desinsetizacao","reservatorio"}, date="2026-10-27", time="08:00", status="agendado", address="Endereço de teste", duration=60 },
                    new { id=second, revision=1, client="Cliente Beta", phone="(35) 99999-2222", primary_service="Desratização", service_ids=new[]{"desratizacao"}, date="2026-10-28", time="14:00", status="agendado", address="Outro endereço", duration=60 },
                    new { id=third, revision=1, client="Completar contato", phone="", primary_service="Reservatório", service_ids=new[]{"reservatorio"}, date="2026-10-28", time="16:00", status="agendado", address="", duration=60 }
                }));
            if (method == HttpMethod.Get && path.StartsWith("rest/v1/agenda_message_templates?"))
                return Task.FromResult(Json(new[] { new { key="confirmacao", body="Olá, {cliente}! Podemos confirmar {servico} para {data} às {hora}? Endereço: {endereco}." } }));
            string Occurrence(string id) => id == first ? "2026-10-27T08:00" : "2026-10-28T14:00";
            object Job(string id) => new { id, appointment_id=id, template_key="confirmacao", occurrence_key=Occurrence(id), state=states[id] };
            if (method == HttpMethod.Get && path.StartsWith("rest/v1/agenda_message_queue?"))
                return Task.FromResult(Json(states.Keys.Select(Job).ToArray()));
            if (method == HttpMethod.Post && path == "rest/v1/rpc/agenda_prepare_manual_message")
            {
                var payload = System.Text.Json.JsonSerializer.SerializeToElement(data);
                var id = payload.GetProperty("p_appointment_id").GetString()!;
                if (payload.GetProperty("p_company_id").GetString() != company) throw new Exception("Empresa incorreta na fila.");
                states.TryAdd(id, "preparada"); return Task.FromResult(Json(Job(id)));
            }
            if (method == HttpMethod.Post && path == "rest/v1/rpc/agenda_issue_response_token")
                return Task.FromResult(Json(new { token=new string('a',64), expires_at=DateTimeOffset.UtcNow.AddHours(1).ToString("O") }));
            if (method == HttpMethod.Patch && path.StartsWith("rest/v1/agenda_message_queue?"))
            {
                if (!path.Contains("company_id=eq." + company) || !path.Contains("state=eq.preparada")) throw new Exception("Registro sem CAS/empresa.");
                var id = path.Contains("id=eq." + first) ? first : second;
                marks++; states[id] = "enviada_manual"; return Task.FromResult(Json(new[] { Job(id) }));
            }
            throw new Exception("HTTP inesperado na fila fictícia: " + path);
        };
        var store = Activator.CreateInstance(storeType, flags | BindingFlags.Public, null, [transport], null)!;
        var validate = typeof(Program).GetMethod(nameof(FixtureValidate), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(workType).CreateDelegate(typeof(Func<,>).MakeGenericType(workType, typeof(Task<>).MakeGenericType(workType)));
        var open = typeof(Program).GetMethod(nameof(FixtureDraft), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(requestType).CreateDelegate(typeof(Func<,>).MakeGenericType(requestType, typeof(Task<bool>)));
        using var queue = (Form)Activator.CreateInstance(dialogType, flags, null,
            [store, company, validate, open, (Func<DateOnly>)(() => today), (Func<string,bool>)(_ => true)], null)!;
        queue.Show(owner); Pump();
        if (!Screen.FromControl(queue).WorkingArea.Contains(queue.Bounds)) throw new Exception("Fila e botões ficaram fora da área útil.");
        T Field<T>(string name) => (T)dialogType.GetField(name, flags)!.GetValue(queue)!;
        var list = Field<ListBox>("list"); var prepare = Field<Button>("prepare"); var sent = Field<Button>("sentNext");
        if (list.Items.Count != 3 || !prepare.Enabled || sent.Enabled || !Field<TextBox>("preview").Text.Contains("Desinsetização + Limpeza"))
            throw new Exception("Fila inicial/prévia múltipla incorreta.");
        foreach (var dark in new[] { false, true })
        { configure.Invoke(null, [dark]); Pump(); Capture(queue, "confirmacoes-" + (dark ? "escuro" : "claro")); }
        allowDraft = false; drafts.Clear(); prepare.PerformClick(); Pump();
        if (sent.Enabled || marks != 0 || drafts.Count != 0) throw new Exception("Cancelar revisão foi interpretado como envio.");
        allowDraft = true; prepare.PerformClick(); Pump();
        if (!sent.Enabled || drafts.Count != 1 || marks != 0 || drafts[0].Phone != "5535999991111" ||
            !drafts[0].Text.Contains("Cliente Alfa") || !drafts[0].Text.Contains("27/10/2026 às 08:00") ||
            !drafts[0].Text.Contains("modux-resposta.html#token="))
            throw new Exception("Abrir conversa perdeu dados/link ou registrou envio sozinho.");
        Capture(queue, "confirmacoes-navegador-compacto");
        sent.PerformClick(); Pump();
        if (marks != 1 || drafts.Count != 2 || drafts[1].Phone != "5535999992222" ||
            !drafts[1].Text.Contains("Cliente Beta") || !drafts[1].Text.Contains("28/10/2026 às 14:00") || list.Items.Count != 2)
            throw new Exception("Enviei e próxima não avançou para o cliente correto.");
        Field<Button>("skip").PerformClick(); Pump();
        if (marks != 1 || sent.Enabled || prepare.Enabled || drafts.Count != 2 || list.Items.Count != 1)
            throw new Exception("Pular marcou envio ou contato inválido abriu conversa.");
        Field<Button>("restoreSkipped").PerformClick(); Pump();
        if (list.Items.Count != 2 || states[second] != "preparada") throw new Exception("Rever pulados perdeu pendência compartilhada.");
        queue.Close();
        Console.WriteLine("PASS: fila Windows real, prévia, revisão cancelada, abertura individual/link, envio manual separado, próxima e pular sem envio; zero rede real.");
    }

    private static void CheckCalendarViews(Assembly assembly, Control panel, Form form, MethodInfo configure)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var calendar = (Control)panel.GetType().GetField("calendar", flags)!.GetValue(panel)!;
        var entryType = assembly.GetType("CentralWhatsApp.WebView2.AgendaCalendarEntry", true)!;
        var colorType = assembly.GetType("CentralWhatsApp.WebView2.AgendaServiceColors", true)!;
        var category = colorType.GetField("Desinsetizacao", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fixtures = Array.CreateInstance(entryType, 6);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today.AddDays(-1), "08:00", "Visita passada", "Desinsetização + Reservatório", "agendado", category, 60, false, "g:past", null]), 0);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today, "08:00", "Visita de hoje", "Desinsetização", "agendado", category, 90, false, "g:today", null]), 1);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today.AddDays(1), "08:00", "Visita futura", "Desinsetização", "agendado", category, 120, false, "g:next", null]), 2);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today.AddDays(1), "08:30", "Visita simultânea", "Reservatório", "agendado", category, 60, false, "g:overlap", null]), 3);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today, "Dia todo", "Evento dia inteiro", "Interno", "agendado", category, 1440, true, "g:all", today.AddDays(2)]), 4);
        fixtures.SetValue(Activator.CreateInstance(entryType, [today.AddDays(-1), "23:30", "Cruza meia-noite", "Desinsetização", "agendado", category, 90, false, "g:night", null]), 5);
        var modeType = assembly.GetType("CentralWhatsApp.WebView2.AgendaCalendarMode", true)!;
        calendar.GetType().GetMethod("SetEntries")!.Invoke(calendar, [fixtures]);
        foreach (var dark in new[] { false, true })
        foreach (var mode in new[] { "Month", "Week", "Day" })
        {
            configure.Invoke(null, [dark]);
            var period = (ComboBox)panel.GetType().GetField("period", flags)!.GetValue(panel)!;
            period.SelectedIndex = mode switch { "Day" => 0, "Week" => 1, _ => 2 };
            Pump();
            if (calendar.GetType().GetProperty("Mode", flags)!.GetValue(calendar)!.ToString() != mode)
                throw new Exception("O seletor de período não mudou a visão real do calendário.");
            if (Descendants(calendar).Count() > 60) throw new Exception("Calendário voltou a criar controles por evento.");
            Capture(form, "calendario-" + mode.ToLowerInvariant() + (dark ? "-escuro" : "-claro"));
        }
        // Stress the actual drawing surface with 500 events; log time without a flaky runner-speed assertion.
        var stress = Array.CreateInstance(entryType, 500);
        for (var i = 0; i < 500; i++) stress.SetValue(Activator.CreateInstance(entryType,
            [today.AddDays(i % 7 - 3), $"{7 + i % 12:00}:00", "Visita fictícia " + i, "Serviço", "agendado", category, 60, false, "fixture-" + i, null]), i);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        calendar.GetType().GetMethod("SetEntries")!.Invoke(calendar, [stress]);
        for (var i = 0; i < 8; i++)
        {
            calendar.GetType().GetMethod("SetView", flags)!.Invoke(calendar, [today, Enum.Parse(modeType, i % 2 == 0 ? "Week" : "Day")]);
            calendar.Refresh();
        }
        timer.Stop();
        Console.WriteLine($"PERF: 500 eventos, 8 trocas/redesenhos semana/dia, {timer.ElapsedMilliseconds} ms, {Descendants(calendar).Count()} controles nativos.");
        calendar.GetType().GetMethod("SetEntries")!.Invoke(calendar, [fixtures]);
        Console.WriteLine("PASS: visões reais mês/semana/dia, eventos simultâneos/dia inteiro, seletor múltiplo e superfície constante.");
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
            ((Button)typeof(MainForm).GetField("collapseButton", flags)!.GetValue(main)!).PerformClick(); Pump();
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
