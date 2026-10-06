using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace CentralWhatsApp.WebView2;

internal static class Program
{
    private const string MutexName = "MODUX.SingleInstance";
    private const string PipeName = "MODUX.Compose.v1";
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main(string[] args)
    {
        var link = args.FirstOrDefault(a => a.StartsWith("modux:", StringComparison.OrdinalIgnoreCase));
        var request = ComposeRequest.Parse(link);
        if (link is not null && request is null)
        {
            MessageBox.Show("O link da mensagem é inválido ou grande demais.", "MODUX");
            return;
        }
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (link is not null)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    pipe.Connect(5000);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
                    writer.WriteLine(link);
                }
                catch
                {
                    MessageBox.Show("Não foi possível abrir a mensagem no MODUX ativo. Atualize para a versão 1.6.0 ou reabra o aplicativo e tente novamente.", "MODUX");
                }
            }
            ActivateExistingInstance();
            return;
        }
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        using var cancellation = new CancellationTokenSource();
        if (request is not null) form.QueueCompose(request);
        form.Shown += (_, _) => { _ = Listen(form, cancellation.Token); };
        form.FormClosed += (_, _) => cancellation.Cancel();
        Application.Run(form);
        GC.KeepAlive(mutex);
    }

    private static async Task Listen(MainForm form, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var line = new StringBuilder();
                var character = new char[1];
                while (line.Length <= ComposeRequest.MaxUriLength)
                {
                    var count = await reader.ReadAsync(character.AsMemory(), deadline.Token).ConfigureAwait(false);
                    if (count == 0 || character[0] == '\n') break;
                    if (character[0] != '\r') line.Append(character[0]);
                }
                var request = ComposeRequest.Parse(line.ToString());
                if (request is not null && !form.IsDisposed)
                    form.BeginInvoke(new Action(() => form.QueueCompose(request)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception)
            {
                if (token.IsCancellationRequested) break;
                try { await Task.Delay(250, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static void ActivateExistingInstance()
    {
        var current = Process.GetCurrentProcess();
        var existing = Process.GetProcessesByName(current.ProcessName)
            .FirstOrDefault(process => process.Id != current.Id);
        if (existing?.MainWindowHandle is not { } handle || handle == IntPtr.Zero) return;
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }
}
