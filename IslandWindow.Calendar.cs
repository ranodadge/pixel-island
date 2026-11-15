using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DynamicIsland;

/// <summary>캘린더 화면(달력 + 날짜별 메모)과, 기본 상태·제어 센터에 보여 주는 컴퓨터 상태(CPU·메모리·배터리).</summary>
public partial class IslandWindow
{
    static readonly Brush SundayBrush = Util.Brush("#FF7B7B"), SaturdayBrush = Util.Brush("#7FB2FF"), LowBatteryBrush = Util.Brush("#FF6B6B");

    bool calendarOpen;
    DateTime calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateTime calendarDay = DateTime.Today;
    double calendarHeight = 300, statsTimer = 1;

    // ───────────────────────── 컴퓨터 상태 ─────────────────────────

    void TickStats(double dt)
    {
        statsTimer += dt;
        if (statsTimer < 1) return;
        statsTimer = 0;
        SystemStats.Update();
        UpdateStats();
    }

    void UpdateStats()
    {
        var settings = ThemeStore.Settings;
        bool battery = SystemStats.HasBattery;
        string cpu = $"{SystemStats.Cpu * 100:0}%", ram = $"{SystemStats.Memory * 100:0}%", level = $"{SystemStats.Battery * 100:0}%";
        bool low = battery && !SystemStats.PluggedIn && SystemStats.Battery <= 0.2;

        // 기본(접힌) 상태
        StatsPanel.Visibility = settings.ShowStats ? Visibility.Visible : Visibility.Collapsed;
        BatteryPanel.Visibility = settings.ShowBattery && battery ? Visibility.Visible : Visibility.Collapsed;
        CpuText.Text = cpu;
        RamText.Text = ram;
        if (battery)
        {
            BatteryText.Text = level;
            BatteryGlyph.Text = BatteryIcon(SystemStats.Battery, SystemStats.PluggedIn);
            if (low) BatteryGlyph.Foreground = BatteryText.Foreground = LowBatteryBrush;
            else
            {
                BatteryGlyph.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                BatteryText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
        }

        // 제어 센터
        var track = (Brush)Resources["TrackBrush"];
        var accent = (Brush)Resources["AccentBrush"];
        CtrlCpuText.Text = cpu;
        CtrlRamText.Text = $"{SystemStats.MemoryUsedGb:0.0} / {SystemStats.MemoryTotalGb:0}GB";
        CpuMeter.Value = SystemStats.Cpu;
        RamMeter.Value = SystemStats.Memory;
        foreach (var meter in new[] { CpuMeter, RamMeter, BatteryMeter })
        {
            meter.Track = track;
            meter.Fill = accent;
            meter.InvalidateVisual();
        }
        CtrlBattery.Visibility = battery ? Visibility.Visible : Visibility.Collapsed;
        BatteryGap.Width = new GridLength(battery ? 12 : 0);
        BatteryColumn.Width = battery ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (battery)
        {
            CtrlBatteryLabel.Text = SystemStats.PluggedIn ? "배터리 · 충전" : "배터리";
            CtrlBatteryText.Text = level;
            BatteryMeter.Value = SystemStats.Battery;
            if (low) BatteryMeter.Fill = LowBatteryBrush;
        }
    }

    /// <summary>잔량(10% 단위)과 충전 여부에 맞는 배터리 아이콘.</summary>
    static string BatteryIcon(double level, bool pluggedIn)
    {
        int step = Math.Clamp((int)Math.Round(level * 10), 0, 10);
        if (pluggedIn) return step == 10 ? "" : ((char)(0xE85A + step)).ToString();
        return step == 10 ? "" : ((char)(0xE850 + step)).ToString();
    }

    // ───────────────────────── 캘린더 ─────────────────────────

    void CalendarOpen_Click(object sender, MouseButtonEventArgs e) => OpenCalendar();
    void CalendarButton_Click(object sender, RoutedEventArgs e) => OpenCalendar();
    void CalendarBack_Click(object sender, RoutedEventArgs e) => CloseCalendar();

    void CalendarToday_Click(object sender, RoutedEventArgs e)
    {
        calendarDay = DateTime.Today;
        calendarMonth = new DateTime(calendarDay.Year, calendarDay.Month, 1);
        RenderCalendar();
    }

    void CalendarPrev_Click(object sender, RoutedEventArgs e) => MoveMonth(-1);
    void CalendarNext_Click(object sender, RoutedEventArgs e) => MoveMonth(1);

    void MoveMonth(int delta)
    {
        calendarMonth = calendarMonth.AddMonths(delta);
        RenderCalendar();
    }

    // 달력 위에서 휠을 굴리면 달을 넘긴다 (메모 목록 위에서는 목록이 스크롤된다)
    void Calendar_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!CalendarGrid.IsMouseOver && !CalendarWeekdays.IsMouseOver) return;
        MoveMonth(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    void OpenCalendar()
    {
        CloseList();
        calendarOpen = true;
        showControl = expanded = true;
        calendarDay = DateTime.Today;
        calendarMonth = new DateTime(calendarDay.Year, calendarDay.Month, 1);
        MemoInput.Clear();
        RenderCalendar();
    }

    void CloseCalendar()
    {
        if (!calendarOpen) return;
        calendarOpen = false;
        MemoInput.Clear();
        if (inputMode) SetInputMode(false);
    }

    void UpdateCalendarHeight()
    {
        if (CalendarLayer.Visibility != Visibility.Visible) return;
        double h = CalendarBody.DesiredSize.Height;
        if (h > 0) calendarHeight = Math.Clamp(h, 200, 380);
    }

    void RenderCalendar()
    {
        CalendarTitle.Text = calendarMonth.ToString("yyyy년 M월", Korean);

        if (CalendarWeekdays.Children.Count == 0)
        {
            string names = "일월화수목금토";
            for (int i = 0; i < 7; i++)
            {
                var t = new TextBlock { Text = names[i].ToString(), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
                if (i == 0) t.Foreground = SundayBrush;
                else if (i == 6) t.Foreground = SaturdayBrush;
                else t.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
                CalendarWeekdays.Children.Add(t);
            }
        }

        CalendarGrid.Children.Clear();
        var first = calendarMonth.AddDays(-(int)calendarMonth.DayOfWeek); // 그 달 1일이 들어 있는 주의 일요일부터
        var today = DateTime.Today;
        for (int i = 0; i < 42; i++)
        {
            var date = first.AddDays(i);
            bool inMonth = date.Month == calendarMonth.Month, isToday = date == today, selected = date == calendarDay;
            bool red = date.DayOfWeek == DayOfWeek.Sunday || CalendarStore.Holiday(date) != null;

            var text = new TextBlock
            {
                Text = date.Day.ToString(), FontSize = 12,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            if (isToday) text.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
            else if (red) text.Foreground = SundayBrush;
            else if (date.DayOfWeek == DayOfWeek.Saturday) text.Foreground = SaturdayBrush;
            else text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            // 오늘은 강조색 동그라미, 고른 날짜는 테두리
            var circle = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            if (isToday) circle.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else if (selected) circle.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            if (selected)
            {
                circle.BorderThickness = new Thickness(1.5);
                circle.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            }

            var cell = new Grid { Height = 27, Background = Brushes.Transparent, Cursor = Cursors.Hand, Opacity = inMonth ? 1 : 0.32 };
            cell.Children.Add(circle);
            cell.Children.Add(text);
            if (CalendarStore.HasMemo(date))
            {
                var dot = new Ellipse
                {
                    Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 12, 0),
                };
                dot.SetResourceReference(Shape.FillProperty, isToday ? "TextBrush" : "AccentBrush");
                cell.Children.Add(dot);
            }

            var picked = date;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                calendarDay = picked;
                if (picked.Month != calendarMonth.Month) calendarMonth = new DateTime(picked.Year, picked.Month, 1);
                RenderCalendar();
                e.Handled = true;
            };
            CalendarGrid.Children.Add(cell);
        }
        RenderDay();
    }

    void RenderDay()
    {
        string text = calendarDay.ToString("M월 d일 dddd", Korean);
        if (CalendarStore.Holiday(calendarDay) is { } holiday) text += " · " + holiday;
        if (calendarDay == DateTime.Today) text += " · 오늘";
        CalendarDayText.Text = text;

        MemoItems.Children.Clear();
        var memos = CalendarStore.Memos(calendarDay);
        for (int i = 0; i < memos.Count; i++)
        {
            int index = i;
            var row = new Grid { Height = 26 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bullet = new Ellipse { Width = 5, Height = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            bullet.SetResourceReference(Shape.FillProperty, "AccentBrush");
            row.Children.Add(bullet);

            var memo = new TextBlock
            {
                Text = memos[i], FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = memos[i], Margin = new Thickness(2, 0, 6, 0),
            };
            memo.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(memo, 1);
            row.Children.Add(memo);

            var delete = new Button
            {
                Style = (Style)FindResource("IconBtn"), Content = "", FontSize = 9,
                Width = 22, Height = 22, Margin = new Thickness(0), ToolTip = "메모 지우기",
            };
            System.Windows.Automation.AutomationProperties.SetName(delete, "메모 지우기");
            delete.Click += (_, _) =>
            {
                CalendarStore.Remove(calendarDay, index);
                RenderCalendar();
            };
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            MemoItems.Children.Add(row);
        }
        MemoPlaceholder.Text = memos.Count == 0 ? "이 날짜에 메모 추가" : "메모 더 추가";
    }

    // ───────────────────────── 메모 입력 ─────────────────────────

    void MemoInput_MouseDown(object sender, MouseButtonEventArgs e) => SetInputMode(true, MemoInput);

    void MemoInput_Changed(object sender, TextChangedEventArgs e) =>
        MemoPlaceholder.Visibility = MemoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void MemoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddMemo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            MemoInput.Clear();
            SetInputMode(false);
            e.Handled = true;
        }
    }

    void MemoAdd_Click(object sender, RoutedEventArgs e) => AddMemo();

    void AddMemo()
    {
        string text = MemoInput.Text.Trim();
        if (text.Length == 0)
        {
            SetInputMode(true, MemoInput); // 아직 아무것도 안 썼으면 입력 칸으로 보내 준다
            return;
        }
        CalendarStore.Add(calendarDay, text);
        MemoInput.Clear();
        RenderCalendar();
    }
}
