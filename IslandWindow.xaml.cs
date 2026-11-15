using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    enum Mode { CompactIdle, CompactMedia, Media, Control, List, Calendar, Unlock, Notif }

    const double UnlockSeconds = 2.4;

    static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    readonly MediaService media;
    readonly AudioLevelService audio;
    readonly SystemControlService system;
    readonly WifiService wifi = new();
    readonly BluetoothService bluetooth = new();
    readonly AudioOutputService outputs = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Spring width = new(), height = new(), radius = new(), top = new();
    readonly float[] levels = new float[8], raw = new float[8];
    readonly Queue<IslandNotification> queue = new();
    readonly DispatcherTimer housekeeping = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer idleTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };

    Theme theme = new();
    IntPtr hwnd;
    Mode mode = Mode.CompactIdle;
    bool hover, expanded, showControl, peekControl, wasPlaying, layoutDirty = true, hooked;
    double last, hoverIn, hoverOut, peek, notifLeft, topOffset = 8, audioIdle = 10, controlRefresh, unlockLeft;
    double appliedW, appliedH, appliedR, appliedTop;
    int lastMinute = -1;
    IslandNotification? current;

    public event Action? OpenEditorRequested;

    public IslandWindow(MediaService media, NotificationService notifications, AudioLevelService audio, SystemControlService system)
    {
        InitializeComponent();
        this.media = media;
        this.audio = audio;
        this.system = system;

        CompactVis.Levels = BigVis.Levels = Stage.Levels = levels;

        media.Changed += OnMediaChanged;
        system.Changed += UpdateControl;
        notifications.Received += n =>
        {
            if (ThemeStore.Settings.ShowNotifications) Enqueue(n);
        };

        PillHost.MouseEnter += (_, _) => hover = true;
        PillHost.MouseLeave += (_, _) => hover = false;
        PillHost.MouseLeftButtonDown += (_, _) =>
        {
            if (current != null) notifLeft = 0;   // 알림은 눌러서 닫기
            else expanded = true;
        };
        // 오른쪽 클릭: 음악이 나오는 중에도 바로 제어 센터로
        PillHost.MouseRightButtonUp += (_, e) =>
        {
            if (current != null) return;
            showControl = !showControl || !expanded;
            expanded = true;
            e.Handled = true;
        };
        // 섬 위에서 휠을 굴리면 볼륨 조절
        PillHost.MouseWheel += (_, e) =>
        {
            if (current != null || listPage != ListPage.None || calendarOpen) return;
            system.SetVolume(system.Volume + e.Delta / 120.0 * 0.02);
            showControl = expanded = true;
            UpdateControl();
            e.Handled = true;
        };

        VolumeSlider.ValueChanged += v =>
        {
            system.SetVolume(v);
            UpdateVolumeText();
        };
        BrightSlider.ValueChanged += v =>
        {
            system.SetBrightness(v);
            BrightText.Text = $"{v * 100:0}";
        };

        ApplyTheme(ThemeStore.Active);
        width.Snap(140);
        height.Snap(34);
        radius.Snap(17);
        top.Snap(topOffset);
        UpdateClock();

        // 다른 창을 누르면 비밀번호 입력을 위해 잠깐 받았던 포커스를 돌려준다
        Deactivated += (_, _) => { if (inputMode) SetInputMode(false); };

        idleTimer.Tick += OnFrame;
        SetLively(true);
        housekeeping.Tick += (_, _) => Housekeeping();
        housekeeping.Start();
        _ = system.RefreshAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        // 작업 표시줄/Alt+Tab에 안 나오고, 눌러도 지금 쓰던 창의 포커스를 뺏지 않게
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
        Reposition();
    }

    void Reposition()
    {
        double x = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        if (Math.Abs(Left - x) > 0.5) Left = x;
        if (Math.Abs(Top) > 0.5) Top = 0;
    }

    void Housekeeping()
    {
        bool hide = ThemeStore.Settings.HideOnFullscreen && Native.IsFullscreenAppActive(hwnd);
        if (hide && IsVisible) Hide();
        else if (!hide && !IsVisible) Show();
        if (!IsVisible) return;
        Reposition();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ───────────────────────── 테마 ─────────────────────────

    public void ApplyTheme(Theme t)
    {
        theme = t;
        var bg = Util.ParseColor(t.Background, Colors.Black);
        Pill.Background = t.UseGradient
            ? new LinearGradientBrush(bg, Util.ParseColor(t.Background2, bg), new Point(0, 0), new Point(1, 1))
            : Util.Brush(bg);
        Pill.BorderBrush = Util.Brush(t.Border);
        Pill.BorderThickness = new Thickness(Math.Max(0, t.BorderThickness));
        PillHost.Opacity = Math.Clamp(t.Opacity, 0.2, 1);
        Glow.Visibility = t.Glow ? Visibility.Visible : Visibility.Collapsed;
        Glow.Background = Util.Brush(t.GlowColor);

        var text = Util.ParseColor(t.Text, Colors.White);
        Resources["TextBrush"] = Util.Brush(text);
        Resources["SubBrush"] = Util.Brush(Util.WithAlpha(text, 0.62));
        Resources["TrackBrush"] = Util.Brush(Util.WithAlpha(text, 0.18));
        Resources["FillBrush"] = Util.Brush(Util.WithAlpha(text, 0.42));
        VolumeSlider.Track = BrightSlider.Track = (Brush)Resources["TrackBrush"];
        VolumeSlider.Fill = BrightSlider.Fill = (Brush)Resources["FillBrush"];
        VolumeSlider.InvalidateVisual();
        BrightSlider.InvalidateVisual();
        UpdateAccent();

        CompactVis.Mode = BigVis.Mode = t.Visualizer;
        Particles.Configure(t.Particles, Util.ParseColor(t.ParticleColor, Colors.White));
        Stage.ApplyTheme(t);
        topOffset = t.Stage == "Inside" || t.Characters.Count == 0 ? 8 : Stage.Headroom + 3;
        layoutDirty = true;
    }

    void UpdateAccent()
    {
        var accent = Util.ParseColor(theme.Accent, Colors.HotPink);
        if (theme.UseAlbumColor && media.HasSession && media.ArtColor is Color album)
        {
            // 앨범 색이 배경에 묻히지 않게 밝기/채도를 조금 끌어올린다
            var (h, s, v) = Util.ToHsv(album);
            bool darkBg = Util.Luminance(Util.ParseColor(theme.Background, Colors.Black)) < 0.5;
            accent = darkBg
                ? Util.FromHsv(h, Math.Clamp(s, 0.35, 0.85), Math.Max(v, 0.85))
                : Util.FromHsv(h, Math.Max(s, 0.5), Math.Min(v, 0.6));
        }
        var brush = Util.Brush(accent);
        Resources["AccentBrush"] = brush;
        Resources["OnAccentBrush"] = Util.Luminance(accent) > 0.6 ? Brushes.Black : Brushes.White;
        CompactVis.Fill = BigVis.Fill = brush;
        Stage.EmoteBrush = brush;
        UpdateControl();
    }

    // ───────────────────────── 음악 ─────────────────────────

    void OnMediaChanged()
    {
        TitleText.Text = media.Title;
        ArtistText.Text = media.Artist;
        ArtistText.Visibility = media.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactTitle.Text = media.Title;
        ArtBrush.ImageSource = media.Art;
        CompactThumbBrush.ImageSource = media.Art;
        CompactThumb.Background = media.Art != null ? CompactThumbBrush : (Brush)Resources["TrackBrush"];
        PlayBtn.Content = media.IsPlaying ? "" : "";
        UpdateAccent();

        bool playing = media.HasSession && media.IsPlaying;
        if (playing && !wasPlaying) Stage.React("music");
        wasPlaying = playing;
        layoutDirty = true;
    }

    void Prev_Click(object sender, RoutedEventArgs e) => media.Previous();
    void Play_Click(object sender, RoutedEventArgs e) => media.Toggle();
    void Next_Click(object sender, RoutedEventArgs e) => media.Next();
    void Gear_Click(object sender, RoutedEventArgs e) => OpenEditorRequested?.Invoke();

    void Track_Click(object sender, MouseButtonEventArgs e)
    {
        var dur = media.Duration;
        if (dur.TotalSeconds <= 1 || Track.ActualWidth <= 0) return;
        double ratio = Math.Clamp(e.GetPosition(Track).X / Track.ActualWidth, 0, 1);
        media.Seek(TimeSpan.FromTicks((long)(dur.Ticks * ratio)));
        e.Handled = true;
    }

    static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    // ───────────────────────── 제어 센터 ─────────────────────────

    bool HasMedia => media.HasSession && media.Title.Length > 0;

    double ControlHeight => 188 +(system.BrightnessAvailable ? 38 : 0) + (HasMedia ? 44 : 0);

    void OpenControl_Click(object sender, RoutedEventArgs e) => showControl = true;
    void BackToMusic_Click(object sender, RoutedEventArgs e) => showControl = peekControl = false;
    void Mute_Click(object sender, RoutedEventArgs e) => system.ToggleMute();

    void WifiTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (system.WifiAvailable) _ = system.SetWifiAsync(!system.WifiOn);
    }

    void BluetoothTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (system.BluetoothAvailable) _ = system.SetBluetoothAsync(!system.BluetoothOn);
    }

    static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }

    void UpdateControl()
    {
        var accent = (Brush)Resources["AccentBrush"];
        var onAccent = (Brush)Resources["OnAccentBrush"];
        var text = (Brush)Resources["TextBrush"];
        var track = (Brush)Resources["TrackBrush"];

        void Tile(Border tile, Button more, bool available, bool on)
        {
            tile.Background = on ? accent : track;
            var fg = on ? onAccent : text;
            TextElement.SetForeground(tile, fg);
            more.Foreground = fg;
            tile.Opacity = available ? 1 : 0.45;
        }

        Tile(WifiTile, WifiMore, system.WifiAvailable, system.WifiOn);
        WifiStatus.Text = system.RadioNote.Length > 0 && !system.WifiAvailable ? "권한 필요"
            : !system.WifiAvailable ? "사용할 수 없음"
            : !system.WifiOn ? "꺼짐"
            : system.NetworkIsWifi && system.NetworkName.Length > 0 ? system.NetworkName
            : "켜짐 · 연결 안 됨";

        Tile(BluetoothTile, BluetoothMore, system.BluetoothAvailable, system.BluetoothOn);
        BluetoothStatus.Text = system.RadioNote.Length > 0 && !system.BluetoothAvailable ? "권한 필요"
            : !system.BluetoothAvailable ? "사용할 수 없음"
            : !system.BluetoothOn ? "꺼짐"
            : system.BluetoothDevices.Length > 0 ? system.BluetoothDevices
            : "켜짐";

        VolumeSlider.Value = system.Volume;
        UpdateVolumeText();

        BrightRow.Visibility = system.BrightnessAvailable ? Visibility.Visible : Visibility.Collapsed;
        BrightSlider.Value = system.Brightness;
        BrightText.Text = $"{BrightSlider.Value * 100:0}";

        bool hasMedia = HasMedia;
        BackToMusicBtn.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        MiniPlayer.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        MiniTitle.Text = media.Artist.Length > 0 ? $"{media.Title} · {media.Artist}" : media.Title;
        MiniThumbBrush.ImageSource = media.Art;
        MiniPlayBtn.Content = media.IsPlaying ? "" : "";
    }

    void UpdateVolumeText()
    {
        bool silent = system.Muted || system.Volume < 0.005;
        MuteBtn.Content = silent ? "" : "";
        VolumeText.Text = system.Muted ? "음소거" : $"{system.Volume * 100:0}";
    }

    // ───────────────────────── 알림 ─────────────────────────

    public void Enqueue(IslandNotification n)
    {
        if (queue.Count > 8) return;
        queue.Enqueue(n);
        if (current == null) NextNotification();
    }

    public void ShowTestNotification() =>
        Enqueue(new IslandNotification("Dynamic Island", "알림이 도착했어요 ✨", "윈도우 알림이 오면 이렇게 섬이 펼쳐져요. 눌러서 닫을 수 있어요.", null));

    /// <summary>편집기에서 "펼쳐 보기" — 잠깐 펼친 모습을 보여 준다.</summary>
    public void Peek(double seconds = 4)
    {
        peek = seconds;
        peekControl = false;
    }

    /// <summary>편집기에서 "제어 센터 보기".</summary>
    public void PeekControl(double seconds = 6)
    {
        peek = seconds;
        peekControl = true;
    }

    /// <summary>
    /// 윈도우 잠금이 풀렸을 때(또는 로그인 직후 앱이 켜졌을 때) Face ID 풍으로 자물쇠가 열리는 연출.
    /// 실제 얼굴 인식과 잠금 해제는 윈도우(Windows Hello)가 하고, 섬은 그 결과를 보여 줄 뿐이다.
    /// </summary>
    public void PlayUnlock(string text = "잠금 해제됨")
    {
        UnlockText.Text = text;
        UnlockGlyph.Text = "";
        unlockLeft = UnlockSeconds;
    }

    void UpdateUnlock()
    {
        double t = UnlockSeconds - unlockLeft;
        const double open = 0.55;
        bool opened = t >= open;
        string glyph = opened ? "" : "";
        if (UnlockGlyph.Text != glyph)
        {
            UnlockGlyph.Text = glyph;
            Stage.React("hello");
        }
        double pop = opened ? 1 + 0.45 * Math.Exp(-(t - open) * 7) : 1 - 0.08 * Math.Sin(t / open * Math.PI);
        UnlockScale.ScaleX = UnlockScale.ScaleY = pop;
        UnlockRing.Opacity = opened ? Math.Max(0.25, 1 - (t - open) * 1.2) : 0.35 + 0.65 * (t / open);
    }

    void NextNotification()
    {
        if (queue.Count == 0)
        {
            current = null;
            return;
        }
        current = queue.Dequeue();
        NotifApp.Text = current.App;
        NotifApp.Visibility = current.App.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifTitle.Text = current.Title;
        NotifBody.Text = current.Body;
        NotifBody.Visibility = current.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifIconBrush.ImageSource = current.Icon;
        string source = current.App.Length > 0 ? current.App : current.Title;
        NotifInitial.Text = current.Icon == null && source.Length > 0 ? source[..1].ToUpperInvariant() : "";
        notifLeft = Math.Clamp(ThemeStore.Settings.NotificationSeconds, 2, 15);
        Stage.React("notify");
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    /// <summary>
    /// 섬이 움직이거나 마우스가 올라와 있을 땐 화면 주사율에 맞춰 부드럽게 그리고,
    /// 평소엔 타이머로 느긋하게(초당 20여 번) 그려서 CPU를 아낀다.
    /// </summary>
    void SetLively(bool on)
    {
        if (on == hooked) return;
        hooked = on;
        if (on)
        {
            idleTimer.Stop();
            CompositionTarget.Rendering += OnFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnFrame;
            idleTimer.Start();
        }
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (hooked && now - last < 1 / 75.0) return; // 고주사율 모니터에서도 60fps 안팎으로만
        double dt = Math.Min(0.05, now - last);
        last = now;
        if (!IsVisible) return;

        var settings = ThemeStore.Settings;
        if (hover)
        {
            hoverIn += dt;
            hoverOut = 0;
            if (settings.ExpandOnHover && hoverIn > 0.12) expanded = true;
        }
        else
        {
            hoverIn = 0;
            hoverOut += dt;
            // 슬라이더를 끄는 중, 비밀번호를 입력하는 중, 연결을 시도하는 중에는 마우스가 나가도 접지 않는다.
            // (비밀번호를 다른 창에서 복사해 올 수 있도록 입력 칸은 1분 동안은 열어 둔다)
            bool dragging = Mouse.Captured is IslandSlider;
            bool typing = PasswordPanel.Visibility == Visibility.Visible;
            pinIdle = typing ? pinIdle + dt : 0;
            bool pinned = dragging || listBusy || inputMode || (typing && pinIdle < 60);
            if (!pinned && hoverOut > (settings.ExpandOnHover ? 0.35 : 1.0))
            {
                expanded = false;
                if (peek <= 0)
                {
                    showControl = false;
                    CloseList();
                    CloseCalendar();
                }
            }
        }
        if (hover) pinIdle = 0;
        if (peek > 0)
        {
            peek -= dt;
            if (peek <= 0)
            {
                peekControl = false;
                if (!expanded)
                {
                    showControl = false;
                    CloseList();
                    CloseCalendar();
                }
            }
        }
        if (unlockLeft > 0)
        {
            unlockLeft -= dt;
            UpdateUnlock();
        }

        if (current != null)
        {
            if (!hover) notifLeft -= dt;
            if (notifLeft <= 0) NextNotification();
        }

        bool hasMedia = HasMedia;
        bool playing = hasMedia && media.IsPlaying;
        bool inside = theme.Stage == "Inside";
        bool open = expanded || peek > 0;
        bool control = !hasMedia || showControl || (peek > 0 && peekControl);

        Mode m = current != null ? Mode.Notif
            : unlockLeft > 0 ? Mode.Unlock
            : open ? (!control ? Mode.Media : listPage != ListPage.None ? Mode.List : calendarOpen ? Mode.Calendar : Mode.Control)
            : hasMedia ? Mode.CompactMedia : Mode.CompactIdle;

        if (m != mode || layoutDirty)
        {
            if (m == Mode.Control && mode != Mode.Control)
            {
                controlRefresh = 0;
                UpdateControl();
            }
            mode = m;
            layoutDirty = false;
            UpdateCompact(hasMedia, inside);
        }

        TickStats(dt);
        if (m == Mode.List) TickList(dt);
        if (m == Mode.Calendar) UpdateCalendarHeight();

        // 기본 상태의 폭은 내용(시계 + 리소스 + 배터리)에 맞춘다. "섬 안" 모드는 캐릭터가 놀 자리를 더 준다.
        double idleContent = IdleRow.DesiredSize.Width;
        double idleWidth = Math.Clamp(idleContent + 30, 110, 330);
        (double w, double h) = m switch
        {
            Mode.CompactIdle => inside ? (Math.Min(idleWidth + 96, 420), 36.0) : (idleWidth, 34.0),
            Mode.CompactMedia => inside ? (250.0, 36.0) : (220.0, 36.0),
            Mode.Media => (380.0, 156.0),
            Mode.Control => (380.0, ControlHeight),
            Mode.List => (380.0, listHeight),
            Mode.Calendar => (380.0, calendarHeight),
            Mode.Unlock => (200.0, 58.0),
            _ => (380.0, 92.0),
        };
        width.Target = w;
        height.Target = h;
        radius.Target = Math.Max(3, theme.Roundness * Math.Min(h / 2, 40));
        top.Target = topOffset;
        width.Step(dt);
        height.Step(dt);
        radius.Step(dt);
        top.Step(dt);
        ApplyGeometry();

        bool compact = m is Mode.CompactIdle or Mode.CompactMedia;
        SetLively(hover || !compact || Moving(width) || Moving(height) || Moving(top) || CompactLayer.Opacity < 0.99);

        Fade(CompactLayer, compact, dt);
        Fade(MediaLayer, m == Mode.Media, dt);
        Fade(ControlLayer, m == Mode.Control, dt);
        Fade(ListLayer, m == Mode.List, dt);
        Fade(CalendarLayer, m == Mode.Calendar, dt);
        Fade(UnlockLayer, m == Mode.Unlock, dt);
        Fade(NotifLayer, m == Mode.Notif, dt);

        int minute = DateTime.Now.Minute;
        if (minute != lastMinute)
        {
            lastMinute = minute;
            UpdateClock();
        }

        if (m == Mode.Media) UpdateProgress();
        if (m == Mode.Control)
        {
            // 열려 있는 동안에는 몇 초마다 상태(연결된 네트워크, 블루투스 기기 등)를 다시 읽는다
            controlRefresh -= dt;
            if (controlRefresh <= 0)
            {
                controlRefresh = 3;
                _ = system.RefreshAsync();
            }
        }

        UpdateLevels(dt, now, playing);
        if (hasMedia)
        {
            if (compact) CompactVis.InvalidateVisual();
            else if (m == Mode.Media) BigVis.InvalidateVisual();
        }

        Stage.PillRect = new Rect((ActualWidth - appliedW) / 2, appliedTop, appliedW, appliedH);
        Stage.PillRadius = appliedR;
        Stage.Inside = inside;
        Stage.ActorsVisible = !inside || compact;
        Stage.MusicPlaying = playing;
        if (inside)
        {
            Stage.InsetL = hasMedia ? 36 : 10;
            Stage.InsetR = hasMedia ? 42 : idleContent + 22; // 오른쪽에 붙은 시계·리소스 표시를 피해서
        }
        else Stage.InsetL = Stage.InsetR = 0;
        Stage.Tick(dt);
        Particles.Tick(dt);
    }

    static bool Moving(Spring s) => Math.Abs(s.Value - s.Target) > 0.2 || Math.Abs(s.Velocity) > 2;

    void UpdateCompact(bool hasMedia, bool inside)
    {
        var show = Visibility.Visible;
        var gone = Visibility.Collapsed;
        IdleRow.Visibility = hasMedia ? gone : show;
        IdleRow.HorizontalAlignment = inside ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        IdleRow.Margin = inside ? new Thickness(0, 0, 13, 0) : new Thickness(0);
        CompactThumb.Visibility = hasMedia ? show : gone;
        CompactVis.Visibility = hasMedia ? show : gone;
        CompactTitle.Visibility = hasMedia && !inside ? show : gone; // "섬 안" 모드에선 가운데가 캐릭터 자리
    }

    void ApplyGeometry()
    {
        double w = Math.Max(24, width.Value), h = Math.Max(16, height.Value);
        double r = Math.Clamp(radius.Value, 0, h / 2), t = Math.Max(0, top.Value);
        if (Math.Abs(w - appliedW) < 0.05 && Math.Abs(h - appliedH) < 0.05 &&
            Math.Abs(r - appliedR) < 0.05 && Math.Abs(t - appliedTop) < 0.05) return;

        appliedW = w; appliedH = h; appliedR = r; appliedTop = t;
        PillHost.Width = w;
        PillHost.Height = h;
        PillHost.Margin = new Thickness(0, t, 0, 0);
        Pill.CornerRadius = Glow.CornerRadius = new CornerRadius(r);
        double bt = Pill.BorderThickness.Left;
        PillContent.Clip = new RectangleGeometry(
            new Rect(0, 0, Math.Max(0, w - 2 * bt), Math.Max(0, h - 2 * bt)),
            Math.Max(0, r - bt), Math.Max(0, r - bt));
    }

    static void Fade(FrameworkElement el, bool on, double dt)
    {
        double target = on ? 1 : 0;
        if (Math.Abs(el.Opacity - target) > 0.004)
            el.Opacity += (target - el.Opacity) * Math.Min(1, dt * (on ? 9 : 16));
        else if (el.Opacity != target) el.Opacity = target;

        el.IsHitTestVisible = on;
        var vis = !on && el.Opacity < 0.01 ? Visibility.Collapsed : Visibility.Visible;
        if (el.Visibility != vis) el.Visibility = vis;
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("tt h:mm", Korean);
        CtrlClock.Text = now.ToString("H:mm");
        CtrlDate.Text = now.ToString("M월 d일 dddd", Korean);
    }

    void UpdateProgress()
    {
        var dur = media.Duration;
        if (dur.TotalSeconds > 1)
        {
            var pos = media.CurrentPosition;
            TrackFill.Width = Math.Max(0, Track.ActualWidth * Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1));
            PosText.Text = Fmt(pos);
            DurText.Text = Fmt(dur);
        }
        else
        {
            TrackFill.Width = 0;
            PosText.Text = DurText.Text = "";
        }
    }

    void UpdateLevels(double dt, double now, bool playing)
    {
        // 실제 소리 분석은 음악이 재생 중일 때만 켠다
        // (켜고 끄는 일 자체는 AudioLevelService가 백그라운드에서 한다 — 여기서는 원하는 상태만 알려 준다)
        bool want = playing && ThemeStore.Settings.AudioReactive;
        audioIdle = want ? 0 : audioIdle + dt;
        audio.SetWanted(want || audioIdle < 3);

        bool fresh = want && audio.Read(raw);
        for (int i = 0; i < levels.Length; i++)
        {
            double target = 0;
            if (playing)
            {
                // 소리를 읽을 수 없으면(음소거, 다른 출력 장치) 그럴듯한 움직임으로 대신한다
                target = fresh
                    ? raw[i]
                    : 0.2 + 0.6 * Math.Abs(Math.Sin(now * (3 + i * 0.7) + i * 1.3)) * Math.Abs(Math.Sin(now * 1.7 + i));
            }
            double cur = levels[i];
            cur = target > cur ? cur + (target - cur) * Math.Min(1, dt * 30) : Math.Max(target, cur - dt * 2.4);
            levels[i] = (float)cur;
        }
    }
}
