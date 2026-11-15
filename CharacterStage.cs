using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// 캐릭터들이 섬 위(또는 안)에서 노는 무대. 걷고, 뛰고, 공을 차고, 서로 쫓아다니고,
/// 음악이 나오면 박자에 맞춰 춤추고, 조용하면 잠든다.
/// </summary>
public sealed class CharacterStage : FrameworkElement
{
    enum St { Idle, Walk, Sleep, Dance, ChaseBall, ChaseFriend }

    sealed class Actor
    {
        public CharacterDef Def = new();
        public SpriteSet Sprite = new();
        public double X = double.NaN, Y, VY;
        public int Facing = 1;
        public St State = St.Idle;
        public double Timer, TargetX, Anim, Squash, SquashV, Phase;
        public string? Emote;
        public double EmoteT;
        public Actor? Friend;
    }

    sealed class BallState { public double X = double.NaN, Y, VX, VY, Rot; }

    const double BallR = 4.5;
    static readonly Typeface EmoteFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Pen EmoteOutline = MakePen(Color.FromArgb(200, 20, 18, 28), 2.5);
    static readonly Pen BallOutline = MakePen(Color.FromRgb(34, 32, 42), 1.2);

    readonly List<Actor> actors = new();
    readonly Random rng = new();
    BallState? ball;
    Brush ballBrush = Brushes.HotPink;
    double opacity = 1, time, quiet, bassAvg, sinceBeat = 9;
    int beatCount;

    // 매 프레임 아일랜드 창이 채워 주는 값들
    public Rect PillRect;
    public double PillRadius, InsetL, InsetR;
    public bool Inside, ActorsVisible = true, MusicPlaying;
    public float[] Levels = new float[8];
    public Brush EmoteBrush = Brushes.White;

    /// <summary>"섬 위" 모드에서 캐릭터가 서 있으려면 섬 위로 필요한 높이.</summary>
    public double Headroom { get; private set; }

    public CharacterStage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Cursor = Cursors.Hand;
    }

    static Pen MakePen(Color c, double t)
    {
        var p = new Pen(new SolidColorBrush(c), t) { LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    public void ApplyTheme(Theme t)
    {
        while (actors.Count > t.Characters.Count) actors.RemoveAt(actors.Count - 1);
        Headroom = 0;
        for (int i = 0; i < t.Characters.Count; i++)
        {
            if (i >= actors.Count) actors.Add(new Actor { Phase = rng.NextDouble() * 6, Facing = rng.Next(2) * 2 - 1 });
            var a = actors[i];
            a.Def = t.Characters[i];
            a.Sprite = Sprites.Build(a.Def);
            double visible = (a.Sprite.H - a.Sprite.TopPad - a.Sprite.BottomPad) * a.Def.PixelSize + (a.Def.Floats ? 9 : 0);
            Headroom = Math.Max(Headroom, visible);
        }
        foreach (var a in actors)
            if (a.Friend != null && !actors.Contains(a.Friend)) a.Friend = null;

        if (t.Ball) ball ??= new BallState();
        else ball = null;
        ballBrush = Util.Brush(t.BallColor);
        InvalidateVisual();
    }

    /// <summary>바깥 사건에 반응: "notify"(알림 도착), "music"(음악 시작).</summary>
    public void React(string what)
    {
        quiet = 0;
        foreach (var a in actors)
        {
            if (a.State == St.Sleep) { a.State = St.Idle; a.Timer = 0.6; }
            if (what == "notify") { Jump(a, 210); Say(a, "!"); }
            else if (what == "hello") { Jump(a, 220); Say(a, "♥"); }
            else if (what == "music") { Say(a, "♪"); a.State = St.Dance; a.Timer = 5 + rng.NextDouble() * 4; }
        }
    }

    double Scale(Actor a) => Inside ? Math.Min(a.Def.PixelSize, 1.5) : a.Def.PixelSize;
    double Ground => Inside ? PillRect.Bottom - 3 : PillRect.Top + 1;
    double Hover(Actor a) => a.Def.Floats ? (Inside ? 2.5 + Math.Sin(time * 2.4 + a.Phase) * 1.5 : 5 + Math.Sin(time * 2.4 + a.Phase) * 3) : 0;

    void Say(Actor a, string emote) { a.Emote = emote; a.EmoteT = 1.4; }

    void Jump(Actor a, double v)
    {
        if (a.Y > 0.5) return;
        if (Inside) v *= 0.55;
        a.VY = v;
        a.Y = 0.5;
        a.SquashV = 4;
    }

    public void Tick(double dt)
    {
        time += dt;
        quiet += dt;
        sinceBeat += dt;
        opacity += ((ActorsVisible ? 1 : 0) - opacity) * Math.Min(1, dt * 10);

        double left = PillRect.Left + InsetL, right = PillRect.Right - InsetR;
        if (!Inside) { left += PillRadius * 0.3; right -= PillRadius * 0.3; }
        if (right < left) left = right = (left + right) / 2;

        bool beat = false;
        if (MusicPlaying)
        {
            quiet = 0;
            double bass = (Levels[0] + Levels[1]) * 0.5;
            bassAvg += (bass - bassAvg) * Math.Min(1, dt * 1.5);
            if (sinceBeat > 0.26 && (bass > bassAvg * 1.22 + 0.04 || sinceBeat > 0.75))
            {
                beat = true;
                sinceBeat = 0;
                beatCount++;
            }
        }

        if (ball != null)
        {
            double lo = left + BallR, hi = right - BallR;
            if (hi < lo) lo = hi = (lo + hi) / 2;
            if (double.IsNaN(ball.X)) ball.X = lo + rng.NextDouble() * (hi - lo);
            ball.VY -= 800 * dt;
            ball.Y += ball.VY * dt;
            if (ball.Y <= 0)
            {
                ball.Y = 0;
                ball.VY = ball.VY < -45 ? -ball.VY * 0.55 : 0;
                ball.VX *= Math.Pow(0.25, dt);
            }
            ball.X += ball.VX * dt;
            ball.Rot += ball.VX * dt / BallR;
            if (ball.X < lo) { ball.X = lo; ball.VX = Math.Abs(ball.VX) * 0.7; }
            if (ball.X > hi) { ball.X = hi; ball.VX = -Math.Abs(ball.VX) * 0.7; }
        }

        foreach (var a in actors) Step(a, dt, left, right, beat);
        InvalidateVisual();
    }

    void Step(Actor a, double dt, double left, double right, bool beat)
    {
        double half = a.Sprite.W * Scale(a) * 0.3;
        double lo = left + half, hi = right - half;
        if (hi < lo) lo = hi = (left + right) / 2;
        if (double.IsNaN(a.X)) a.X = lo + rng.NextDouble() * (hi - lo);

        if (a.Y > 0 || a.VY > 0)
        {
            a.VY -= 1100 * dt;
            a.Y += a.VY * dt;
            if (a.Y <= 0) { a.Y = 0; a.VY = 0; a.SquashV = -5; }
        }
        a.SquashV += (-220 * a.Squash - 16 * a.SquashV) * dt;
        a.Squash = Math.Clamp(a.Squash + a.SquashV * dt, -0.35, 0.35);
        if (a.EmoteT > 0) a.EmoteT -= dt;
        a.Timer -= dt;

        double speed = 30 * a.Def.Speed * (Inside ? 0.8 : 1);
        switch (a.State)
        {
            case St.Idle:
                if (a.Timer <= 0) Choose(a, lo, hi);
                break;

            case St.Walk:
                MoveTo(a, a.TargetX, speed, dt, lo, hi);
                if (rng.NextDouble() < dt * 0.3 * a.Def.Energy) Jump(a, 170);
                if (Math.Abs(Math.Clamp(a.TargetX, lo, hi) - a.X) < 1.5 || a.Timer < -9) Rest(a, 0.6 + rng.NextDouble() * 2.6 * (1.2 - a.Def.Energy));
                break;

            case St.ChaseBall:
                if (ball == null) { Rest(a, 0.3); break; }
                MoveTo(a, ball.X, speed * 1.9, dt, lo, hi);
                if (Math.Abs(ball.X - a.X) < half + 5 && ball.Y < 12)
                {
                    ball.VX = a.Facing * (90 + rng.NextDouble() * 110);
                    ball.VY = 130 + rng.NextDouble() * 130;
                    if (Inside) { ball.VX *= 0.7; ball.VY *= 0.5; }
                    Jump(a, 120);
                    Rest(a, 0.4 + rng.NextDouble());
                }
                else if (a.Timer < -6) Rest(a, 0.5);
                break;

            case St.ChaseFriend:
            {
                var f = a.Friend;
                if (f == null) { Rest(a, 0.3); break; }
                MoveTo(a, f.X, speed * 1.8, dt, lo, hi);
                if (Math.Abs(f.X - a.X) < half * 2 + 3)
                {
                    Jump(a, 190);
                    Jump(f, 160);
                    Say(a, "♥");
                    f.Facing = -a.Facing;
                    if (f.State == St.Sleep) { f.State = St.Idle; f.Timer = 1; }
                    Rest(a, 1);
                }
                else if (a.Timer < -6) Rest(a, 0.5);
                break;
            }

            case St.Sleep:
                if (MusicPlaying || a.Timer <= 0) Rest(a, 0.5);
                else if (a.EmoteT <= 0) Say(a, "z");
                break;

            case St.Dance:
                if (beat)
                {
                    Jump(a, 110 + 80 * a.Def.Energy);
                    if (beatCount % 4 == 0) a.Facing = -a.Facing;
                    if (beatCount % 8 == 0) Say(a, "♪");
                }
                if (!MusicPlaying || a.Timer <= 0) Rest(a, 0.3);
                break;
        }

        // 섬이 줄어들면 안쪽으로 종종걸음
        if (a.X < lo) a.X = Math.Min(lo, a.X + 260 * dt);
        if (a.X > hi) a.X = Math.Max(hi, a.X - 260 * dt);
    }

    void Rest(Actor a, double seconds) { a.State = St.Idle; a.Timer = seconds; }

    void MoveTo(Actor a, double target, double speed, double dt, double lo, double hi)
    {
        double d = Math.Clamp(target, lo, hi) - a.X;
        if (Math.Abs(d) <= 1) return;
        a.Facing = d > 0 ? 1 : -1;
        a.X += a.Facing * Math.Min(speed * dt, Math.Abs(d));
        a.Anim += dt;
    }

    void Choose(Actor a, double lo, double hi)
    {
        double e = a.Def.Energy, r = rng.NextDouble();
        if (MusicPlaying && r < 0.55 + 0.35 * e) { a.State = St.Dance; a.Timer = 4 + rng.NextDouble() * 6; return; }
        if (!MusicPlaying && quiet > 40 && r < 0.6 - 0.4 * e) { a.State = St.Sleep; a.Timer = 12 + rng.NextDouble() * 20; return; }

        r = rng.NextDouble();
        if (ball != null && r < 0.25 + 0.3 * e) { a.State = St.ChaseBall; a.Timer = 0; return; }
        if (actors.Count > 1 && r < 0.45 + 0.25 * e)
        {
            Actor other;
            do other = actors[rng.Next(actors.Count)]; while (other == a);
            a.Friend = other;
            a.State = St.ChaseFriend;
            a.Timer = 0;
            return;
        }
        if (r < 0.9) { a.State = St.Walk; a.TargetX = lo + rng.NextDouble() * (hi - lo); a.Timer = 0; return; }
        Rest(a, 1 + rng.NextDouble() * 2);
        if (e > 0.3) Jump(a, 200);
    }

    Rect Bounds(Actor a)
    {
        double ps = Scale(a);
        double w = a.Sprite.W * ps, h = (a.Sprite.H - a.Sprite.TopPad - a.Sprite.BottomPad) * ps;
        double feet = Ground - a.Y - Hover(a);
        return new Rect(a.X - w / 2, feet - h, w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (opacity < 0.01 || PillRect.Width <= 0) return;
        double ground = Ground;
        dc.PushOpacity(opacity);
        if (Inside)
        {
            var clip = new RectangleGeometry(PillRect, PillRadius, PillRadius);
            clip.Freeze();
            dc.PushClip(clip);
        }

        if (ball != null && !double.IsNaN(ball.X))
        {
            var c = new Point(ball.X, ground - BallR - ball.Y);
            dc.DrawEllipse(ballBrush, BallOutline, c, BallR, BallR);
            double dx = Math.Cos(ball.Rot) * (BallR - 1.5), dy = Math.Sin(ball.Rot) * (BallR - 1.5);
            dc.DrawLine(BallOutline, new Point(c.X - dx, c.Y - dy), new Point(c.X + dx, c.Y + dy));
        }

        foreach (var a in actors)
        {
            if (double.IsNaN(a.X) || a.Sprite.Frames.Length == 0) continue;
            double ps = Scale(a);
            double w = a.Sprite.W * ps, h = a.Sprite.H * ps;
            bool moving = a.State is St.Walk or St.ChaseBall or St.ChaseFriend;
            int frame = moving ? (int)(a.Anim * 7) % 2
                : a.State == St.Dance ? beatCount % 2
                : a.Def.Floats ? (int)(time * 2.5 + a.Phase) % 2
                : 0;

            double sx = 1 - a.Squash * 0.5, sy = 1 + a.Squash;
            double tilt = 0;
            if (a.State == St.Sleep) { sy *= 0.86 + 0.03 * Math.Sin(time * 2 + a.Phase); sx *= 1.05; }
            if (a.State == St.Dance) tilt = Math.Sin(time * 9 + a.Phase) * 7;

            // 발끝을 기준으로 늘이고(scale) 기울인(rotate) 뒤 제자리에 놓는다(translate)
            var m = Matrix.Identity;
            m.Scale(a.Facing * sx, sy);
            m.Rotate(tilt);
            m.Translate(a.X, ground - a.Y - Hover(a));
            var tf = new MatrixTransform(m);
            tf.Freeze();
            dc.PushTransform(tf);
            dc.DrawImage(a.Sprite.Frames[frame % a.Sprite.Frames.Length], new Rect(-w / 2, -h + a.Sprite.BottomPad * ps, w, h));
            dc.Pop();
        }

        if (Inside) dc.Pop();

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var a in actors)
        {
            if (a.EmoteT <= 0 || a.Emote == null || double.IsNaN(a.X)) continue;
            var b = Bounds(a);
            var ft = new FormattedText(a.Emote, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, EmoteFace, 13, EmoteBrush, dip);
            double rise = (1.4 - a.EmoteT) * 7;
            double x = a.X - ft.Width / 2, y = b.Top - ft.Height - rise;
            if (y < 1) { y = Math.Max(1, b.Top - rise); x = a.X + a.Facing * (b.Width / 2 + 3) - (a.Facing < 0 ? ft.Width : 0); }
            var geo = ft.BuildGeometry(new Point(x, y));
            dc.PushOpacity(Math.Min(1, a.EmoteT * 3));
            dc.DrawGeometry(null, EmoteOutline, geo);
            dc.DrawGeometry(EmoteBrush, null, geo);
            dc.Pop();
        }
        dc.Pop();
    }

    Actor? ActorAt(Point p)
    {
        if (opacity < 0.5) return null;
        for (int i = actors.Count - 1; i >= 0; i--)
            if (!double.IsNaN(actors[i].X) && Bounds(actors[i]).Contains(p)) return actors[i];
        return null;
    }

    bool BallAt(Point p)
    {
        if (ball == null || opacity < 0.5 || double.IsNaN(ball.X)) return false;
        var c = new Point(ball.X, Ground - BallR - ball.Y);
        return (p - c).Length <= BallR + 3;
    }

    // 캐릭터나 공 위에서만 클릭을 받고, 나머지는 아래(섬의 버튼들)로 흘려보낸다
    protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
        ActorAt(p.HitPoint) != null || BallAt(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        quiet = 0;
        if (ActorAt(p) is { } a)
        {
            if (a.State == St.Sleep) Rest(a, 1);
            Jump(a, 230);
            Say(a, "♥");
            e.Handled = true;
        }
        else if (ball != null && BallAt(p))
        {
            ball.VX = (ball.X >= p.X ? 1 : -1) * (120 + rng.NextDouble() * 80);
            ball.VY = Inside ? 90 : 220;
            e.Handled = true;
        }
    }
}
