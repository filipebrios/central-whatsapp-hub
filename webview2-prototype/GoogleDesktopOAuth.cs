using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CentralWhatsApp.WebView2;

/// <summary>Autorização instalada Google OAuth 2.0 com PKCE e credenciais locais DPAPI.
/// Nenhuma credencial é distribuída no binário, e o login ocorre no navegador padrão.</summary>
internal sealed class GoogleDesktopOAuth : IDisposable, IGoogleCalendarAuthorization
{
    internal const string EventScope = "https://www.googleapis.com/auth/calendar.events";
    internal const string ListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string storageFile;
    private Record? config;

    private sealed class Record
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset Expires { get; set; }
        public string? CalendarId { get; set; }
        public string? CalendarName { get; set; }
    }

    internal bool Configured => config is { ClientId.Length: > 0 };
    internal bool Connected => !string.IsNullOrWhiteSpace(config?.RefreshToken);
    internal string? CalendarId => config?.CalendarId;
    internal string? CalendarName => config?.CalendarName;
    // Durante o piloto somente a agenda separada de testes permite gravar.
    internal bool CanWriteSelectedCalendar =>
        GoogleCalendarWriteGuard.IsWritableTestCalendar(
            config?.CalendarName, config?.CalendarId, Connected);

    internal void DemandSafeWrite() =>
        GoogleCalendarWriteGuard.DemandTestCalendar(
            config?.CalendarName, config?.CalendarId, Connected);

    internal GoogleDesktopOAuth(bool diagnostic = false)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CentralWhatsApp", diagnostic ? "GoogleOAuthDiagnostic" : "WebView2Prototype");
        Directory.CreateDirectory(folder);
        storageFile = Path.Combine(folder, "google-oauth.dpapi");
        if (!File.Exists(storageFile)) return;
        try
        {
            var decoded = ProtectedData.Unprotect(File.ReadAllBytes(storageFile), null,
                DataProtectionScope.CurrentUser);
            config = JsonSerializer.Deserialize<Record>(decoded);
        }
        catch (Exception e) when (e is IOException or CryptographicException or JsonException)
        {
            config = null;
        }
    }

    internal void LoadDesktopCredentials(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("installed", out var installed) ||
            installed.ValueKind != JsonValueKind.Object ||
            !installed.TryGetProperty("client_id", out var clientNode) ||
            clientNode.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Selecione um JSON OAuth do tipo Aplicativo para computador.");
        var clientId = clientNode.GetString() ?? "";
        var secret = installed.TryGetProperty("client_secret", out var node) ? node.GetString() ?? "" : "";
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            || clientId.Length > 300 || secret.Length > 600)
            throw new InvalidOperationException("Credenciais OAuth inválidas.");
        if (config?.ClientId == clientId) config.ClientSecret = secret;
        else config = new Record { ClientId = clientId, ClientSecret = secret };
        Save();
    }

    internal void SelectCalendar(string id, string name)
    {
        if (!Connected || string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Autorize a conta Google e selecione um calendário válido.");
        // Bloquear a seleção de agendas operacionais nesta fase. O nome é verificado
        // novamente dentro do cliente de API antes de CADA operação de escrita.
        GoogleCalendarWriteGuard.DemandTestCalendar(name, id, Connected);
        config!.CalendarId = id;
        config.CalendarName = name;
        Save();
    }

    internal void Disconnect()
    {
        config = null;
        if (File.Exists(storageFile)) File.Delete(storageFile);
    }

    private void Save()
    {
        if (config is null) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(storageFile + ".tmp", encrypted);
        File.Move(storageFile + ".tmp", storageFile, true);
    }

    internal async Task AuthorizeAsync(CancellationToken ct = default)
    {
        var settings = config ?? throw new InvalidOperationException("Importe as credenciais OAuth Desktop primeiro.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        int port;
        using (var tcp = new TcpListener(IPAddress.Loopback, 0))
        {
            tcp.Start();
            port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        }
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        try { listener.Start(); }
        catch (HttpListenerException)
        {
            throw new InvalidOperationException("Não foi possível abrir o retorno local para autorizar o Google.");
        }
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId, ["redirect_uri"] = redirect,
            ["response_type"] = "code", ["scope"] = EventScope + " " + ListScope,
            ["state"] = state, ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256", ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" +
            string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" +
                Uri.EscapeDataString(x.Value)));
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { throw new InvalidOperationException("Não foi possível abrir o navegador de autorização."); }
        HttpListenerContext callback;
        try
        {
            callback = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(4), ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("O tempo de autorização do Google expirou.");
        }
        // HttpListener fica ativo até concluir a resposta ao navegador. O using
        // o encerra com segurança ao término da autorização.
        var receivedState = callback.Request.QueryString["state"];
        var code = callback.Request.QueryString["code"];
        var valid = callback.Request.RemoteEndPoint?.Address is { } address &&
            IPAddress.IsLoopback(address) &&
            !string.IsNullOrEmpty(code) &&
            string.Equals(receivedState, state, StringComparison.Ordinal) &&
            string.IsNullOrEmpty(callback.Request.QueryString["error"]);
        try
        {
            var reply = Encoding.UTF8.GetBytes(valid
                ? "<!doctype html><meta charset=utf-8><h2>Google autorizado. Volte ao MODUX.</h2>"
                : "<!doctype html><meta charset=utf-8><h2>Autorização não concluída. Volte ao MODUX.</h2>");
            callback.Response.ContentType = "text/html; charset=utf-8";
            callback.Response.Headers.Add("Cache-Control", "no-store");
            callback.Response.StatusCode = valid ? 200 : 400;
            await callback.Response.OutputStream.WriteAsync(reply, ct);
            callback.Response.Close();
        }
        catch (HttpListenerException) { }
        if (!valid)
            throw new InvalidOperationException("Autorização cancelada ou verificação de segurança não corresponde.");
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
                ["code"] = code!, ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect
            }), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("O Google não aceitou o código OAuth; revise as credenciais Desktop.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        ReceiveTokens(body.RootElement);
        if (!Connected)
            throw new InvalidOperationException("O Google não retornou um token de renovação. Revogue o acesso e tente novamente.");
        Save();
    }

    private void ReceiveTokens(JsonElement json)
    {
        if (config is null) throw new InvalidOperationException("Credenciais ausentes.");
        config.AccessToken = json.GetProperty("access_token").GetString();
        if (json.TryGetProperty("refresh_token", out var rt) && !string.IsNullOrWhiteSpace(rt.GetString()))
            config.RefreshToken = rt.GetString();
        config.Expires = DateTimeOffset.UtcNow.AddSeconds(
            json.TryGetProperty("expires_in", out var expiry) ? Math.Max(60, expiry.GetInt32()) : 3600);
    }

    internal async Task<string> AccessTokenAsync(CancellationToken ct = default)
    {
        if (config is not { RefreshToken.Length: > 0 } settings)
            throw new InvalidOperationException("Autorize a conta Google primeiro.");
        if (!string.IsNullOrEmpty(settings.AccessToken) &&
            settings.Expires > DateTimeOffset.UtcNow.AddMinutes(2))
            return settings.AccessToken;
        using var response = await http.PostAsync(TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
                ["refresh_token"] = settings.RefreshToken, ["grant_type"] = "refresh_token"
            }), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Sessão Google expirada ou revogada. Autorize novamente.");
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        ReceiveTokens(data.RootElement);
        Save();
        return config!.AccessToken!;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    string? IGoogleCalendarAuthorization.CalendarId => CalendarId;
    Task<string> IGoogleCalendarAuthorization.AccessTokenAsync(CancellationToken ct) => AccessTokenAsync(ct);
    void IGoogleCalendarAuthorization.DemandSafeWrite() => DemandSafeWrite();

    public void Dispose() => http.Dispose();
}
