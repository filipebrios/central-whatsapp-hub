namespace CentralWhatsApp.WebView2;

/// <summary>
/// Conexão oficial do Google Calendar; credenciais escolhidas pelo usuário no próprio Windows.
/// Nunca solicita senha nem transfere o JSON ao backend da São Bento.
/// </summary>
internal sealed class GoogleCalendarManageDialog : Form
{
    private readonly GoogleDesktopOAuth oauth;
    private readonly GoogleCalendarApi api;
    private readonly Label state = new()
    {
        Dock = DockStyle.Top, Height = 95, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(18, 7, 18, 7),
        ForeColor = Color.FromArgb(49, 70, 79)
    };
    private readonly ComboBox calendars = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Height = 32
    };
    private readonly Button chooseFile = Button("1. Importar credenciais do Google"),
        authorize = Button("2. Autorizar no navegador"),
        select = Button("3. Usar calendário selecionado"),
        verify = Button("4. Verificar evento de teste"),
        disconnect = Button("Desconectar este Windows"),
        cancelAuthorization = Button("Cancelar autorização");
    private CancellationTokenSource? authorizationCancellation;
    private readonly DateTimePicker testDate = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy",
        Value = new DateTime(2026, 10, 23), Width = 180
    };
    private bool busy;
    internal bool Changed { get; private set; }

    internal GoogleCalendarManageDialog(GoogleDesktopOAuth oauth, GoogleCalendarApi api)
    {
        this.oauth = oauth;
        this.api = api;
        Text = "Conta Google • Agenda São Bento";
        Width = 610; Height = 490; MinimumSize = MaximumSize = Size;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20, 17, 20, 14),
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true
        };
        body.Controls.Add(new Label
        {
            Text = "Google Agenda com edição",
            AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold),
            Margin = new Padding(2, 3, 2, 10)
        });
        body.Controls.Add(new Label
        {
            Text = "Use o JSON de um cliente OAuth 2.0 do tipo Aplicativo para computador,\n" +
                   "criado no Google Cloud com a API Calendar ativada.",
            AutoSize = true, ForeColor = Color.FromArgb(78, 88, 99)
        });
        body.Controls.Add(chooseFile);
        body.Controls.Add(authorize);
        cancelAuthorization.BackColor = Color.FromArgb(83, 96, 107);
        cancelAuthorization.Click += (_, _) => authorizationCancellation?.Cancel();
        body.Controls.Add(cancelAuthorization);
        body.Controls.Add(new Label
        {
            Text = "Nesta fase, somente MODUX - TESTES pode receber alterações:",
            AutoSize = true, Margin = new Padding(5, 18, 5, 4)
        });
        calendars.Width = 520;
        calendars.SelectedIndexChanged += (_, _) => RefreshStatus();
        body.Controls.Add(calendars);
        body.Controls.Add(select);
        body.Controls.Add(new Label
        {
            Text = "Data do compromisso fictício TESTE MODUX:",
            AutoSize = true, Margin = new Padding(5, 10, 5, 4)
        });
        body.Controls.Add(testDate);
        body.Controls.Add(verify);
        body.Controls.Add(disconnect);
        state.Width = 540;
        body.Controls.Add(state);
        Controls.Add(body);
        chooseFile.Click += (_, _) => ImportCredentials();
        authorize.Click += async (_, _) => await Authorize();
        select.Click += (_, _) => SelectCalendar();
        verify.Click += async (_, _) => await VerifyTestEvent();
        disconnect.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                "Desconectar o Google deste Windows? Nenhum evento será excluído.",
                "Desconectar Google", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            oauth.Disconnect();
            Changed = true;
            calendars.Items.Clear();
            RefreshStatus();
        };
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            if (authorizationCancellation is not null)
            {
                authorizationCancellation.Cancel();
                state.Text = "Cancelando a autorização. Aguarde e feche a janela novamente.";
            }
            else state.Text = "Aguarde a consulta Google terminar antes de fechar.";
        };
        Shown += async (_, _) =>
        {
            RefreshStatus();
            if (!oauth.Connected) return;
            busy = true;
            RefreshStatus();
            try { await RefreshCalendars(); }
            finally
            {
                var result = state.Text;
                busy = false;
                RefreshStatus();
                state.Text = result;
            }
        };
    }

    private static Button Button(string title) => new()
    {
        Text = title, Height = 35, AutoSize = true,
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 103, 44),
        ForeColor = Color.White, Padding = new Padding(10, 5, 10, 5),
        Margin = new Padding(4, 7, 4, 5)
    };

    private void RefreshStatus()
    {
        cancelAuthorization.Visible = authorizationCancellation is not null;
        cancelAuthorization.Enabled = authorizationCancellation is { IsCancellationRequested: false };
        chooseFile.Enabled = !busy;
        authorize.Enabled = !busy && oauth.Configured;
        calendars.Enabled = disconnect.Enabled = testDate.Enabled = !busy;
        select.Enabled = !busy && oauth.Connected &&
            calendars.SelectedItem is GoogleCalendarChoice choice &&
            GoogleCalendarWriteGuard.IsWritableTestCalendar(choice.Name, choice.Id, true);
        verify.Enabled = !busy && oauth.CanWriteSelectedCalendar;
        state.Text = oauth.Connected
            ? oauth.CanWriteSelectedCalendar
                ? "Conta autorizada. Teste controlado ativo em: " +
                  (oauth.CalendarName ?? "MODUX - TESTES") +
                  ".\nA agenda operacional São Bento permanece protegida."
                : "Conta autorizada. Crie no Google Agenda um calendário separado chamado " +
                  "MODUX - TESTES e selecione-o aqui. A agenda real NÃO será alterada."
            : oauth.Configured
                ? "Credenciais instaladas. Clique em Autorizar para conceder acesso no navegador."
                : "Nenhuma conta Google autorizada. Importe o JSON OAuth Desktop primeiro.";
    }

    private void ImportCredentials()
    {
        using var chooser = new OpenFileDialog
        {
            Title = "Credenciais OAuth para aplicativo Desktop",
            Filter = "Arquivo JSON (*.json)|*.json",
            CheckFileExists = true, Multiselect = false
        };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var file = new FileInfo(chooser.FileName);
            if (file.Length > 32_768) throw new InvalidOperationException("Arquivo OAuth inesperadamente grande.");
            oauth.LoadDesktopCredentials(File.ReadAllText(chooser.FileName));
            Changed = true;
            RefreshStatus();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or InvalidOperationException)
        { MessageBox.Show(this, error.Message, "Google Agenda"); }
    }

    private async Task Authorize()
    {
        if (busy) return;
        using var cancellation = new CancellationTokenSource();
        authorizationCancellation = cancellation;
        busy = true;
        RefreshStatus();
        var cancelled = false;
        state.Text = "Aguardando autorização no navegador padrão (até 4 minutos). Use Cancelar autorização para interromper.";
        try
        {
            await oauth.AuthorizeAsync(cancellation.Token);
            authorizationCancellation = null;
            RefreshStatus();
            Changed = true;
            await RefreshCalendars();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { cancelled = true; }
        catch (Exception error) when (error is InvalidOperationException or
            HttpRequestException or TaskCanceledException or IOException)
        {
            MessageBox.Show(this, error.Message, "Autorização Google",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            authorizationCancellation = null;
            busy = false;
            RefreshStatus();
            if (cancelled) state.Text = "Autorização cancelada. Você pode tentar novamente sem reiniciar o verificador.";
        }
    }

    private async Task RefreshCalendars()
    {
        try
        {
            calendars.Items.Clear();
            var choices = await api.Calendars();
            foreach (var calendar in choices) calendars.Items.Add(calendar);
            if (calendars.Items.Count > 0)
            {
                var current = oauth.CalendarId;
                var index = choices.ToList().FindIndex(c => c.Id == current);
                calendars.SelectedIndex = index >= 0 ? index : 0;
                RefreshStatus();
            }
            else state.Text = "Sua conta não apresenta calendários com permissão para editar.";
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException
            or TaskCanceledException)
        { state.Text = "Não foi possível carregar a lista: " + error.Message; }
    }

    private async Task VerifyTestEvent()
    {
        // Verificação sem qualquer POST/PATCH/DELETE. O usuário criou um
        // compromisso fictício nesta data no calendário secundário.
        if (!oauth.CanWriteSelectedCalendar)
        {
            MessageBox.Show(this, "Selecione primeiro a agenda isolada MODUX - TESTES.",
                "Google Agenda", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (busy) return;
        var selectedDate = DateOnly.FromDateTime(testDate.Value);
        var dateLabel = selectedDate.ToString("dd/MM/yyyy");
        busy = true;
        RefreshStatus();
        state.Text = $"Conferindo leitura do evento fictício de {dateLabel}...";
        try
        {
            var events = await api.Events(selectedDate, selectedDate);
            var expected = events.FirstOrDefault(e =>
                e.Date == selectedDate &&
                e.Title.Contains("TESTE MODUX", StringComparison.OrdinalIgnoreCase));
            if (expected is null)
            {
                state.Text = "Conexão autorizada, mas o evento TESTE MODUX não foi encontrado na agenda selecionada.";
                MessageBox.Show(this,
                    $"A conexão Google funcionou, mas não encontrei o compromisso fictício de {dateLabel}. " +
                    "Confira a data e o calendário do evento TESTE MODUX e tente novamente.",
                    "Teste de leitura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                state.Text = "Leitura Google confirmada no calendário de testes. Nenhum evento foi alterado.";
                MessageBox.Show(this,
                    "Evento localizado no Google Agenda de testes:\n" +
                    expected.Title + "\nData: " + dateLabel + " | Hora: " + expected.Time +
                    "\n\nEste teste apenas LEU os dados; não criou nem alterou eventos.",
                    "Teste de leitura aprovado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or
            HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            state.Text = "Falha ao conferir a leitura do evento fictício.";
            MessageBox.Show(this, error.Message, "Google Agenda",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            // Preserve o resultado da consulta ao reabilitar os controles.
            var result = state.Text;
            busy = false;
            RefreshStatus();
            state.Text = result;
        }
    }

    private void SelectCalendar()
    {
        if (calendars.SelectedItem is not GoogleCalendarChoice item) return;
        if (!GoogleCalendarWriteGuard.IsWritableTestCalendar(item.Name, item.Id, oauth.Connected))
        {
            MessageBox.Show(this,
                "Nesta fase, selecione um calendário separado chamado MODUX - TESTES. " +
                "As agendas operacionais não podem ser editadas.",
                "Proteção da agenda São Bento", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
            $"Usar o calendário isolado “{item.Name}” para TESTAR criação e edição de eventos? Nenhuma agenda operacional será alterada.",
            "Confirmar calendário", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        oauth.SelectCalendar(item.Id, item.Name);
        Changed = true;
        RefreshStatus();
    }
}
