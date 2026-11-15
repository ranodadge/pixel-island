using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>색을 보여 주는 버튼. 누르면 색상/채도/밝기 슬라이더와 추천 색, HEX 입력이 뜬다.</summary>
public sealed class ColorPickerButton : Button
{
    static readonly string[] Presets =
    {
        "#000000", "#1A1A22", "#2B1B2E", "#0E1621", "#1A1338", "#FFFFFF", "#FFF3C4", "#F4F2FF",
        "#FF5E5E", "#FF9F1C", "#FFD84D", "#6EE7A8", "#5EEAD4", "#6FD3FF", "#6C8CFF", "#B7A6FF",
        "#FF7AA8", "#FF9EC7", "#F4A259", "#8D6E63", "#5CFFB0", "#3A9BD9", "#7C66FF", "#9E9EAE",
    };

    readonly Border swatch = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
    readonly TextBlock label = new() { Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
    Popup? popup;
    Slider? hue, sat, val;
    TextBox? box;
    bool busy;
    string hex = "#FFFFFF";

    public event Action<string>? ColorChanged;

    public string Hex
    {
        get => hex;
        set { hex = Util.ToHex(Util.ParseColor(value, Colors.White)); Refresh(); }
    }

    public ColorPickerButton()
    {
        SetResourceReference(StyleProperty, typeof(Button));
        Padding = new Thickness(6, 4, 8, 4);
        swatch.BorderBrush = Util.Brush(Color.FromArgb(70, 255, 255, 255));
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(swatch);
        sp.Children.Add(label);
        Content = sp;
        Click += (_, _) => Open();
        Refresh();
    }

    void Refresh()
    {
        swatch.Background = Util.Brush(hex);
        label.Text = hex;
    }

    void Open()
    {
        if (popup == null) Build();
        Sync();
        popup!.IsOpen = true;
    }

    void Build()
    {
        var panel = new StackPanel { Width = 216 };

        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var p in Presets)
        {
            var preset = p;
            var b = new Border
            {
                Width = 23, Height = 23, Margin = new Thickness(2), CornerRadius = new CornerRadius(6),
                Background = Util.Brush(p), BorderBrush = Util.Brush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            };
            b.MouseLeftButtonDown += (_, _) => { Set(preset); Sync(); };
            presets.Children.Add(b);
        }
        panel.Children.Add(presets);

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(Util.FromHsv(i * 60, 1, 1), i / 6.0));
        panel.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = rainbow, Margin = new Thickness(5, 2, 5, 0) });

        hue = AddSlider(panel, "색상", 360);
        sat = AddSlider(panel, "채도", 1);
        val = AddSlider(panel, "밝기", 1);

        box = new TextBox { Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily("Consolas") };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromBox(); };
        box.LostFocus += (_, _) => FromBox();
        panel.Children.Add(box);

        var frame = new Border
        {
            Background = Util.Brush("#22222C"), BorderBrush = Util.Brush("#3A3A48"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 0), Child = panel,
        };
        System.Windows.Documents.TextElement.SetForeground(frame,Util.Brush("#EDEDF2"));
        popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = frame };
    }

    Slider AddSlider(Panel panel, string name, double max)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var text = new TextBlock { Text = name, Width = 34, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, FontSize = 12 };
        DockPanel.SetDock(text, Dock.Left);
        var s = new Slider { Minimum = 0, Maximum = max, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) =>
        {
            if (busy) return;
            Set(Util.ToHex(Util.FromHsv(hue!.Value, sat!.Value, val!.Value)));
            if (box != null) box.Text = hex;
        };
        row.Children.Add(text);
        row.Children.Add(s);
        panel.Children.Add(row);
        return s;
    }

    void FromBox()
    {
        if (box == null || busy) return;
        var c = Util.ParseColor(box.Text, Util.ParseColor(hex, Colors.White));
        Set(Util.ToHex(c));
        Sync();
    }

    void Sync()
    {
        if (hue == null) return;
        busy = true;
        var (h, s, v) = Util.ToHsv(Util.ParseColor(hex, Colors.White));
        hue.Value = h;
        sat!.Value = s;
        val!.Value = v;
        box!.Text = hex;
        busy = false;
    }

    void Set(string value)
    {
        if (value == hex) return;
        hex = value;
        Refresh();
        ColorChanged?.Invoke(hex);
    }
}

/// <summary>16x16 도트 그림판. 왼쪽 버튼으로 그리고 오른쪽 버튼으로 지운다.</summary>
public sealed class PixelEditor : FrameworkElement
{
    const int N = Sprites.N;
    const double Cell = 20;
    // 투명 칸은 어두운 외곽선 색과 헷갈리지 않게 조금 밝은 체크무늬로
    static readonly Brush CheckA = Util.Brush("#4B4B5A"), CheckB = Util.Brush("#3F3F4C");
    static readonly Pen GridPen = MakeGridPen();

    readonly char[,] px = new char[N, N];
    Brush[] palette = Array.Empty<Brush>();

    public int Current = 1;
    public string Tool = "Pen";
    public event Action? Edited;

    public PixelEditor()
    {
        Width = Height = N * Cell;
        Cursor = Cursors.Pen;
        SnapsToDevicePixels = true;
        Fill('.');
    }

    static Pen MakeGridPen()
    {
        var p = new Pen(Util.Brush(Color.FromArgb(70, 0, 0, 0)), 1);
        p.Freeze();
        return p;
    }

    void Fill(char c)
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++) px[y, x] = c;
    }

    public void Load(IList<string> rows, IList<string> pal)
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                char c = y < rows.Count && x < rows[y].Length ? rows[y][x] : '.';
                px[y, x] = Sprites.IndexOf(c) >= 0 ? c : '.';
            }
        SetPalette(pal);
    }

    public void SetPalette(IList<string> pal)
    {
        palette = pal.Select(p => (Brush)Util.Brush(p)).ToArray();
        InvalidateVisual();
    }

    public List<string> ToRows()
    {
        var rows = new List<string>(N);
        var buf = new char[N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++) buf[x] = px[y, x];
            rows.Add(new string(buf));
        }
        return rows;
    }

    public void Clear() { Fill('.'); Changed(); }

    public void Mirror()
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N / 2; x++)
                (px[y, x], px[y, N - 1 - x]) = (px[y, N - 1 - x], px[y, x]);
        Changed();
    }

    void Changed()
    {
        InvalidateVisual();
        Edited?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                var r = new Rect(x * Cell, y * Cell, Cell, Cell);
                int idx = Sprites.IndexOf(px[y, x]);
                dc.DrawRectangle(idx >= 0 && idx < palette.Length ? palette[idx] : ((x + y) % 2 == 0 ? CheckA : CheckB), null, r);
            }
        }
        for (int i = 1; i < N; i++)
        {
            dc.DrawLine(GridPen, new Point(i * Cell, 0), new Point(i * Cell, N * Cell));
            dc.DrawLine(GridPen, new Point(0, i * Cell), new Point(N * Cell, i * Cell));
        }
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Paint(e.GetPosition(this), e.ChangedButton == MouseButton.Right, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        if (e.LeftButton == MouseButtonState.Pressed) Paint(e.GetPosition(this), false, false);
        else if (e.RightButton == MouseButtonState.Pressed) Paint(e.GetPosition(this), true, false);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    void Paint(Point p, bool erase, bool first)
    {
        int x = (int)(p.X / Cell), y = (int)(p.Y / Cell);
        if (x < 0 || y < 0 || x >= N || y >= N) return;
        char c = erase || Tool == "Eraser" ? '.' : Sprites.IndexChars[Math.Clamp(Current, 0, 15)];

        if (Tool == "Fill" && !erase)
        {
            if (!first) return;
            char from = px[y, x];
            if (from == c) return;
            var stack = new Stack<(int x, int y)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cy < 0 || cx >= N || cy >= N || px[cy, cx] != from) continue;
                px[cy, cx] = c;
                stack.Push((cx + 1, cy));
                stack.Push((cx - 1, cy));
                stack.Push((cx, cy + 1));
                stack.Push((cx, cy - 1));
            }
            Changed();
            return;
        }

        if (px[y, x] == c) return;
        px[y, x] = c;
        Changed();
    }
}
