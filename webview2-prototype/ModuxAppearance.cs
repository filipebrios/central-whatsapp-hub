using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace CentralWhatsApp.WebView2;

// Styles only native Windows controls. WebView2 documents, profiles and extensions are untouched.
internal static class ModuxAppearance
{
    private sealed class State { public State() { } internal int Version = -1; internal bool Hooked; internal string? Role; }
    private static readonly ConditionalWeakTable<Control, State> states = new();
    private static int version;
    private static bool installed;
    internal static bool Dark { get; private set; }
    // ARANY reference: graphite header, warm neutral surfaces, restrained lavender accent.
    internal static Color Header => Color.FromArgb(32, 33, 37);
    internal static Color HeaderField => Color.FromArgb(51, 52, 59);
    internal static Color Surface => Dark ? Color.FromArgb(32, 33, 37) : Color.White;
    internal static Color Canvas => Dark ? Color.FromArgb(23, 24, 28) : Color.FromArgb(250, 250, 250);
    internal static Color Field => Dark ? Color.FromArgb(45, 46, 53) : Color.FromArgb(240, 240, 243);
    internal static Color Text => Dark ? Color.FromArgb(245, 245, 247) : Color.FromArgb(32, 33, 37);
    internal static Color Muted => Dark ? Color.FromArgb(179, 184, 202) : Color.FromArgb(113, 116, 123);
    internal static Color Border => Dark ? Color.FromArgb(62, 63, 71) : Color.FromArgb(222, 222, 226);
    internal static Color Accent => Dark ? Color.FromArgb(198, 206, 233) : Color.FromArgb(100, 117, 186);
    internal static Color Selection => Dark ? Color.FromArgb(60, 64, 85) : Color.FromArgb(235, 236, 245);

    internal static void Role(Button button, string role)
    {
        var state = states.GetOrCreateValue(button);
        state.Role = role; state.Version = -1;
    }

    internal static void Configure(bool dark)
    {
        if (!installed)
        {
            Application.Idle += (_, _) =>
            {
                foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                    if (!form.IsDisposed) Apply(form, form is MainForm);
            };
            installed = true;
        }
        if (Dark != dark) { Dark = dark; version++; }
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            if (!form.IsDisposed) Apply(form, form is MainForm);
    }

    private static void Apply(Control control, bool preserveChrome)
    {
        if (control.IsDisposed || control is Microsoft.Web.WebView2.WinForms.WebView2) return;
        var state = states.GetOrCreateValue(control);
        if (!state.Hooked)
        {
            state.Hooked = true;
            control.ControlAdded += (_, e) => { if (e.Control is { } child) Apply(child, preserveChrome && control is not AgendaPanel); };
            if (control is Button button)
                button.SizeChanged += (_, _) => Round(button);
            if (control.Tag as string == "card")
                control.SizeChanged += (_, _) => RoundCard(control);
        }
        if (state.Version == version) return;
        state.Version = version;
        if (SystemInformation.HighContrast) return;
        if (control is AgendaPanel || control.Tag as string == "modux-content") preserveChrome = false;
        if (control is AgendaMonthView month) { month.ApplyAppearance(); return; }

        if (control is Button action)
        {
            state.Role ??= action.BackColor.G > action.BackColor.R + 30 ||
                action.BackColor.B > action.BackColor.R + 50 ? "primary" :
                action.BackColor.R > action.BackColor.G + 65 ? "danger" : "secondary";
            var primary = state.Role == "primary";
            var chrome = state.Role == "chrome";
            var account = state.Role == "account";
            action.FlatStyle = FlatStyle.Flat;
            action.FlatAppearance.BorderSize = state.Role is "secondary" or "sidebar" ? 1 : 0;
            action.FlatAppearance.BorderColor = Border;
            action.FlatAppearance.MouseOverBackColor = chrome ? Color.FromArgb(70, 72, 83) : Selection;
            if (!account)
            {
                action.BackColor = chrome ? HeaderField : state.Role == "danger" ? Color.FromArgb(167, 51, 61) :
                    primary ? (Dark ? Color.FromArgb(241, 241, 245) : Header) : Field;
                action.ForeColor = chrome || state.Role == "danger" ? Color.White : primary && Dark ? Header : primary ? Color.White : Text;
                action.FlatAppearance.MouseOverBackColor = chrome ? Color.FromArgb(70, 72, 83) :
                    primary ? (Dark ? Color.FromArgb(220, 224, 238) : Color.FromArgb(64, 66, 80)) : Selection;
            }
            action.Cursor = Cursors.Hand;
            action.UseVisualStyleBackColor = false;
            Round(action);
        }
        else if (!preserveChrome)
        {
            switch (control)
            {
                case Form form:
                    form.BackColor = Canvas; form.ForeColor = Text;
                    break;
                case DataGridView grid:
                    grid.EnableHeadersVisualStyles = false;
                    grid.BackgroundColor = Surface; grid.GridColor = Border;
                    grid.BorderStyle = BorderStyle.None;
                    grid.ColumnHeadersHeight = 40; grid.RowTemplate.Height = 38;
                    grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
                    grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                    grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Field;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Field;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                    grid.DefaultCellStyle.BackColor = Surface;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Selection;
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = Canvas;
                    break;
                case TextBoxBase text:
                    text.BackColor = Field; text.ForeColor = Text;
                    break;
                case ComboBox combo:
                    combo.BackColor = Field; combo.ForeColor = Text; combo.FlatStyle = FlatStyle.Flat;
                    break;
                case ListBox list:
                    list.BackColor = Surface; list.ForeColor = Text; list.BorderStyle = BorderStyle.None;
                    break;
                case NumericUpDown number:
                    number.BackColor = Field; number.ForeColor = Text;
                    break;
                case DateTimePicker date:
                    if (date.Format == DateTimePickerFormat.Short)
                    { date.Format = DateTimePickerFormat.Custom; date.CustomFormat = "dd/MM/yyyy"; }
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case GroupBox group:
                    group.ForeColor = Text;
                    break;
                case LinkLabel link:
                    link.LinkColor = Accent; link.ActiveLinkColor = Accent;
                    break;
                case Label label:
                    if (label.Tag as string is "service-color" or "chrome-text") break;
                    if (label.Tag as string == "muted") { label.ForeColor = Muted; break; }
                    // Keep service swatches and deliberate warning/success colors.
                    if (label.BackColor.R > 210 && label.BackColor.G > 210 && label.BackColor.B > 210)
                        label.BackColor = Color.Transparent;
                    if (label.ForeColor.R == label.ForeColor.G && label.ForeColor.G == label.ForeColor.B ||
                        Math.Abs(label.ForeColor.R - label.ForeColor.G) < 45 && Math.Abs(label.ForeColor.G - label.ForeColor.B) < 45)
                        label.ForeColor = Text;
                    break;
                case UserControl module:
                    module.BackColor = Surface; module.ForeColor = Text;
                    break;
                case Panel panel:
                    panel.BackColor = panel.Tag as string is "canvas" or "modux-content" ? Canvas : Surface;
                    if (panel.Tag as string == "card") RoundCard(panel);
                    break;
            }
        }
        foreach (Control child in control.Controls.Cast<Control>().ToArray()) Apply(child, preserveChrome);
        control.Invalidate();
    }

    private static void Round(Button button)
    {
        if (button.Width < 4 || button.Height < 4 || SystemInformation.HighContrast) return;
        var radius = Math.Min(18, Math.Min(button.Width, button.Height) / 2);
        var bounds = new Rectangle(0, 0, button.Width, button.Height);
        using var shape = new GraphicsPath();
        var diameter = radius * 2;
        shape.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        var old = button.Region;
        button.Region = new Region(shape);
        old?.Dispose();
    }

    private static void RoundCard(Control card)
    {
        if (card.Width < 48 || card.Height < 48 || SystemInformation.HighContrast) return;
        const int diameter = 40;
        using var shape = new GraphicsPath();
        shape.AddArc(0, 0, diameter, diameter, 180, 90);
        shape.AddArc(card.Width - diameter, 0, diameter, diameter, 270, 90);
        shape.AddArc(card.Width - diameter, card.Height - diameter, diameter, diameter, 0, 90);
        shape.AddArc(0, card.Height - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        var old = card.Region; card.Region = new Region(shape); old?.Dispose();
    }
}
