using System.Globalization;
using System.Drawing.Drawing2D;

namespace CentralWhatsApp.WebView2;

/// <summary>One buffered drawing surface: no per-event native control creation or network access.</summary>
internal sealed class AgendaMonthView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Label caption = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), Margin = new Padding(12, 7, 8, 0) };
    private readonly FlowLayoutPanel header = new() { Dock = DockStyle.Top, Height = 48, WrapContents = false, Padding = new Padding(6, 5, 5, 2) };
    private readonly FlowLayoutPanel legend = new() { Dock = DockStyle.Top, Height = 30, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 2, 5, 0) };
    private readonly CalendarSurface surface;
    private readonly Button previous = Nav("‹"), current = Nav("Hoje"), next = Nav("›");
    private IReadOnlyList<AgendaServiceCategory> legendEntries = [];
    private DateOnly selectedDate = DateOnly.FromDateTime(AgendaCalendarLayout.Now);
    internal AgendaCalendarMode Mode { get; private set; } = AgendaCalendarMode.Month;
    internal AgendaCalendarSpan VisibleRange => AgendaCalendarLayout.Range(selectedDate, Mode);
    internal AgendaCalendarSpan DisplayRange => Mode == AgendaCalendarMode.Month ? AgendaCalendarLayout.MonthEnvelope(selectedDate) : VisibleRange;
    public DateOnly DisplayedMonth => new(selectedDate.Year, selectedDate.Month, 1);
    public DateOnly SelectedDate => selectedDate;
    public event Action<DateOnly>? DateSelected;
    public event Action<DateOnly>? MonthChanged;
    internal event Action<string>? EventActivated;

    internal AgendaMonthView()
    {
        Dock = DockStyle.Top; Height = 430;
        surface = new CalendarSurface(this) { Dock = DockStyle.Fill };
        header.Controls.AddRange([previous, current, next, caption]);
        previous.Click += (_, _) => Navigate(-1); next.Click += (_, _) => Navigate(1);
        current.Click += (_, _) => ChangeDate(DateOnly.FromDateTime(AgendaCalendarLayout.Now));
        Controls.Add(surface); Controls.Add(legend); Controls.Add(header);
        SetLegend(AgendaServiceColors.Legend); ApplyAppearance();
    }
    private static Button Nav(string text) => new() { Text = text, Width = text == "Hoje" ? 60 : 36,
        Height = 31, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 0, 3, 0), Cursor = Cursors.Hand };
    private void Navigate(int direction) => ChangeDate(Mode switch { AgendaCalendarMode.Month => selectedDate.AddMonths(direction),
        AgendaCalendarMode.Week => selectedDate.AddDays(7 * direction), _ => selectedDate.AddDays(direction) });
    private void ChangeDate(DateOnly date)
    {
        selectedDate = date; UpdateCaption(); surface.ResetScroll(); surface.Invalidate(); MonthChanged?.Invoke(date);
    }
    public void SetMonth(DateOnly date) { SetView(date, Mode); }
    public void SelectDate(DateOnly day) { selectedDate = day; UpdateCaption(); surface.Invalidate(); }
    internal void SetView(DateOnly day, AgendaCalendarMode mode)
    {
        var changed = mode != Mode || !VisibleRange.Contains(day);
        selectedDate = day; Mode = mode; UpdateCaption();
        if (changed) surface.ResetScroll();
        surface.Invalidate();
    }
    public void SetEntries(IEnumerable<AgendaCalendarEntry> source)
    {
        surface.Entries = source.ToArray(); surface.Invalidate();
    }
    private void UpdateCaption()
    {
        var span = VisibleRange;
        caption.Text = Mode switch
        {
            AgendaCalendarMode.Day => selectedDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd 'de' MMMM", Br),
            AgendaCalendarMode.Week => $"{span.First:dd/MM} – {span.Last:dd/MM/yyyy}",
            _ => Br.TextInfo.ToTitleCase(selectedDate.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Br))
        };
        surface.AccessibleName = "Agenda " + (Mode == AgendaCalendarMode.Month ? "mensal" : Mode == AgendaCalendarMode.Week ? "semanal" : "do dia") + " • " + caption.Text;
    }
    internal void SetLegend(IReadOnlyList<AgendaServiceCategory> categories)
    {
        if (legendEntries.SequenceEqual(categories)) return;
        legendEntries = categories.ToArray();
        foreach (Control child in legend.Controls.Cast<Control>().ToArray()) child.Dispose();
        foreach (var category in categories.Where(x => new[] { "desinsetizacao", "desratizacao", "orcamento", "confirmacao", "cancelado", "interno" }.Contains(x.Id)))
        {
            var item = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 1, 12, 0), BackColor = ModuxAppearance.Surface };
            item.Controls.Add(new Label { Text = "■", AutoSize = true, ForeColor = category.Color, Tag = "service-color", Margin = Padding.Empty, Font = new Font("Segoe UI", 8) });
            item.Controls.Add(new Label { Text = category.Label, AutoSize = true, ForeColor = ModuxAppearance.Text, Margin = Padding.Empty, Font = new Font("Segoe UI", 8) });
            legend.Controls.Add(item);
        }
    }
    internal void ApplyAppearance()
    {
        BackColor = header.BackColor = legend.BackColor = ModuxAppearance.Surface;
        caption.ForeColor = ModuxAppearance.Text;
        foreach (Control item in legend.Controls)
        {
            item.BackColor = ModuxAppearance.Surface;
            foreach (Control child in item.Controls)
                if (child.Tag as string != "service-color") child.ForeColor = ModuxAppearance.Text;
        }
        foreach (var button in new[] { previous, current, next })
        { button.BackColor = ModuxAppearance.Field; button.ForeColor = ModuxAppearance.Accent; button.FlatAppearance.BorderSize = 0; ModuxAppearance.Round(button); }
        surface.BackColor = ModuxAppearance.Surface; UpdateCaption(); surface.Invalidate();
    }
    private void Choose(DateOnly date, string? key, bool edit)
    {
        var previous = VisibleRange;
        selectedDate = date; UpdateCaption(); surface.Invalidate(); DateSelected?.Invoke(date);
        if (!previous.Contains(date)) MonthChanged?.Invoke(date);
        if (edit && !string.IsNullOrEmpty(key)) EventActivated?.Invoke(key);
    }

    private sealed class CalendarSurface : ScrollableControl
    {
        private readonly AgendaMonthView owner;
        private readonly ToolTip tip = new() { AutoPopDelay = 14000, ShowAlways = true };
        private readonly System.Windows.Forms.Timer clock = new() { Interval = 60000 };
        private readonly List<(Rectangle Bounds, DateOnly Day, AgendaCalendarEntry? Entry)> hits = [];
        private string lastTip = "";
        private readonly Font regular = new("Segoe UI", 8.5f);
        private readonly Font strong = new("Segoe UI", 8.5f, FontStyle.Bold);
        internal AgendaCalendarEntry[] Entries = [];
        private const int Hour = 52, Gutter = 52, Heading = 46, AllDayRows = 3;
        internal CalendarSurface(AgendaMonthView owner)
        {
            this.owner = owner; DoubleBuffered = true; AutoScroll = true; TabStop = true;
            ResizeRedraw = true; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            clock.Tick += (_, _) => { if (Visible) Invalidate(); }; clock.Start();
        }
        internal void ResetScroll()
        {
            AutoScrollPosition = Point.Empty;
            AutoScrollMinSize = owner.Mode == AgendaCalendarMode.Month ? Size.Empty : new Size(0, Heading + 24 * Hour + 92);
            if (owner.Mode != AgendaCalendarMode.Month) AutoScrollPosition = new Point(0, 7 * Hour);
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); hits.Clear();
            if (ClientSize.Width < 80 || ClientSize.Height < 40) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (owner.Mode == AgendaCalendarMode.Month) DrawMonth(e.Graphics); else DrawTimeline(e.Graphics);
        }
        private void DrawText(Graphics g, string text, Rectangle bounds, Color color, bool bold = false, bool wrap = false)
        {
            TextRenderer.DrawText(g, text, bold ? strong : regular, bounds, color, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
                (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) | TextFormatFlags.PreserveGraphicsClipping);
        }
        private static void Fill(Graphics g, Rectangle bounds, Color color)
        { using var brush = new SolidBrush(color); g.FillRectangle(brush, bounds); }
        private void Chip(Graphics g, Rectangle bounds, AgendaCalendarEntry item, DateOnly day, string text, bool wrap = false)
        {
            if (bounds.Height < 3 || bounds.Width < 3) return;
            var past = item.IsPast(AgendaCalendarLayout.Now);
            Fill(g, bounds, AgendaCalendarLayout.Fade(item.Category.Color, ModuxAppearance.Surface, past));
            DrawText(g, text, Rectangle.Inflate(bounds, -4, -2), past ? ModuxAppearance.Muted : item.Category.TextColor, false, wrap);
            hits.Add((bounds, day, item));
        }
        private void DrawMonth(Graphics g)
        {
            var width = ClientSize.Width; var height = ClientSize.Height;
            using var line = new Pen(ModuxAppearance.Border);
            var first = AgendaCalendarLayout.MonthEnvelope(owner.SelectedDate).First;
            for (var i = 0; i < 7; i++)
                DrawText(g, new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" }[i],
                    new Rectangle(i * width / 7 + 8, 5, width / 7, 22), ModuxAppearance.Muted);
            for (var i = 0; i < 42; i++)
            {
                var day = first.AddDays(i); var col = i % 7; var row = i / 7;
                var bounds = Rectangle.FromLTRB(col * width / 7, 28 + row * (height - 28) / 6,
                    (col + 1) * width / 7, 28 + (row + 1) * (height - 28) / 6);
                Fill(g, bounds, day == owner.SelectedDate ? ModuxAppearance.Selection : day.Month == owner.SelectedDate.Month ? ModuxAppearance.Surface : ModuxAppearance.Canvas);
                g.DrawRectangle(line, bounds); hits.Add((bounds, day, null));
                DrawText(g, day.Day.ToString(), new Rectangle(bounds.X + 5, bounds.Y + 3, bounds.Width - 8, 20),
                    day == DateOnly.FromDateTime(AgendaCalendarLayout.Now) ? ModuxAppearance.Accent : ModuxAppearance.Muted, true);
                var list = Entries.Where(x => x.Start < day.AddDays(1).ToDateTime(TimeOnly.MinValue) && x.End > day.ToDateTime(TimeOnly.MinValue)).OrderBy(x => x.Time).ToArray();
                var slots = Math.Max(0, (bounds.Height - 25) / 20);
                var shown = Math.Min(list.Length, slots);
                for (var index = 0; index < shown; index++) Chip(g, new Rectangle(bounds.X + 3, bounds.Y + 24 + index * 20, bounds.Width - 6, 19),
                    list[index], day, $"{list[index].Time} {list[index].Client}");
                if (list.Length > shown) DrawText(g, $"+{list.Length - shown}", new Rectangle(bounds.Right - 39, bounds.Y + 3, 36, 19), ModuxAppearance.Muted);
            }
        }
        private void DrawTimeline(Graphics g)
        {
            var span = owner.VisibleRange; var count = owner.Mode == AgendaCalendarMode.Day ? 1 : 7;
            var column = (ClientSize.Width - Gutter) / (float)count;
            using var line = new Pen(ModuxAppearance.Border);
            var maxAllDay = Enumerable.Range(0, count).Select(i => Entries.Count(x => x.IsAllDay && x.Date <= span.First.AddDays(i) && x.End > span.First.AddDays(i).ToDateTime(TimeOnly.MinValue))).DefaultIfEmpty().Max();
            var allHeight = maxAllDay == 0 ? 0 : Math.Min(AllDayRows, maxAllDay) * 22 + (maxAllDay > AllDayRows ? 20 : 0);
            var gridTop = Heading + allHeight; var scroll = AutoScrollPosition.Y;
            var state = g.Save(); g.SetClip(new Rectangle(0, gridTop, ClientSize.Width, Math.Max(0, ClientSize.Height - gridTop)));
            for (var hour = 0; hour <= 24; hour++)
            {
                var y = gridTop + hour * Hour + scroll;
                g.DrawLine(line, Gutter, y, ClientSize.Width, y);
                if (hour < 24) DrawText(g, $"{hour:00}:00", new Rectangle(2, y + 2, Gutter - 4, 20), ModuxAppearance.Muted);
            }
            for (var dayIndex = 0; dayIndex < count; dayIndex++)
            {
                var day = span.First.AddDays(dayIndex); var x = Gutter + (int)(dayIndex * column);
                g.DrawLine(line, x, gridTop, x, ClientSize.Height);
                foreach (var slot in AgendaCalendarLayout.Slots(Entries, day))
                {
                    var bounds = new Rectangle(x + (int)(column * slot.Lane / slot.Lanes) + 2,
                        gridTop + (int)(slot.StartMinute * Hour / 60) + scroll + 1,
                        Math.Max(4, (int)(column / slot.Lanes) - 4), Math.Max(18, (int)((slot.EndMinute - slot.StartMinute) * Hour / 60) - 2));
                    if (bounds.Bottom < gridTop || bounds.Top > ClientSize.Height) continue;
                    Chip(g, bounds, slot.Entry, day, slot.Entry.Client + "\n" + slot.Entry.Time + " – " + slot.Entry.End.ToString("HH:mm"), true);
                    hits[^1] = (Rectangle.Intersect(bounds, new Rectangle(0, gridTop, ClientSize.Width, Math.Max(0, ClientSize.Height - gridTop))), day, slot.Entry);
                }
            }
            var now = AgendaCalendarLayout.Now; var today = DateOnly.FromDateTime(now);
            if (span.Contains(today))
            {
                var x = Gutter + (int)((today.DayNumber - span.First.DayNumber) * column);
                var y = gridTop + (int)(now.TimeOfDay.TotalMinutes * Hour / 60) + scroll;
                using var red = new Pen(Color.FromArgb(211, 57, 57), 2); g.DrawLine(red, x, y, x + column, y);
            }
            g.Restore(state);
            Fill(g, new Rectangle(0, 0, ClientSize.Width, gridTop), ModuxAppearance.Surface);
            for (var dayIndex = 0; dayIndex < count; dayIndex++)
            {
                var day = span.First.AddDays(dayIndex); var x = Gutter + (int)(dayIndex * column);
                DrawText(g, day.ToDateTime(TimeOnly.MinValue).ToString("ddd dd/MM", Br), new Rectangle(x + 6, 10, (int)column - 10, 25), day == today ? ModuxAppearance.Accent : ModuxAppearance.Text, true);
                hits.Add((new Rectangle(x, 0, (int)column, Heading), day, null));
                var all = Entries.Where(item => item.IsAllDay && item.Date <= day && item.End > day.ToDateTime(TimeOnly.MinValue)).ToArray();
                for (var i = 0; i < Math.Min(AllDayRows, all.Length); i++) Chip(g, new Rectangle(x + 2, Heading + i * 22, (int)column - 4, 20), all[i], day, all[i].Client);
                if (all.Length > AllDayRows) DrawText(g, $"+{all.Length - AllDayRows} dia inteiro", new Rectangle(x + 3, Heading + AllDayRows * 22, (int)column - 6, 20), ModuxAppearance.Muted);
            }
            DrawText(g, "GMT−03", new Rectangle(1, 20, Gutter - 2, 20), ModuxAppearance.Muted);
            g.DrawLine(line, 0, gridTop, ClientSize.Width, gridTop);
        }
        private (Rectangle Bounds, DateOnly Day, AgendaCalendarEntry? Entry)? Hit(Point point)
        {
            for (var i = hits.Count - 1; i >= 0; i--) if (hits[i].Bounds.Contains(point)) return hits[i];
            if (owner.Mode != AgendaCalendarMode.Month && point.X >= Gutter)
            {
                var count = owner.Mode == AgendaCalendarMode.Day ? 1 : 7;
                var index = Math.Clamp((point.X - Gutter) * count / Math.Max(1, ClientSize.Width - Gutter), 0, count - 1);
                return (Rectangle.Empty, owner.VisibleRange.First.AddDays(index), null);
            }
            return null;
        }
        protected override void OnMouseClick(MouseEventArgs e)
        { base.OnMouseClick(e); if (Hit(e.Location) is { } hit) owner.Choose(hit.Day, hit.Entry?.Key, false); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        { base.OnMouseDoubleClick(e); if (Hit(e.Location) is { } hit) owner.Choose(hit.Day, hit.Entry?.Key, true); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); var hit = Hit(e.Location);
            var value = hit?.Entry is { } item ? $"{item.Client}\n{item.Time} • {item.Service}\n{item.Status}" : "";
            if (value != lastTip) { lastTip = value; tip.SetToolTip(this, value); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Left or Keys.Right)
            { owner.Choose(owner.SelectedDate.AddDays(e.KeyCode == Keys.Left ? -1 : 1), null, false); e.Handled = true; }
            else if (e.KeyCode is Keys.PageUp or Keys.PageDown) { owner.Navigate(e.KeyCode == Keys.PageUp ? -1 : 1); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { clock.Dispose(); tip.Dispose(); regular.Dispose(); strong.Dispose(); } base.Dispose(disposing); }
    }
}
