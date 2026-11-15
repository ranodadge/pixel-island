using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace DynamicIsland;

public partial class App : Application
{
    static Mutex? mutex;
    IslandWindow? island;
    EditorWindow? editor;
    WinForms.NotifyIcon? tray;
    MediaService media = null!;
    NotificationService notifications = null!;
    AudioLevelService audio = null!;
    SystemControlService system = null!;

    // ── 멈춤 감시: 화면(UI) 스레드가 2초 넘게 대답이 없으면, 마지막으로 하던 일을 error.log에 남긴다 ──
    static volatile string crumb = "";
    static long heartbeat = Environment.TickCount64;

    /// <summary>오래 걸릴 수 있는 작업을 시작하기 전에 "지금 뭘 하려는지" 적어 둔다.</summary>
    public static void Crumb(string what) => crumb = $"{what} @{DateTime.Now:HH:mm:ss.fff}";

    void StartWatchdog()
    {
        // 우선순위를 낮게 두면 애니메이션으로 바쁠 때 순서가 밀려 "멈춤"으로 잘못 보일 수 있어서 보통 우선순위로 둔다
        var beat = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        beat.Tick += (_, _) => Interlocked.Exchange(ref heartbeat, Environment.TickCount64);
        beat.Start();
        var thread = new Thread(() =>
        {
            bool reported = false;
            while (true)
            {
                Thread.Sleep(500);
                long stalled = Environment.TickCount64 - Interlocked.Read(ref heartbeat);
                if (stalled > 2000 && !reported)
                {
                    reported = true;
                    try
                    {
                        File.AppendAllText(Path.Combine(ThemeStore.Root, "error.log"),
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 화면이 {stalled}ms째 멈춰 있음. 마지막 작업: {crumb}\n\n");
                    }
                    catch { }
                }
                else if (stalled < 1000 && reported)
                {
                    reported = false;
                    try { File.AppendAllText(Path.Combine(ThemeStore.Root, "error.log"), $"[{DateTime.Now:HH:mm:ss}] 화면이 다시 움직임\n\n"); } catch { }
                }
            }
        }) { IsBackground = true, Name = "watchdog" };
        thread.Start();
    }

    public static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(ThemeStore.Root, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 개발·확인용: DynamicIsland.exe --pixelize <사진> <미리보기.png> [색 수]
        // 창을 띄우지 않고 사진 도트화 결과만 PNG로 저장하고 끝낸다.
        if (e.Args.Length >= 3 && e.Args[0] == "--pixelize")
        {
            try
            {
                int colors = e.Args.Length > 3 && int.TryParse(e.Args[3], out int c) ? c : 8;
                Pixelizer.SavePreview(Pixelizer.Convert(e.Args[1], colors, true, true), e.Args[2]);
            }
            catch (Exception ex) { File.WriteAllText(e.Args[2] + ".error.txt", ex.ToString()); }
            Shutdown();
            return;
        }

        // 개발·확인용: DynamicIsland.exe --diag <결과.txt>
        // 창 없이 Wi-Fi/블루투스/소리 출력 목록을 읽어 개수와 상태만 적고 끝낸다 (연결은 건드리지 않는다).
        if (e.Args.Length >= 2 && e.Args[0] == "--diag")
        {
            _ = RunDiagnosticsAsync(e.Args[1]);
            return;
        }
        // 개발·확인용: DynamicIsland.exe --set-output <장치 ID> — 기본 출력 장치만 바꾸고 끝낸다
        if (e.Args.Length >= 2 && e.Args[0] == "--set-output")
        {
            new AudioOutputService().SetDefault(e.Args[1]);
            Shutdown();
            return;
        }

        // 개발용: DynamicIsland.exe --make-icon <폴더> — 앱 아이콘(app.ico)과 미리보기 PNG를 다시 그려 저장한다
        if (e.Args.Length >= 2 && e.Args[0] == "--make-icon")
        {
            try { AppIcon.Save(e.Args[1]); }
            catch (Exception ex) { Log(ex); }
            Shutdown();
            return;
        }
        // 설치·제거: 시작 메뉴/바탕 화면 바로가기와 "설치된 앱" 등록 (Installer.cs)
        // 파일 이름에 Setup이 들어 있으면(DynamicIslandSetup.exe) 더블클릭만으로 설치하는 설치파일로 동작한다
        bool setup = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Contains("Setup", StringComparison.OrdinalIgnoreCase);
        if (e.Args.Contains("--install") || e.Args.Contains("--uninstall") || setup)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool quiet = e.Args.Contains("--quiet");
            if (e.Args.Contains("--uninstall")) Installer.RunUninstall(quiet);
            else if (!setup || quiet || Installer.ConfirmSetup()) Installer.RunInstall(quiet);
            Shutdown();
            return;
        }

        mutex = new Mutex(true, "DynamicIsland.SingleInstance", out bool first);
        if (!first)
        {
            // 이미 켜져 있는데 아이콘을 또 눌렀다면, 켜져 있는 쪽에서 꾸미기 창을 열어 보여 준다
            if (!e.Args.Contains("--startup") && EventWaitHandle.TryOpenExisting(Installer.ShowSignal, out var show))
                using (show) show.Set();
            Shutdown();
            return;
        }
        ListenForSignals();

        DispatcherUnhandledException += (_, ev) =>
        {
            Log(ev.Exception);
            ev.Handled = true;
        };

        ThemeStore.Load();
        CalendarStore.Load();
        StartWatchdog();
        media = new MediaService();
        notifications = new NotificationService();
        audio = new AudioLevelService();
        system = new SystemControlService();

        island = new IslandWindow(media, notifications, audio, system);
        island.OpenEditorRequested += OpenEditor;
        island.Show();

        SetupTray();
        _ = media.StartAsync();
        _ = notifications.StartAsync();

        if (ThemeStore.FirstRun || e.Args.Contains("--editor")) OpenEditor();

        // 윈도우 잠금이 풀리는 순간(Windows Hello 얼굴 인식, PIN 등)을 알아채서 섬에서 연출을 보여 준다
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        if (e.Args.Contains("--startup") && ThemeStore.Settings.UnlockAnimation)
        {
            // 로그인하자마자 자동으로 켜진 경우: 바탕 화면이 뜬 뒤 환영 인사
            var greet = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            greet.Tick += (_, _) =>
            {
                greet.Stop();
                island?.PlayUnlock("환영합니다");
            };
            greet.Start();
        }
    }

    void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason != Microsoft.Win32.SessionSwitchReason.SessionUnlock) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (ThemeStore.Settings.UnlockAnimation) island?.PlayUnlock();
        });
    }

    async Task RunDiagnosticsAsync(string path)
    {
        var sb = new System.Text.StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        static string Mask(string s) => s.Length <= 2 ? "**" : s[..2] + new string('*', Math.Min(6, s.Length - 2));
        try
        {
            var wifi = new WifiService();
            var networks = await wifi.ListAsync(false);
            sb.AppendLine($"wifi: {networks.Count} networks, note='{wifi.Note}' ({clock.ElapsedMilliseconds}ms)");
            foreach (var n in networks.Take(12))
                sb.AppendLine($"  {Mask(n.Ssid)} bars={n.Bars} secured={n.Secured} enterprise={n.Enterprise} known={n.Known} connected={n.Connected}");

            var devices = await new BluetoothService().ListAsync();
            sb.AppendLine($"bluetooth: {devices.Count} paired ({clock.ElapsedMilliseconds}ms)");
            foreach (var d in devices)
                sb.AppendLine($"  {Mask(d.Name)} hasAddress={d.Address.Length == 12} connected={d.Connected} audio={d.IsAudio} signals=[{d.Signals}]");

            // 주소·ID는 구조만 보이도록 긴 16진수 덩어리를 줄여서 적는다
            static string Short(string s) => System.Text.RegularExpressions.Regex.Replace(s, "[0-9A-Fa-f]{8,}", m => m.Value[..2] + ".." + m.Value[^3..]);
            foreach (var d in devices.Where(x => x.Connected))
                sb.AppendLine($"  connected addr={Short(d.Address)} classicId={Short(d.ClassicId)} leId={Short(d.LeId)}");
            foreach (var ep in await Task.Run(BluetoothAudio.FindEndpoints))
                if (ep.FilterId.Contains("bth", StringComparison.OrdinalIgnoreCase) || (ep.Active && !ep.FilterId.Contains("root#") && !ep.FilterId.Contains("intelaudio")))
                    sb.AppendLine($"  endpoint active={ep.Active} filter={Short(ep.FilterId)}");

            var outputs = await Task.Run(() => new AudioOutputService().List());
            sb.AppendLine($"audio outputs: {outputs.Count} ({clock.ElapsedMilliseconds}ms)");
            foreach (var o in outputs) sb.AppendLine($"  {o.Name} default={o.IsDefault} id={o.Id}");

            var hello = await global::Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
            sb.AppendLine($"windows hello: {hello} ({clock.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
        File.WriteAllText(path, sb.ToString());
        Shutdown();
    }

    /// <summary>
    /// 종료할 때 윈도우 오디오/블루투스 구성요소 정리가 길게 늘어지는 일이 있다.
    /// 그동안 프로세스가 창 없이 남아 있으면 다시 실행해도 "이미 실행 중"으로 보고 바로 꺼져 버리므로,
    /// 저장할 것은 이미 다 저장된 뒤인 이 시점에서 2초 안에 확실히 끝낸다.
    /// </summary>
    static void ExitPromptly()
    {
        new Thread(() =>
        {
            Thread.Sleep(2000);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }) { IsBackground = true }.Start();
    }

    /// <summary>다른 프로세스가 보내는 신호를 기다린다: "창 보여 줘"(아이콘을 또 누름), "꺼 줘"(설치·제거).</summary>
    void ListenForSignals()
    {
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ShowSignal);
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.QuitSignal);
        new Thread(() =>
        {
            while (true)
            {
                int which = WaitHandle.WaitAny(new WaitHandle[] { show, quit });
                Dispatcher.BeginInvoke(which == 0 ? new Action(OpenEditor) : new Action(Shutdown));
                if (which == 1) return;
            }
        }) { IsBackground = true, Name = "signals" }.Start();
    }

    void OpenEditor()
    {
        if (island == null) return;
        if (editor == null)
        {
            editor = new EditorWindow(island!, notifications);
            editor.Closed += (_, _) => editor = null;
            editor.Show();
        }
        if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
        editor.Activate();
    }

    void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("테마 꾸미기", null, (_, _) => OpenEditor());

        var themes = new WinForms.ToolStripMenuItem("테마 바꾸기");
        menu.Items.Add(themes);
        menu.Opening += (_, _) =>
        {
            themes.DropDownItems.Clear();
            foreach (var t in ThemeStore.Themes)
            {
                var theme = t;
                var item = new WinForms.ToolStripMenuItem(t.Name) { Checked = t.Id == ThemeStore.Settings.ActiveThemeId };
                item.Click += (_, _) =>
                {
                    ThemeStore.Settings.ActiveThemeId = theme.Id;
                    ThemeStore.SaveSettings();
                    island?.ApplyTheme(theme);
                    editor?.SelectTheme(theme);
                };
                themes.DropDownItems.Add(item);
            }
        };

        menu.Items.Add("알림 테스트", null, (_, _) => island?.ShowTestNotification());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());

        tray = new WinForms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => OpenEditor();
    }

    /// <summary>앱 아이콘(Assets\app.ico)에서 트레이 크기에 맞는 그림을 골라 쓴다.</summary>
    static System.Drawing.Icon MakeTrayIcon()
    {
        try
        {
            var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource != null)
                using (resource.Stream)
                    return new System.Drawing.Icon(resource.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log(ex); }
        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        audio?.Dispose();
        system?.Dispose();
        ExitPromptly();
        base.OnExit(e);
    }
}
