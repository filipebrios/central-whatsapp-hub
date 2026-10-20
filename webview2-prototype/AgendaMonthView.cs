using System.Globalization;

namespace CentralWhatsApp.WebView2;

internal sealed record AgendaCalendarEntry(DateOnly Date, string Time, string Client, string Service, string Status);

/// <summary>Calendário mensal local, alimentado somente com eventos autorizados pelo Supabase.</summary>
internal sealed class AgendaMonthView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private readonly TableLayoutPanel days = new() { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 7, BackColor = Color.FromArgb(230, 235, 231), CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
    private readonly Label caption = new() { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(11, 19, 43), Margin = new Padding(10, 9, 10, 5) };
    private readonly Button previous = Button("‹");
    private readonly Button next = Button("›");
    private readonly Button current = Button("Hoje");
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
        Height = 364;
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
        Controls.Add(days); Controls.Add(header);
        Render();
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

    private void Render()
    {
        caption.Text = Br.TextInfo.ToTitleCase(displayedMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Br));
        days.SuspendLayout();
        try
        {
            days.Controls.Clear();
            var heads = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
            for (var col = 0; col < 7; col++)
            {
                days.Controls.Add(new Label
                {
                    Text = heads[col], Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    BackColor = Color.FromArgb(232, 241, 234),
                    ForeColor = Color.FromArgb(26, 63, 38),
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
                var preview = count == 0 ? "" :
                    "\n" + string.Join("\n", dayEntries!.Take(2)
                        .Select(x => $"{x.Time} {Truncate(x.Client, 13)}"));
                if (count > 2) preview += $"\n+{count - 2} outros";
                var button = new Button
                {
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft,
                    Text = $"{date.Day}{(date == today ? " •" : "")}{preview}",
                    FlatStyle = FlatStyle.Flat,
                    Padding = new Padding(5, 4, 2, 2),
                    Margin = Padding.Empty,
                    Font = new Font("Segoe UI", 8f),
                    UseVisualStyleBackColor = false,
                    BackColor = date == selectedDate
                        ? Color.FromArgb(210, 238, 215)
                        : date.Month == displayedMonth.Month ? Color.White : Color.FromArgb(244, 246, 245),
                    ForeColor = date.Month == displayedMonth.Month ? Color.FromArgb(15, 31, 20) : Color.Gray,
                    AccessibleName = $"{date:dd/MM/yyyy} • {count} agendamentos"
                };
                button.FlatAppearance.BorderSize = date == selectedDate ? 2 : 0;
                button.FlatAppearance.BorderColor = Color.FromArgb(0, 103, 44);
                button.Click += (_, _) =>
                {
                    SelectDate(date);
                    DateSelected?.Invoke(date);
                };
                days.Controls.Add(button, i % 7, 1 + i / 7);
            }
        }
        finally { days.ResumeLayout(); }
    }
    private static string Truncate(string value, int count) =>
        value.Length <= count ? value : value[..(count - 1)] + "…";
}
