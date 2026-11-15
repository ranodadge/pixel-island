using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>테마 꾸미기 창. 목록에서 고른 테마가 곧 지금 적용된 테마이고, 고치는 즉시 섬에 반영된다.</summary>
public partial class EditorWindow : Window
{
    const int MaxCharacters = 3;

    /// <summary>사진 도트화에 쓴 원본과 옵션. 편집기를 열어 둔 동안만 기억해서, 옵션을 바꿔 가며 다시 변환할 수 있게 한다.</summary>
    sealed class PhotoSource
    {
        public string Path = "";
        public int Colors = 8;
        public bool RemoveBackground = true, Outline = true;
    }

    readonly Dictionary<CharacterDef, PhotoSource> photos = new();

    readonly IslandWindow island;
    readonly NotificationService notifications;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    Theme? dirty;
    bool ready, refreshing;
    string tab = "Look", frame = "A", tool = "Pen";
    int charIndex, palIndex = 1;
    Panel target = null!;

    public EditorWindow(IslandWindow island, NotificationService notifications)
    {
        InitializeComponent();
        this.island = island;
        this.notifications = notifications;
        target = Body;

        saveTimer.Tick += (_, _) => Flush();
        Closed += (_, _) =>
        {
            Flush();
            notifications.StatusChanged -= OnNotificationStatus;
        };
        notifications.StatusChanged += OnNotificationStatus;

        ThemeList.ItemsSource = ThemeStore.Themes;
        ThemeList.SelectedItem = ThemeStore.Active;
        ready = true;
        RenderTab();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int on = 1; // 어두운 제목 표시줄
        Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    Theme T => ThemeList.SelectedItem as Theme ?? ThemeStore.Active;

    void OnNotificationStatus()
    {
        if (tab == "Settings") RenderTab();
    }

    public void SelectTheme(Theme t)
    {
        if (!ReferenceEquals(ThemeList.SelectedItem, t)) ThemeList.SelectedItem = t;
    }

    // ───────────────────────── 저장/적용 ─────────────────────────

    void Touch()
    {
        dirty = T;
        island.ApplyTheme(T);
        saveTimer.Stop();
        saveTimer.Start();
    }

    void Flush()
    {
        saveTimer.Stop();
        if (dirty != null) ThemeStore.Save(dirty);
        dirty = null;
        ThemeStore.SaveSettings();
    }

    void RefreshList(Theme? select = null)
    {
        refreshing = true;
        ThemeList.Items.Refresh();
        refreshing = false;
        if (select != null) ThemeList.SelectedItem = select;
    }

    void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || refreshing || ThemeList.SelectedItem is not Theme t) return;
        Flush();
        ThemeStore.Settings.ActiveThemeId = t.Id;
        ThemeStore.SaveSettings();
        island.ApplyTheme(t);
        charIndex = 0;
        frame = "A";
        RenderTab();
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready || sender is not RadioButton { Tag: string tag }) return;
        tab = tag;
        RenderTab();
    }

    void Peek_Click(object sender, RoutedEventArgs e) => island.Peek();
    void PeekControl_Click(object sender, RoutedEventArgs e) => island.PeekControl();
    void TestNotification_Click(object sender, RoutedEventArgs e) => island.ShowTestNotification();

    // ───────────────────────── 테마 목록 버튼 ─────────────────────────

    void AddTheme(Theme t)
    {
        Flush();
        ThemeStore.Themes.Add(t);
        ThemeStore.Save(t);
        RefreshList(t);
    }

    void New_Click(object sender, RoutedEventArgs e) =>
        AddTheme(new Theme { Name = "새 테마", Characters = { new CharacterDef() } });

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var copy = T.Clone();
        copy.Name = T.Name + " 복사본";
        AddTheme(copy);
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeStore.Themes.Count <= 1) return;
        var t = T;
        if (MessageBox.Show(this, $"'{t.Name}' 테마를 지울까요? 되돌릴 수 없어요.", "테마 삭제",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        dirty = null;
        ThemeStore.Delete(t);
        RefreshList(ThemeStore.Themes[0]);
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "테마 파일 (*.json)|*.json", Title = "테마 가져오기" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(dlg.FileName), Json.Options)
                    ?? throw new InvalidDataException();
            t.Id = Guid.NewGuid().ToString("N");
            AddTheme(t);
        }
        catch
        {
            MessageBox.Show(this, "테마 파일을 읽을 수 없어요.", "가져오기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "테마 파일 (*.json)|*.json", FileName = T.Name + ".json", Title = "테마 내보내기" };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(T, Json.Options)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "내보내기", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "기본 테마 6개를 처음 상태로 되돌릴까요?\n직접 만든 테마는 그대로 남아요.", "기본 테마 복원",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        string id = T.Id;
        dirty = null;
        ThemeStore.RestoreBuiltIns();
        RefreshList(ThemeStore.Themes.FirstOrDefault(t => t.Id == id) ?? ThemeStore.Themes[0]);
    }

    // ───────────────────────── 폼 조각들 ─────────────────────────

    void Rebuild() => Dispatcher.BeginInvoke(RenderTab);

    void Header(string text) =>
        target.Children.Add(new TextBlock
        {
            Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, target.Children.Count == 0 ? 0 : 24, 0, 10),
        });

    void Hint(string text) =>
        target.Children.Add(new TextBlock
        {
            Text = text, FontSize = 12, Opacity = 0.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(ReferenceEquals(target, Body) ? 150 : 0, 0, 0, 6),
        });

    void Label(string text) =>
        target.Children.Add(new TextBlock { Text = text, Opacity = 0.8, Margin = new Thickness(0, 10, 0, 6) });

    void Row(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        target.Children.Add(g);
    }

    void ColorRow(string label, Func<string> get, Action<string> set)
    {
        var b = new ColorPickerButton { Hex = get(), Margin = new Thickness(0) };
        b.ColorChanged += h => { set(h); Touch(); };
        Row(label, b);
    }

    void SliderRow(string label, double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> format, Action? released = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var s = new Slider { Minimum = min, Maximum = max, Value = get(), Width = 230, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = format(get()), Margin = new Thickness(12, 0, 0, 0), Width = 60, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            t.Text = format(e.NewValue);
            Touch();
        };
        // 끄는 동안 화면을 다시 짜면 드래그가 끊기므로, 손을 뗀 뒤에 하고 싶은 일은 여기서
        if (released != null) s.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(released);
        sp.Children.Add(s);
        sp.Children.Add(t);
        Row(label, sp);
    }

    void CheckRow(string label, Func<bool> get, Action<bool> set, bool rebuild = false)
    {
        var c = new CheckBox { IsChecked = get(), Content = "사용" };
        void Changed(object s, RoutedEventArgs e)
        {
            set(c.IsChecked == true);
            Touch();
            if (rebuild) Rebuild();
        }
        c.Checked += Changed;
        c.Unchecked += Changed;
        Row(label, c);
    }

    WrapPanel Chips((string value, string text)[] options, Func<string> get, Action<string> set, bool rebuild = false, bool touch = true)
    {
        var wp = new WrapPanel();
        string group = Guid.NewGuid().ToString("N");
        string current = get();
        foreach (var (value, text) in options)
        {
            var rb = new RadioButton { Content = text, GroupName = group, IsChecked = value == current, Style = (Style)FindResource("Chip") };
            rb.Checked += (_, _) =>
            {
                set(value);
                if (touch) Touch();
                if (rebuild) Rebuild();
            };
            wp.Children.Add(rb);
        }
        return wp;
    }

    void ChipsRow(string label, (string value, string text)[] options, Func<string> get, Action<string> set, bool rebuild = false) =>
        Row(label, Chips(options, get, set, rebuild));

    static Button SmallButton(string text, Action click)
    {
        var b = new Button { Content = text };
        b.Click += (_, _) => click();
        return b;
    }

    static string Percent(double v) => $"{v * 100:0}%";

    // ───────────────────────── 탭 ─────────────────────────

    void RenderTab()
    {
        if (!ready) return;
        Body.Children.Clear();
        target = Body;
        switch (tab)
        {
            case "Chars": RenderCharacters(); break;
            case "Decor": RenderDecor(); break;
            case "Settings": RenderSettings(); break;
            default: RenderLook(); break;
        }
    }

    void RenderLook()
    {
        var t = T;
        Header("기본");
        var name = new TextBox { Text = t.Name, Width = 250 };
        name.TextChanged += (_, _) =>
        {
            t.Name = name.Text;
            Touch();
            RefreshList();
        };
        Row("테마 이름", name);

        Header("색");
        ColorRow("배경색", () => t.Background, v => t.Background = v);
        CheckRow("그라데이션", () => t.UseGradient, v => t.UseGradient = v, true);
        if (t.UseGradient) ColorRow("그라데이션 끝 색", () => t.Background2, v => t.Background2 = v);
        ColorRow("글자색", () => t.Text, v => t.Text = v);
        ColorRow("강조색", () => t.Accent, v => t.Accent = v);
        CheckRow("앨범 색 따라가기", () => t.UseAlbumColor, v => t.UseAlbumColor = v);
        Hint("음악이 나올 때 강조색(비주얼라이저 등)을 앨범 아트의 대표색으로 바꿔요.");

        Header("모양");
        SliderRow("둥글기", 0, 1, 0.05, () => t.Roundness, v => t.Roundness = v, Percent);
        SliderRow("불투명도", 0.4, 1, 0.05, () => t.Opacity, v => t.Opacity = v, Percent);
        SliderRow("테두리 두께", 0, 4, 0.5, () => t.BorderThickness, v => t.BorderThickness = v, v => $"{v:0.#}px");
        ColorRow("테두리 색", () => t.Border, v => t.Border = v);
        CheckRow("빛 번짐(글로우)", () => t.Glow, v => t.Glow = v, true);
        if (t.Glow) ColorRow("글로우 색", () => t.GlowColor, v => t.GlowColor = v);

        Header("음악 비주얼라이저");
        ChipsRow("스타일", new[] { ("Bars", "막대"), ("Wave", "물결"), ("Dots", "점") }, () => t.Visualizer, v => t.Visualizer = v);
    }

    void RenderDecor()
    {
        var t = T;
        Header("섬 안 장식");
        ChipsRow("흩날리는 것", new[] { ("None", "없음"), ("Stars", "별"), ("Snow", "눈"), ("Petals", "꽃잎"), ("Hearts", "하트"), ("Bubbles", "방울") },
            () => t.Particles, v => t.Particles = v);
        ColorRow("장식 색", () => t.ParticleColor, v => t.ParticleColor = v);

        Header("장난감");
        CheckRow("공", () => t.Ball, v => t.Ball = v, true);
        if (t.Ball) ColorRow("공 색", () => t.BallColor, v => t.BallColor = v);
        Hint("캐릭터들이 공을 쫓아가서 차요. 공이나 캐릭터를 직접 클릭해도 반응해요.");
    }

    void RenderSettings()
    {
        var s = ThemeStore.Settings;
        Header("동작");
        ChipsRow("펼치는 방법", new[] { ("hover", "마우스를 올리면"), ("click", "클릭하면") },
            () => s.ExpandOnHover ? "hover" : "click", v => s.ExpandOnHover = v == "hover");
        CheckRow("전체 화면에서 숨기기", () => s.HideOnFullscreen, v => s.HideOnFullscreen = v);
        Hint("게임이나 동영상을 전체 화면으로 볼 때는 아일랜드를 숨겨요.");
        CheckRow("소리에 맞춰 움직이기", () => s.AudioReactive, v => s.AudioReactive = v);
        Hint("음악이 재생되는 동안 스피커로 나가는 소리의 크기를 읽어서 비주얼라이저와 캐릭터 춤에 써요. 소리는 저장하거나 전송하지 않아요.");
        CheckRow("윈도우 시작 시 실행", Startup.Get, Startup.Set);
        Hint("켜 두면 컴퓨터를 켜고 로그인할 때 아일랜드가 자동으로 같이 켜져요.");

        Header("앱 설치");
        Row("상태", new TextBlock
        {
            Text = Installer.RunningInstalled ? "설치됨 — 시작 메뉴나 윈도우 검색에서 'Dynamic Island'로 실행할 수 있어요"
                 : Installer.IsInstalled ? "설치돼 있지만, 지금은 설치본이 아닌 다른 위치의 파일로 켜져 있어요"
                 : "아직 설치 안 됨",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 460,
        });
        var install = new WrapPanel();
        if (!Installer.RunningInstalled)
            install.Children.Add(SmallButton(Installer.IsInstalled ? "설치본을 이 버전으로 바꾸기" : "앱으로 설치하기",
                () => Installer.Launch(Environment.ProcessPath!, "--install")));
        if (Installer.IsInstalled)
            install.Children.Add(SmallButton("제거", () => Installer.Launch(Installer.InstalledExe, "--uninstall")));
        Row("", install);
        if (Installer.IsInstalled)
            CheckRow("바탕 화면 바로가기", () => Installer.HasDesktopShortcut, Installer.SetDesktopShortcut);
        Hint("설치하면 시작 메뉴와 윈도우 검색에 앱이 나오고, 설정 > 앱 > 설치된 앱 목록에도 올라가요. " +
             "이미 켜져 있을 때 아이콘을 다시 누르면 이 꾸미기 창이 열려요. 제거해도 만들어 둔 테마와 설정은 남아요.");

        Header("기본 상태에 표시");
        CheckRow("CPU·메모리 사용량", () => s.ShowStats, v => s.ShowStats = v);
        CheckRow("배터리 잔량", () => s.ShowBattery, v => s.ShowBattery = v);
        Hint("접혀 있는 섬에 시계와 함께 보여요(음악이 나올 때는 음악 정보가 대신 나와요). 배터리는 노트북처럼 배터리가 있는 기기에서만 표시돼요. " +
             "펼친 제어 센터에서는 더 자세한 게이지로 볼 수 있어요.");

        Header("얼굴 인식 잠금 해제 (Face ID)");
        var hello = new TextBlock { Text = "확인 중…", TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        Row("Windows Hello", hello);
        _ = ShowHelloStatusAsync(hello);
        var helloButtons = new WrapPanel();
        helloButtons.Children.Add(SmallButton("얼굴 인식 설정 열기", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true }); } catch { }
        }));
        helloButtons.Children.Add(SmallButton("잠금 해제 연출 미리 보기", () => island.PlayUnlock()));
        Row("", helloButtons);
        CheckRow("잠금 해제 연출", () => s.UnlockAnimation, v => s.UnlockAnimation = v);
        Hint("잠긴 화면을 실제로 푸는 일은 보안 때문에 윈도우만 할 수 있어요. 얼굴 인식용 적외선 카메라가 있는 PC라면," +
             "윈도우 설정 > 계정 > 로그인 옵션 > 얼굴 인식(Windows Hello)에서 얼굴을 등록하면 화면을 보기만 해도 잠금이 풀려요. " +
             "잠금이 풀리는 순간 아일랜드에서 자물쇠가 열리는 연출이 나와요.");

        Header("알림");
        CheckRow("윈도우 알림 표시", () => s.ShowNotifications, v => s.ShowNotifications = v);
        SliderRow("표시 시간", 2, 10, 1, () => s.NotificationSeconds, v => s.NotificationSeconds = v, v => $"{v:0}초");
        Row("알림 접근 권한", new TextBlock { Text = notifications.Status, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
        var buttons = new WrapPanel();
        buttons.Children.Add(SmallButton("윈도우 알림 설정 열기", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); } catch { }
        }));
        buttons.Children.Add(SmallButton("다시 확인", () => _ = notifications.StartAsync()));
        Row("", buttons);
    }

    static async Task ShowHelloStatusAsync(TextBlock target)
    {
        try
        {
            var availability = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
            target.Text = availability switch
            {
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available =>
                    "사용 중 — 얼굴·지문·PIN 중 등록해 둔 방법으로 잠금이 풀려요",
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.NotConfiguredForUser =>
                    "아직 설정 안 됨 — 아래 버튼으로 얼굴을 등록해 주세요",
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.DisabledByPolicy =>
                    "이 PC의 정책으로 꺼져 있어요",
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.DeviceNotPresent =>
                    "얼굴·지문 인식 장치를 찾지 못했어요",
                _ => "지금은 상태를 확인할 수 없어요",
            };
        }
        catch { target.Text = "상태를 확인할 수 없어요"; }
    }

    // ───────────────────────── 캐릭터 ─────────────────────────

    void RenderCharacters()
    {
        var t = T;
        Header("무대");
        ChipsRow("노는 곳", new[] { ("OnTop", "섬 위에서"), ("Inside", "섬 안에서") }, () => t.Stage, v => t.Stage = v);
        Hint("'섬 위에서'는 캐릭터가 설 자리만큼 섬이 화면 위에서 조금 내려와요. '섬 안에서'는 접힌 섬 안에서만 놀아요.");

        Header("캐릭터");
        charIndex = Math.Clamp(charIndex, 0, Math.Max(0, t.Characters.Count - 1));
        var list = new WrapPanel();
        string group = Guid.NewGuid().ToString("N");
        for (int i = 0; i < t.Characters.Count; i++)
        {
            int index = i;
            var rb = new RadioButton
            {
                Content = $"{i + 1}. {Sprites.KindName(t.Characters[i].Kind)}", GroupName = group,
                IsChecked = i == charIndex, Style = (Style)FindResource("Chip"),
            };
            rb.Checked += (_, _) => { charIndex = index; frame = "A"; Rebuild(); };
            list.Children.Add(rb);
        }
        if (t.Characters.Count < MaxCharacters)
        {
            list.Children.Add(SmallButton("+ 추가", () =>
            {
                t.Characters.Add(new CharacterDef());
                charIndex = t.Characters.Count - 1;
                Touch();
                Rebuild();
            }));
        }
        Row("목록", list);

        if (t.Characters.Count == 0)
        {
            Hint("아직 캐릭터가 없어요. '+ 추가'를 눌러 보세요.");
            return;
        }

        var c = t.Characters[charIndex];
        var kinds = Sprites.Kinds.Select(k => (k, Sprites.KindName(k))).Append(("Custom", Sprites.KindName("Custom"))).Append(("Image", Sprites.KindName("Image"))).ToArray();
        ChipsRow("종류", kinds, () => c.Kind, v => SetKind(c, v), true);

        if (Sprites.IsBuiltIn(c.Kind))
        {
            ColorRow("몸 색", () => c.Color1, v => c.Color1 = v);
            ColorRow("포인트 색", () => c.Color2, v => c.Color2 = v);
        }
        else if (c.Kind == "Image")
        {
            var pick = new WrapPanel();
            pick.Children.Add(SmallButton("이미지 고르기…", () => PickImage(c)));
            pick.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(c.ImagePath) ? "아직 고른 이미지가 없어요 (그동안은 고양이가 대신 나와요)" : Path.GetFileName(c.ImagePath),
                Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 6),
            });
            if (!string.IsNullOrEmpty(c.ImagePath))
            {
                pick.Children.Add(SmallButton("이 이미지를 도트로 바꾸기", () =>
                {
                    photos[c] = new PhotoSource { Path = c.ImagePath! };
                    if (!ApplyPhoto(c)) return;
                    c.Kind = "Custom";
                    Touch();
                    Rebuild();
                }));
            }
            Row("이미지", pick);
            Hint("이미지를 그대로 쓰려면 배경이 투명한 PNG가 가장 잘 어울려요. '도트로 바꾸기'를 누르면 16×16 도트 캐릭터로 자동 변환돼요.");
        }

        SliderRow("크기", 1, 3, 0.5, () => c.PixelSize, v => c.PixelSize = v, v => $"{v * 16:0}px");
        SliderRow("속도", 0.4, 2, 0.1, () => c.Speed, v => c.Speed = v, v => $"x{v:0.0}");
        SliderRow("활발함", 0, 1, 0.05, () => c.Energy, v => c.Energy = v, Percent);
        Hint("활발할수록 자주 뛰고 공과 친구를 쫓아다녀요. 느긋하면 조용할 때 금방 잠들어요.");
        CheckRow("둥둥 떠다니기", () => c.Floats, v => c.Floats = v);

        if (c.Kind == "Custom") RenderPixelEditor(c);

        target = Body;
        var remove = SmallButton("이 캐릭터 빼기", () =>
        {
            t.Characters.Remove(c);
            charIndex = 0;
            Touch();
            Rebuild();
        });
        remove.Margin = new Thickness(0, 22, 0, 0);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(remove);
    }

    static void SetKind(CharacterDef c, string kind)
    {
        if (kind == "Custom")
        {
            // 처음 "직접 그리기"로 바꾸면 지금 캐릭터 그림에서 시작한다
            if (c.FrameA.Count == 0) LoadBuiltInInto(c, Sprites.IsBuiltIn(c.Kind) ? c.Kind : "Cat", c.Color1, c.Color2);
        }
        else if (Sprites.IsBuiltIn(kind))
        {
            (c.Color1, c.Color2) = Sprites.DefaultColors(kind);
            c.Floats = kind == "Ghost";
        }
        c.Kind = kind;
    }

    static void LoadBuiltInInto(CharacterDef c, string kind, string c1, string c2)
    {
        c.Palette = Sprites.BuiltInPalette(c1, c2);
        c.FrameA = Sprites.BuiltInFrame(kind, 0);
        c.FrameB = Sprites.BuiltInFrame(kind, 1);
    }

    void PickImage(CharacterDef c)
    {
        var dlg = new OpenFileDialog { Filter = "이미지 (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp", Title = "캐릭터 이미지 고르기" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // 원본이 옮겨지거나 지워져도 괜찮도록 앱 폴더에 복사해 둔다
            string dest = Path.Combine(ThemeStore.AssetsDir, Guid.NewGuid().ToString("N") + Path.GetExtension(dlg.FileName));
            File.Copy(dlg.FileName, dest, true);
            c.ImagePath = dest;
            Touch();
            Rebuild();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "이미지", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>기억해 둔 사진과 옵션으로 도트를 다시 만들어 캐릭터에 넣는다.</summary>
    bool ApplyPhoto(CharacterDef c)
    {
        if (!photos.TryGetValue(c, out var p)) return false;
        try
        {
            var r = Pixelizer.Convert(p.Path, p.Colors, p.RemoveBackground, p.Outline);
            c.Palette = r.Palette;
            c.FrameA = r.FrameA;
            c.FrameB = r.FrameB;
            palIndex = 1;
            return true;
        }
        catch (Exception ex)
        {
            photos.Remove(c);
            MessageBox.Show(this, "이 사진은 읽을 수 없어요.\n" + ex.Message, "사진으로 도트 만들기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    void PickPhoto(CharacterDef c)
    {
        var dlg = new OpenFileDialog { Filter = "이미지 (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp", Title = "도트로 바꿀 캐릭터 사진 고르기" };
        if (dlg.ShowDialog(this) != true) return;
        var old = photos.GetValueOrDefault(c);
        photos[c] = new PhotoSource
        {
            Path = dlg.FileName,
            Colors = old?.Colors ?? 8,
            RemoveBackground = old?.RemoveBackground ?? true,
            Outline = old?.Outline ?? true,
        };
        if (ApplyPhoto(c)) Touch();
        Rebuild();
    }

    void RenderPixelEditor(CharacterDef c)
    {
        target = Body;
        if (c.FrameA.Count == 0) LoadBuiltInInto(c, "Cat", c.Color1, c.Color2);
        if (c.FrameB.Count == 0) c.FrameB = new List<string>(c.FrameA);
        while (c.Palette.Count < 8) c.Palette.Add("#FFFFFF");
        palIndex = Math.Clamp(palIndex, 0, c.Palette.Count - 1);

        var editor = new PixelEditor { Tool = tool, Current = palIndex };
        editor.Load(frame == "A" ? c.FrameA : c.FrameB, c.Palette);

        // ── 사진 → 도트 자동 변환 ──
        Header("사진으로 도트 만들기");
        photos.TryGetValue(c, out var photo);
        var pickRow = new WrapPanel();
        pickRow.Children.Add(SmallButton(photo == null ? "사진 고르기…" : "다른 사진 고르기…", () => PickPhoto(c)));
        if (photo != null)
        {
            pickRow.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(photo.Path), Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 6),
            });
        }
        Row("캐릭터 사진", pickRow);
        if (photo != null)
        {
            void Reconvert()
            {
                if (ApplyPhoto(c)) editor.Load(frame == "A" ? c.FrameA : c.FrameB, c.Palette);
            }
            SliderRow("색 수", 2, 15, 1, () => photo.Colors, v => { photo.Colors = (int)v; Reconvert(); }, v => $"{v:0}색", released: RenderTab);
            CheckRow("배경 지우기", () => photo.RemoveBackground, v => { photo.RemoveBackground = v; Reconvert(); }, true);
            CheckRow("외곽선 두르기", () => photo.Outline, v => { photo.Outline = v; Reconvert(); }, true);
            Hint("옵션을 바꾸면 사진에서 다시 만들어요. 아래에서 손으로 고친 내용은 그때 덮어써지니, 옵션을 먼저 정하고 다듬어 주세요.");
        }
        else
        {
            Hint("캐릭터 사진이나 그림을 고르면 16×16 도트로 자동 변환해요. 배경이 투명하거나 단색(흰 배경 등)이고 캐릭터가 크게 나온 그림이 가장 잘 나와요. 변환한 뒤 아래에서 직접 다듬을 수 있어요.");
        }

        Header("도트 다듬기");
        editor.Edited += () =>
        {
            if (frame == "A") c.FrameA = editor.ToRows();
            else c.FrameB = editor.ToRows();
            Touch();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border
        {
            Child = editor, BorderBrush = Util.Brush("#3A3A48"), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        });
        var side = new StackPanel { Margin = new Thickness(22, -10, 0, 0) };
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        Body.Children.Add(grid);
        target = side;

        Label("프레임 (두 장을 번갈아 보여 주며 걸어요)");
        side.Children.Add(Chips(new[] { ("A", "프레임 A"), ("B", "프레임 B") }, () => frame, v => frame = v, rebuild: true, touch: false));

        Label("도구");
        side.Children.Add(Chips(new[] { ("Pen", "펜"), ("Eraser", "지우개"), ("Fill", "채우기") }, () => tool,
            v => { tool = v; editor.Tool = v; }, touch: false));
        Hint("마우스 오른쪽 버튼은 언제나 지우개예요.");

        Label("팔레트");
        var swatches = new List<Border>();
        var picker = new ColorPickerButton { Hex = c.Palette[palIndex], Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var white = Brushes.White;
        var dim = Util.Brush("#3A3A48");
        void SyncSwatches()
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                swatches[i].Background = Util.Brush(c.Palette[i]);
                swatches[i].BorderBrush = i == palIndex ? white : dim;
            }
        }
        var palettePanel = new WrapPanel();
        for (int i = 0; i < c.Palette.Count && i < 16; i++)
        {
            int index = i;
            var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(2), Cursor = Cursors.Hand };
            b.MouseLeftButtonDown += (_, _) =>
            {
                palIndex = index;
                editor.Current = index;
                picker.Hex = c.Palette[index];
                SyncSwatches();
            };
            swatches.Add(b);
            palettePanel.Children.Add(b);
        }
        SyncSwatches();
        picker.ColorChanged += h =>
        {
            c.Palette[palIndex] = h;
            editor.SetPalette(c.Palette);
            SyncSwatches();
            Touch();
        };
        side.Children.Add(palettePanel);
        side.Children.Add(picker);
        Hint("칸을 눌러 그릴 색을 고르고, 아래 버튼으로 그 칸의 색을 바꿔요.");

        Label("편집");
        var edit = new WrapPanel();
        edit.Children.Add(SmallButton("모두 지우기", editor.Clear));
        edit.Children.Add(SmallButton("좌우 뒤집기", editor.Mirror));
        edit.Children.Add(SmallButton(frame == "A" ? "A를 B에 복사" : "B를 A에 복사", () =>
        {
            if (frame == "A") c.FrameB = new List<string>(c.FrameA);
            else c.FrameA = new List<string>(c.FrameB);
            Touch();
        }));
        side.Children.Add(edit);

        Label("기본 캐릭터 그림에서 시작하기");
        var load = new WrapPanel();
        foreach (var kind in Sprites.Kinds)
        {
            string k = kind;
            load.Children.Add(SmallButton(Sprites.KindName(k), () =>
            {
                var (c1, c2) = Sprites.DefaultColors(k);
                LoadBuiltInInto(c, k, c1, c2);
                Touch();
                Rebuild();
            }));
        }
        side.Children.Add(load);
        target = Body;
    }
}
