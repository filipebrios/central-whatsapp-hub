using System.Globalization;

namespace CentralWhatsApp.WebView2;

internal sealed record AgendaCalendarEntry(DateOnly Date, string Time, string Client, string Service, string Status, AgendaServiceCategory Category);

/// <summary>Calendário mensal local, alimentado somente com eventos autorizados pelo Supabase.</summary>
internal sealed class AgendaMonthView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private readonly TableLayoutPanel days = new() { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 7, BackColor = Color.FromArgb(230, 235, 231), CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
    private readonly Label caption = new() { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(11, 19, 43), Margin = new Padding(10, 9, 10, 5) };
    private readonly Button previous = Button("‹");
    private readonly Button next = Button("›");
    private readonly Button current = Button("Hoje");
    private readonly ToolTip eventTip = new() { ShowAlways = true, AutoPopDelay = 14000 };
    private readonly FlowLayoutPanel legend = new()
    {
        Dock = DockStyle.Top, Height = 32, WrapContents = false,
        AutoScroll = true, BackColor = Color.White,
        Padding = new Padding(8, 4, 5, 0)
    };
    private List<AgendaCalendarEntry> entries = [];
    private DateOnly selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public event Action<DateOnly>? DateSelected;
    public event Action<DateOnly>? MonthChanged;
    public DateOnly DisplayedMonth => displayedMonth;
    public DateOnly SelectedDate => selectedDate;

    internal AgendaMonthView()
    {
        Dock = DockStyle.Top;
        Height = 430;
        BackColor = Color.White;
        days.Padding = new Padding(1);
        for (var i = 0; i < 7; i++)
            days.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));
        days.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        for (var i = 0; i < 6; i++)
            days.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6f));
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 46, WrapContents = false,
            BackColor = Color.White, Padding = new Padding(6, 4, 5, 2)
        };
        header.Controls.Add(previous); header.Controls.Add(current); header.Controls.Add(next); header.Controls.Add(caption);
        previous.Click += (_, _) => SetMonth(displayedMonth.AddMonths(-1));
        next.Click += (_, _) => SetMonth(displayedMonth.AddMonths(1));
        current.Click += (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            selectedDate = today;
            SetMonth(new DateOnly(today.Year, today.Month, 1));
            DateSelected?.Invoke(today);
        };
        SetLegend(AgendaServiceColors.Legend);
        Controls.Add(days); Controls.Add(legend); Controls.Add(header);
        Render();
    }

    private IReadOnlyList<AgendaServiceCategory> legendEntries = AgendaServiceColors.Legend;

    internal void SetLegend(IReadOnlyList<AgendaServiceCategory> categories)
    {
        legendEntries = categories;
        foreach (Control child in legend.Controls.Cast<Control>().ToArray()) child.Dispose();
        legend.Controls.Clear();
        var mainIds = new[] { "desinsetizacao", "desratizacao", "orcamento", "confirmacao", "cancelado", "interno", "outros" };
        foreach (var category in categories.Where(c => mainIds.Contains(c.Id)))
        {
            var label = new Label
            {
                AutoSize = true, Text = "■ " + category.Label, Font = new Font("Segoe UI", 8f),
                ForeColor = category.Id == "outros" ? Color.FromArgb(70, 78, 88) : category.Color,
                Margin = new Padding(2, 1, 10, 1)
            };
            legend.Controls.Add(label);
            eventTip.SetToolTip(label, category.Label + " • " + category.GoogleColorName);
        }
        var more = new Button
        {
            Text = "Ver todas", AutoSize = true, Height = 21, BackColor = Color.White,
            ForeColor = Color.FromArgb(0, 103, 44), FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f), Margin = new Padding(0)
        };
        more.Click += (_, _) => ShowLegend();
        legend.Controls.Add(more);
    }

    private static Button Button(string text) => new()
    {
        Text = text, Width = text == "Hoje" ? 56 : 34, Height = 30,
        FlatStyle = FlatStyle.Flat, BackColor = Color.White,
        ForeColor = Color.FromArgb(0, 103, 44),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Margin = new Padding(3, 2, 3, 2)
    };

    public void SetMonth(DateOnly firstDay)
    {
        var target = new DateOnly(firstDay.Year, firstDay.Month, 1);
        if (displayedMonth != target)
        {
            displayedMonth = target;
            selectedDate = target;
            Render();
            MonthChanged?.Invoke(target);
        }
        else Render();
    }

    public void SelectDate(DateOnly day)
    {
        selectedDate = day;
        if (day.Year != displayedMonth.Year || day.Month != displayedMonth.Month)
            SetMonth(day);
        Render();
    }

    public void SetEntries(IEnumerable<AgendaCalendarEntry> source)
    {
        entries = source.ToList();
        Render();
    }

    internal void ApplyAppearance()
    {
        BackColor = ModuxAppearance.Surface;
        days.BackColor = ModuxAppearance.Border;
        legend.BackColor = ModuxAppearance.Surface;
        caption.ForeColor = ModuxAppearance.Text;
        foreach (Control control in Controls)
            if (control is FlowLayoutPanel panel) panel.BackColor = ModuxAppearance.Surface;
        foreach (var button in new[] { previous, next, current })
        {
            button.BackColor = ModuxAppearance.Field;
            button.ForeColor = ModuxAppearance.Dark ? Color.FromArgb(150, 214, 178) : Color.FromArgb(22, 88, 60);
            button.FlatAppearance.BorderSize = 0;
        }
        Render();
    }

    private void Render()
    {
        caption.Text = Br.TextInfo.ToTitleCase(displayedMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Br));
        days.SuspendLayout();
        try
        {
            foreach (Control old in days.Controls.Cast<Control>().ToArray()) old.Dispose();
            days.Controls.Clear();
            var heads = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
            for (var col = 0; col < 7; col++)
            {
                days.Controls.Add(new Label
                {
                    Text = heads[col], Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    BackColor = ModuxAppearance.Field,
                    ForeColor = ModuxAppearance.Muted,
                    Margin = Padding.Empty
                }, col, 0);
            }
            var offset = ((int)displayedMonth.DayOfWeek + 6) % 7;
            var firstVisible = displayedMonth.AddDays(-offset);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var byDay = entries.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.ToList());
            for (var i = 0; i < 42; i++)
            {
                var date = firstVisible.AddDays(i);
                byDay.TryGetValue(date, out var dayEntries);
                var count = dayEntries?.Count ?? 0;
                var dateCell = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                    Margin = Padding.Empty, Padding = new Padding(2),
                    BackColor = date == selectedDate
                        ? (ModuxAppearance.Dark ? Color.FromArgb(33, 67, 54) : Color.FromArgb(225, 242, 231))
                        : date.Month == displayedMonth.Month
                            ? ModuxAppearance.Surface : ModuxAppearance.Canvas,
                    Cursor = Cursors.Hand,
                    AccessibleName = $"{date:dd/MM/yyyy} • {count} agendamentos"
                };
                dateCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                dateCell.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
                dateCell.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
                dateCell.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
                dateCell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                var title = new Label
                {
                    Dock = DockStyle.Fill, Margin = Padding.Empty,
                    Font = new Font("Segoe UI", 8.5f,
                        date == today ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = date == today ? (ModuxAppearance.Dark ? Color.FromArgb(150, 214, 178) : Color.FromArgb(22, 88, 60))
                        : date.Month == displayedMonth.Month ? ModuxAppearance.Text : ModuxAppearance.Muted,
                    Text = $"{date.Day}{(date == today ? " •" : "")}",
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                dateCell.Controls.Add(title, 0, 0);

                // Cada compromisso recebe a sua própria faixa de cor.
                // Uma célula inteira colorida não representaria serviços diferentes no mesmo dia.
                if (dayEntries is { Count: > 0 })
                {
                    for (var entryIndex = 0; entryIndex < Math.Min(2, count); entryIndex++)
                    {
                        var entry = dayEntries[entryIndex];
                        var chip = new Label
                        {
                            Dock = DockStyle.Fill, Height = 17, Margin = new Padding(0, 1, 1, 0),
                            Text = $"{entry.Time}  {entry.Client}",
                            Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                            BackColor = entry.Category.Color,
                            ForeColor = entry.Category.TextColor,
                            AutoEllipsis = true,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Padding = new Padding(3, 0, 2, 0),
                            Cursor = Cursors.Hand
                        };
                        eventTip.SetToolTip(chip,
                            $"{entry.Time} • {entry.Client}\n{entry.Category.Label} | {entry.Service}\n{entry.Status}");
                        chip.Click += (_, _) => SelectAndNotify(date);
                        dateCell.Controls.Add(chip, 0, entryIndex + 1);
                    }
                    if (count > 2)
                    {
                        var extra = new Label
                        {
                            Dock = DockStyle.Fill, Text = $"+{count - 2} outros",
                            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                            ForeColor = ModuxAppearance.Muted, Margin = Padding.Empty,
                            Cursor = Cursors.Hand
                        };
                        eventTip.SetToolTip(extra, string.Join("\n",
                            dayEntries.Skip(2).Take(12).Select(entry =>
                                $"{entry.Time} {entry.Client} • {entry.Category.Label}")));
                        extra.Click += (_, _) => SelectAndNotify(date);
                        dateCell.Controls.Add(extra, 0, 3);
                    }
                }
                title.Click += (_, _) => SelectAndNotify(date);
                dateCell.Click += (_, _) => SelectAndNotify(date);
                days.Controls.Add(dateCell, i % 7, 1 + i / 7);
            }
        }
        finally { days.ResumeLayout(); }
    }
    private void SelectAndNotify(DateOnly date)
    {
        SelectDate(date);
        DateSelected?.Invoke(date);
    }

    private void ShowLegend()
    {
        using var form = new Form
        {
            Text = "Cores dos serviços • São Bento",
            Width = 410, Height = 515,
            StartPosition = FormStartPosition.CenterParent,
            Font = new Font("Segoe UI", 10), BackColor = Color.White
        };
        var container = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(18)
        };
        foreach (var category in legendEntries)
        {
            var item = new Label
            {
                AutoSize = false, Width = 350, Height = 29,
                BackColor = category.Color, ForeColor = category.TextColor,
                Padding = new Padding(9, 4, 3, 2),
                Text = category.Label + "  (" + category.GoogleColorName + ")",
                Margin = new Padding(3, 2, 3, 4)
            };
            container.Controls.Add(item);
        }
        container.Controls.Add(new Label
        {
            Text = "Classificação pelo serviço do evento. As cores originais do Google Agenda não são alteradas.",
            Width = 350, Height = 58, ForeColor = Color.FromArgb(60, 78, 80),
            Margin = new Padding(2, 13, 3, 3)
        });
        form.Controls.Add(container);
        form.ShowDialog(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) eventTip.Dispose();
        base.Dispose(disposing);
    }
}
