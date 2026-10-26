using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace CentralWhatsApp.WebView2;

// Styles only native Windows controls. WebView2 documents, profiles and extensions are untouched.
internal static class ModuxAppearance
{
    private sealed class State { public State() { } internal int Version = -1; internal bool Hooked; }
    private static readonly ConditionalWeakTable<Control, State> states = new();
    private static int version;
    private static bool installed;
    internal static bool Dark { get; private set; }
    internal static Color Surface => Dark ? Color.FromArgb(22, 30, 46) : Color.White;
    internal static Color Canvas => Dark ? Color.FromArgb(15, 22, 36) : Color.FromArgb(246, 248, 251);
    internal static Color Field => Dark ? Color.FromArgb(31, 42, 61) : Color.FromArgb(247, 249, 252);
    internal static Color Text => Dark ? Color.FromArgb(233, 238, 247) : Color.FromArgb(25, 39, 59);
    internal static Color Muted => Dark ? Color.FromArgb(161, 177, 198) : Color.FromArgb(85, 103, 125);
    internal static Color Border => Dark ? Color.FromArgb(50, 65, 87) : Color.FromArgb(223, 230, 239);

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
        }
        if (state.Version == version) return;
        state.Version = version;
        if (SystemInformation.HighContrast) return;
        if (control is AgendaPanel) preserveChrome = false;
        if (control is AgendaMonthView month) { month.ApplyAppearance(); return; }

        if (control is Button action)
        {
            var colored = action.BackColor.R < 80 && action.BackColor.G < 150 && action.BackColor.B > 100;
            var green = action.BackColor.G > action.BackColor.R + 30 && action.BackColor.G > action.BackColor.B + 15;
            action.FlatStyle = FlatStyle.Flat;
            action.FlatAppearance.BorderSize = 0;
            action.FlatAppearance.MouseOverBackColor = green ? Color.FromArgb(23, 104, 70) :
                colored ? Color.FromArgb(37, 91, 224) : Field;
            action.BackColor = green ? Color.FromArgb(22, 88, 60) :
                colored ? Color.FromArgb(29, 78, 216) : Field;
            action.ForeColor = green || colored ? Color.White : Text;
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
                    grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                    grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Field;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Field;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                    grid.DefaultCellStyle.BackColor = Surface;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Dark ? Color.FromArgb(39, 65, 102) : Color.FromArgb(226, 237, 255);
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = Dark ? Color.FromArgb(25, 34, 51) : Color.FromArgb(250, 251, 253);
                    break;
                case TextBoxBase text:
                    text.BackColor = Field; text.ForeColor = Text;
                    break;
                case ComboBox combo:
                    combo.BackColor = Field; combo.ForeColor = Text;
                    break;
                case ListBox list:
                    list.BackColor = Surface; list.ForeColor = Text; list.BorderStyle = BorderStyle.None;
                    break;
                case NumericUpDown number:
                    number.BackColor = Field; number.ForeColor = Text;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case GroupBox group:
                    group.ForeColor = Text;
                    break;
                case Label label:
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
                    panel.BackColor = Surface;
                    break;
            }
        }
        foreach (Control child in control.Controls.Cast<Control>().ToArray()) Apply(child, preserveChrome);
        control.Invalidate();
    }

    private static void Round(Button button)
    {
        if (button.Width < 4 || button.Height < 4 || SystemInformation.HighContrast) return;
        var radius = Math.Min(10, Math.Min(button.Width, button.Height) / 3);
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
}
