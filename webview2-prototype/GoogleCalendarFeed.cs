using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;

namespace CentralWhatsApp.WebView2;

/// <summary>
/// Assinatura local e SOMENTE LEITURA do endereço secreto iCal do Google.
/// A URL é protegida com DPAPI no perfil do Windows e nunca é enviada ao Supabase.
/// </summary>
internal sealed class GoogleCalendarFeed : IDisposable
{
    private const string LocalTimezone = "America/Sao_Paulo";
    private static readonly DateTimeZone Zone = DateTimeZoneProviders.Tzdb[LocalTimezone];
    private readonly string settingsFile;
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public GoogleCalendarFeed()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CentralWhatsApp", "WebView2Prototype");
        Directory.CreateDirectory(directory);
        settingsFile = Path.Combine(directory, "agenda-google-feed.dpapi");
    }

    public bool HasUrl => File.Exists(settingsFile);

    public void SetSecretUrl(string raw)
    {
        if (!TryGoogleUrl(raw, out var uri))
            throw new ArgumentException("Insira o endereço secreto iCal do Google Agenda (HTTPS).");
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(uri.AbsoluteUri),
            null, DataProtectionScope.CurrentUser);
        var temp = settingsFile + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, settingsFile, true);
    }

    public void RemoveSecretUrl()
    {
        if (File.Exists(settingsFile)) File.Delete(settingsFile);
    }

    private Uri LoadSecretUrl()
    {
        if (!HasUrl)
            throw new InvalidOperationException("Conecte o calendário do Google primeiro.");
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(settingsFile), null,
                DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(bytes);
            if (!TryGoogleUrl(text, out var uri))
                throw new InvalidOperationException("Endereço iCal inválido. Configure-o novamente.");
            return uri;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("A assinatura pertence a outro usuário do Windows. Configure-a novamente.");
        }
    }

    // Não aceitar URLs arbitrárias para evitar que a assinatura seja usada para acessar recursos locais.
    private static bool TryGoogleUrl(string raw, out Uri uri)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri!) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "calendar.google.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !uri.AbsolutePath.StartsWith("/calendar/ical/", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith("/basic.ics", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains("/private-", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        return true;
    }

    public async Task<List<GoogleCalendarOccurrence>> Read(DateOnly first, DateOnly last)
    {
        var url = LoadSecretUrl();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            throw new InvalidOperationException("O Google redirecionou o calendário. Verifique o endereço secreto iCal.");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("O endereço iCal foi revogado ou está incorreto.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 5_000_000)
            throw new InvalidOperationException("A agenda excede o limite de leitura de 5 MB.");
        var ics = await response.Content.ReadAsStringAsync();
        if (ics.Length > 5_000_000 || !ics.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal))
            throw new InvalidOperationException("Não foi possível ler uma agenda iCal válida.");
        return Parse(ics, first, last);
    }

    internal static List<GoogleCalendarOccurrence> Parse(string ics, DateOnly first, DateOnly last)
    {
        var calendar = Calendar.Load(ics) ??
            throw new InvalidOperationException("Calendário Google inválido.");
        var from = new LocalDateTime(first.Year, first.Month, first.Day, 0, 0)
            .InZoneLeniently(Zone).ToInstant();
        var exclusive = last.AddDays(1);
        var to = new LocalDateTime(exclusive.Year, exclusive.Month, exclusive.Day, 0, 0)
            .InZoneLeniently(Zone).ToInstant();
        var results = new List<GoogleCalendarOccurrence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // O Ical.Net expande recorrências, EXDATE e exceções do Google.
        foreach (var occurrence in calendar.GetOccurrences(Zone, from)
                     .TakeWhileBefore(to))
        {
            if (occurrence.Source is not CalendarEvent e) continue;
            var local = occurrence.Start.WithZone(Zone).LocalDateTime.ToDateTimeUnspecified();
            var date = DateOnly.FromDateTime(local);
            if (date < first || date > last) continue;
            var title = string.IsNullOrWhiteSpace(e.Summary) ? "(sem título)" : e.Summary.Trim();
            var key = $"{e.Uid}:{local:O}:{title}";
            if (!seen.Add(key)) continue;
            results.Add(new GoogleCalendarOccurrence(date,
                e.IsAllDay ? "Dia todo" : local.ToString("HH:mm"), title,
                e.Location ?? "", e.Description ?? "", e.Uid ?? ""));
            if (results.Count > 1200)
                throw new InvalidOperationException("Muitos eventos neste período. Escolha um intervalo menor.");
        }
        return results.OrderBy(x => x.Date).ThenBy(x => x.Time).ToList();
    }

    public void Dispose() => client.Dispose();
}

internal sealed record GoogleCalendarOccurrence(
    DateOnly Date, string Time, string Title, string Location, string Description, string Uid);

internal sealed class GoogleCalendarConnectDialog : Form
{
    private readonly TextBox link = new()
    {
        Width = 510, UseSystemPasswordChar = true,
        PlaceholderText = "Cole o Endereço secreto no formato iCal"
    };
    private readonly Label info = new()
    {
        AutoSize = false, Width = 510, Height = 88,
        ForeColor = Color.FromArgb(71, 85, 105),
        Text = "Google Agenda → Configurações → Sua agenda → Integrar agenda → Endereço secreto no formato iCal.\n\nNunca compartilhe o link: ele permite visualizar os seus eventos."
    };
    private readonly GoogleCalendarFeed feed;

    internal GoogleCalendarConnectDialog(GoogleCalendarFeed feed)
    {
        this.feed = feed;
        Text = "Conectar Google Agenda (somente leitura)";
        Width = 600; Height = 335; MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20, 15, 20, 10),
            FlowDirection = FlowDirection.TopDown, WrapContents = false
        };
        body.Controls.Add(new Label
        {
            Text = "Google Agenda • assinatura privada",
            AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Margin = new Padding(4, 4, 4, 16)
        });
        body.Controls.Add(info); body.Controls.Add(link);
        var buttons = new FlowLayoutPanel
        {
            Width = 510, Height = 44, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 10, 0, 0)
        };
        var connect = Btn("Conectar");
        var clear = Btn("Desconectar");
        clear.BackColor = Color.FromArgb(85, 102, 112);
        var cancel = Btn("Cancelar");
        cancel.BackColor = Color.FromArgb(85, 102, 112);
        buttons.Controls.Add(connect); buttons.Controls.Add(cancel); buttons.Controls.Add(clear);
        body.Controls.Add(buttons);
        Controls.Add(body);
        connect.Click += (_, _) =>
        {
            try
            {
                feed.SetSecretUrl(link.Text);
                link.Clear();
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is ArgumentException or IOException or CryptographicException)
            {
                MessageBox.Show(this, error.Message, "Google Agenda", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        clear.Click += (_, _) =>
        {
            feed.RemoveSecretUrl();
            DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }
    private static Button Btn(string value) => new()
    {
        Text = value, Height = 34, AutoSize = true,
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 103, 44),
        ForeColor = Color.White, Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(6, 3, 6, 3)
    };
}
