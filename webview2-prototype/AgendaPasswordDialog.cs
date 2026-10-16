using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

/// <summary>
/// Recuperação direta por código recebido no e-mail.
/// O código e a senha só trafegam ao serviço Supabase Auth por HTTPS.
/// Não persistir OTP, senha ou token no computador.
/// </summary>
internal sealed class AgendaPasswordDialog : Form
{
    private const string ApiRoot = "https://snypbekmjseayljvltwq.supabase.co/";
    private const string PublicKey = "sb_publishable_X7TR995xLWtmFxrZBT65MA_yNUaizrC";
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri(ApiRoot),
        Timeout = TimeSpan.FromSeconds(25)
    };

    private readonly TextBox emailBox = new() { Width = 410, PlaceholderText = "E-mail da Agenda" };
    private readonly TextBox codeBox = new() { Width = 220, PlaceholderText = "Código recebido por e-mail" };
    private readonly TextBox passwordBox = new()
        { Width = 350, PlaceholderText = "Nova senha", UseSystemPasswordChar = true };
    private readonly TextBox confirmBox = new()
        { Width = 350, PlaceholderText = "Repita a nova senha", UseSystemPasswordChar = true };
    private readonly Label statusLabel = new()
    {
        AutoSize = false, Width = 440, Height = 64,
        ForeColor = Color.FromArgb(64, 81, 103),
        Text = "Solicite um código por e-mail. Não é necessário abrir nenhum link no navegador."
    };
    private readonly Button sendButton = MakeButton("Enviar código para meu e-mail");
    private readonly Button saveButton = MakeButton("Definir nova senha");
    private bool working;
    private bool codeRequested;

    internal AgendaPasswordDialog(string currentEmail)
    {
        Text = "Definir senha • Agenda São Bento";
        Width = 540;
        Height = 550;
        MinimumSize = MaximumSize = Size;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = Color.White;
        emailBox.Text = currentEmail.Trim();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(22),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true
        };
        flow.Controls.Add(new Label
        {
            Text = "Definir ou recuperar senha",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(11, 19, 43),
            AutoSize = true, Margin = new Padding(3, 0, 3, 14)
        });
        flow.Controls.Add(new Label
        {
            Text = "Use o e-mail autorizado para sua equipe.",
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true, Margin = new Padding(3, 0, 3, 12)
        });
        flow.Controls.Add(emailBox);
        flow.Controls.Add(sendButton);
        flow.Controls.Add(new Label
        {
            Text = "Código enviado pelo Supabase",
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true, Margin = new Padding(3, 16, 3, 4)
        });
        flow.Controls.Add(codeBox);
        flow.Controls.Add(new Label
        {
            Text = "Nova senha (mínimo de 12 caracteres)",
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true, Margin = new Padding(3, 13, 3, 4)
        });
        flow.Controls.Add(passwordBox);
        flow.Controls.Add(confirmBox);
        flow.Controls.Add(saveButton);
        flow.Controls.Add(statusLabel);
        Controls.Add(flow);
        saveButton.Enabled = false;
        sendButton.Click += async (_, _) => await SendCode();
        saveButton.Click += async (_, _) => await ChangePassword();
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text, AutoSize = true, Height = 36, Margin = new Padding(3, 9, 3, 6),
        Padding = new Padding(10, 5, 10, 5),
        BackColor = Color.FromArgb(0, 103, 44), ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    private static StringContent Json(object data) =>
        new(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

    private void ShowStatus(string message) => statusLabel.Text = message;

    private void SetBusy(bool value)
    {
        working = value;
        sendButton.Enabled = !value;
        saveButton.Enabled = !value && codeRequested;
        emailBox.Enabled = !value && !codeRequested;
    }

    private async Task SendCode()
    {
        if (working) return;
        var email = emailBox.Text.Trim();
        if (email.Length == 0 || !email.Contains('@'))
        {
            ShowStatus("Confira o e-mail cadastrado na Agenda.");
            return;
        }
        SetBusy(true);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/recover");
            request.Headers.Add("apikey", PublicKey);
            request.Content = Json(new { email });
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Não foi possível solicitar o código. Aguarde e tente novamente.");

            codeRequested = true;
            ShowStatus("Solicitação enviada. Veja o e-mail de recuperação e digite o código recebido aqui. " +
                       "Se chegar apenas um link, o modelo de e-mail do Supabase precisa incluir {{ .Token }}.");
            codeBox.Focus();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ShowStatus(error is InvalidOperationException ? error.Message :
                "Não foi possível comunicar com o Supabase. Verifique a conexão.");
        }
        finally { SetBusy(false); }
    }

    private async Task ChangePassword()
    {
        if (working || !codeRequested) return;
        var code = codeBox.Text.Trim();
        var password = passwordBox.Text;
        if (code.Length < 6 || !code.All(char.IsDigit))
        {
            ShowStatus("Digite o código numérico completo que chegou ao e-mail.");
            return;
        }
        if (password.Length < 12 || password != confirmBox.Text)
        {
            ShowStatus("A senha precisa ter pelo menos 12 caracteres e coincidir nos dois campos.");
            return;
        }
        SetBusy(true);
        try
        {
            // O código de recuperação é verificado no próprio Supabase, sem navegador ou callback.
            using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "auth/v1/verify");
            verifyRequest.Headers.Add("apikey", PublicKey);
            verifyRequest.Content = Json(new
            {
                email = emailBox.Text.Trim(),
                token = code,
                type = "recovery"
            });
            using var verification = await http.SendAsync(verifyRequest);
            if (!verification.IsSuccessStatusCode)
                throw new InvalidOperationException("Código inválido ou expirado. Solicite outro código.");

            using var session = JsonDocument.Parse(await verification.Content.ReadAsStringAsync());
            var accessToken = session.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("Não foi possível validar o código.");

            // Atualiza somente a conta autenticada pelo OTP; nunca usa chave secreta.
            using var updateRequest = new HttpRequestMessage(HttpMethod.Put, "auth/v1/user");
            updateRequest.Headers.Add("apikey", PublicKey);
            updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            updateRequest.Content = Json(new { password });
            using var updateResponse = await http.SendAsync(updateRequest);
            if (!updateResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("Não foi possível salvar a senha. " +
                    "Confira as regras de senha do Supabase e solicite um novo código.");

            passwordBox.Clear();
            confirmBox.Clear();
            codeBox.Clear();
            MessageBox.Show(this, "Senha definida com sucesso! Volte à Agenda e entre com seu e-mail e a nova senha.",
                "Agenda São Bento", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowStatus(error is InvalidOperationException ? error.Message :
                "Falha de comunicação com o Supabase. Tente novamente.");
        }
        finally { SetBusy(false); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) http.Dispose();
        base.Dispose(disposing);
    }
}
