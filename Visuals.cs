using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>살짝 튕기는 스프링 — 섬의 크기 변화에 쓴다.</summary>
public sealed class Spring
{
    public double Value, Target, Velocity;
    public double Stiffness = 260, Damping = 23;

    public void Snap(double v) { Value = Target = v; Velocity = 0; }

    public void Step(double dt)
    {
        const int sub = 2;
        dt /= sub;
        for (int i = 0; i < sub; i++)
        {
            Velocity += (-Stiffness * (Value - Target) - Damping * Velocity) * dt;
            Value += Velocity * dt;
        }
    }
}

/// <summary>음악 비주얼라이저 (막대 / 물결 / 점).</summary>
public sealed class VisualizerView : FrameworkElement
{
    static readonly int[] CenterOut = { 5, 2, 0, 1, 3 };

    public float[] Levels = new float[8];
    public Brush Fill = Brushes.White;
    public string Mode = "Bars";

    public VisualizerView() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const int n = 5;
        double slot = w / n;

        if (Mode == "Wave")
        {
            double t = Environment.TickCount64 / 1000.0;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                const int steps = 28;
                for (int i = 0; i <= steps; i++)
                {
                    double u = i / (double)steps;
                    double f = u * (n - 1);
                    int a = (int)Math.Floor(f), b = Math.Min(n - 1, a + 1);
                    double lv = Levels[CenterOut[a]] + (Levels[CenterOut[b]] - Levels[CenterOut[a]]) * (f - a);
                    double edge = Math.Sin(u * Math.PI); // 양 끝은 잦아들게
                    double y = h / 2 + Math.Sin(u * 9 + t * 7) * (0.08 + lv * 0.92) * edge * (h / 2 - 1);
                    var p = new Point(u * w, y);
                    if (i == 0) g.BeginFigure(p, false, false);
                    else g.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            var pen = new Pen(Fill, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
            return;
        }

        for (int i = 0; i < n; i++)
        {
            double lv = Math.Clamp(Levels[CenterOut[i]], 0, 1);
            double cx = (i + 0.5) * slot;
            if (Mode == "Dots")
            {
                double r = Math.Min(slot * 0.34, 1.4 + lv * 2.2);
                double y = h / 2 - (lv - 0.25) * (h * 0.55);
                dc.DrawEllipse(Fill, null, new Point(cx, Math.Clamp(y, r, h - r)), r, r);
            }
            else
            {
                double bw = slot * 0.56;
                double bh = Math.Max(bw, lv * h);
                dc.DrawRoundedRectangle(Fill, null, new Rect(cx - bw / 2, (h - bh) / 2, bw, bh), bw / 2, bw / 2);
            }
        }
    }
}

/// <summary>제어 센터의 얇은 게이지 (CPU, 메모리, 배터리). 보기만 하는 용도.</summary>
public sealed class MiniMeter : FrameworkElement
{
    double value;

    public Brush Track = Brushes.Gray, Fill = Brushes.White;

    public MiniMeter() => IsHitTestVisible = false;

    public double Value
    {
        get => value;
        set
        {
            double v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - this.value) < 0.002) return;
            this.value = v;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
        if (value > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(h, w * value), h), r, r);
    }
}

/// <summary>제어 센터의 두툼한 슬라이더 (볼륨, 밝기). 누른 채 끌거나 휠을 굴려 조절한다.</summary>
public sealed class IslandSlider : FrameworkElement
{
    double value;
    bool dragging;

    public Brush Track = Brushes.Gray, Fill = Brushes.White;
    public event Action<double>? ValueChanged;

    public IslandSlider() => Cursor = System.Windows.Input.Cursors.Hand;

    /// <summary>0~1. 사용자가 끌고 있는 동안에는 바깥에서 넣는 값을 무시한다.</summary>
    public double Value
    {
        get => value;
        set
        {
            if (dragging) return;
            double v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - this.value) < 0.0005) return;
            this.value = v;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var rect = new Rect(0, 0, w, h);
        double r = h / 2;
        dc.DrawRoundedRectangle(Track, null, rect, r, r);
        var clip = new RectangleGeometry(rect, r, r);
        clip.Freeze();
        dc.PushClip(clip);
        dc.DrawRectangle(Fill, null, new Rect(0, 0, w * value, h));
        dc.Pop();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);

    void Set(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - value) < 0.0005) return;
        value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(value);
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        dragging = true;
        CaptureMouse();
        Set(e.GetPosition(this).X / Math.Max(1, ActualWidth));
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (dragging) Set(e.GetPosition(this).X / Math.Max(1, ActualWidth));
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e) => dragging = false;

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        Set(value + e.Delta / 120.0 * 0.02);
        e.Handled = true;
    }
}

/// <summary>섬 안에 흩날리는 장식 (별, 눈, 꽃잎, 하트, 방울).</summary>
public sealed class ParticleLayer : FrameworkElement
{
    struct P { public double U, V, Size, Phase, Speed; }

    static readonly Geometry Heart = Geometry.Parse("M 0,-0.35 C 0.5,-1.1 1.3,-0.2 0,0.9 C -1.3,-0.2 -0.5,-1.1 0,-0.35 Z");
    static readonly Geometry Sparkle = Geometry.Parse("M 0,-1 L 0.25,-0.25 L 1,0 L 0.25,0.25 L 0,1 L -0.25,0.25 L -1,0 L -0.25,-0.25 Z");

    readonly Random rng = new();
    readonly Brush[] shades = new Brush[16]; // 투명도 단계별 브러시 (매 프레임 새로 만들지 않게)
    P[] items = Array.Empty<P>();
    string kind = "None";
    Pen outline = new(Brushes.White, 1);
    double time;

    public ParticleLayer() => IsHitTestVisible = false;

    Brush Shade(double alpha) => shades[Math.Clamp((int)(alpha * 15 + 0.5), 0, 15)];

    public void Configure(string newKind, Color newColor)
    {
        for (int i = 0; i < shades.Length; i++) shades[i] = Util.Brush(Util.WithAlpha(newColor, i / 15.0));
        outline = new Pen(Shade(0.6), 1);
        outline.Freeze();
        if (newKind == kind) { InvalidateVisual(); return; }
        kind = newKind;
        int count = kind switch { "Stars" => 14, "Snow" => 14, "Petals" => 9, "Hearts" => 7, "Bubbles" => 9, _ => 0 };
        items = new P[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = new P
            {
                U = rng.NextDouble(), V = rng.NextDouble(),
                Size = 0.6 + rng.NextDouble() * 0.8,
                Phase = rng.NextDouble() * Math.PI * 2,
                Speed = 0.6 + rng.NextDouble() * 0.8,
            };
        }
        InvalidateVisual();
    }

    public void Tick(double dt)
    {
        if (items.Length == 0) return;
        time += dt;
        for (int i = 0; i < items.Length; i++)
        {
            ref var p = ref items[i];
            switch (kind)
            {
                case "Snow":
                    p.V += p.Speed * dt * 0.22;
                    p.U += Math.Sin(time * 1.3 + p.Phase) * 0.02 * dt;
                    break;
                case "Petals":
                    p.V += p.Speed * dt * 0.18;
                    p.U += (0.03 + Math.Sin(time + p.Phase) * 0.03) * dt;
                    break;
                case "Hearts":
                case "Bubbles":
                    p.V -= p.Speed * dt * 0.16;
                    p.U += Math.Sin(time * 1.6 + p.Phase) * 0.02 * dt;
                    break;
            }
            if (p.V > 1.15) { p.V = -0.15; p.U = rng.NextDouble(); }
            if (p.V < -0.15) { p.V = 1.15; p.U = rng.NextDouble(); }
            if (p.U > 1.05) p.U = -0.05;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (items.Length == 0 || w <= 0 || h <= 0) return;
        foreach (var p in items)
        {
            double x = p.U * w, y = p.V * h;
            switch (kind)
            {
                case "Stars":
                {
                    double tw = 0.25 + 0.75 * Math.Abs(Math.Sin(time * p.Speed * 1.4 + p.Phase));
                    DrawShape(dc, Sparkle, x, y, 2.6 * p.Size, 2.6 * p.Size, 0, Shade(tw));
                    break;
                }
                case "Snow":
                    dc.DrawEllipse(Shade(0.75), null, new Point(x, y), 1.5 * p.Size, 1.5 * p.Size);
                    break;
                case "Petals":
                    DrawShape(dc, Dot, x, y, 3 * p.Size, 1.6 * p.Size, time * 70 * p.Speed + p.Phase * 57, Shade(0.8));
                    break;
                case "Hearts":
                    DrawShape(dc, Heart, x, y, 3.2 * p.Size, 3.2 * p.Size, Math.Sin(time * 2 + p.Phase) * 12, Shade(0.7));
                    break;
                case "Bubbles":
                    dc.DrawEllipse(null, outline, new Point(x, y), 2.6 * p.Size, 2.6 * p.Size);
                    break;
            }
        }
    }

    static readonly Geometry Dot = MakeDot();

    static Geometry MakeDot()
    {
        var g = new EllipseGeometry(new Point(0, 0), 1, 1);
        g.Freeze();
        return g;
    }

    static void DrawShape(DrawingContext dc, Geometry geo, double x, double y, double sx, double sy, double angle, Brush brush)
    {
        var m = Matrix.Identity;
        m.Scale(sx, sy);
        m.Rotate(angle);
        m.Translate(x, y);
        var tf = new MatrixTransform(m);
        tf.Freeze();
        dc.PushTransform(tf);
        dc.DrawGeometry(brush, null, geo);
        dc.Pop();
    }
}
