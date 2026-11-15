using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.WiFi;

namespace DynamicIsland;

/// <summary>제어 센터에서 들어가는 목록 화면들: 주변 Wi-Fi(비밀번호 입력 포함), 블루투스 기기, 소리 출력 장치.</summary>
public partial class IslandWindow
{
    enum ListPage { None, Wifi, Bluetooth, Audio }

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    ListPage listPage = ListPage.None;
    bool listBusy, inputMode;
    int listVersion;
    double listHeight = 150, pinIdle;
    WifiNetworkInfo? passwordFor;
    IntPtr previousForeground;

    void WifiMore_Click(object sender, RoutedEventArgs e) => OpenList(ListPage.Wifi);
    void BluetoothMore_Click(object sender, RoutedEventArgs e) => OpenList(ListPage.Bluetooth);
    void AudioMore_Click(object sender, RoutedEventArgs e) => OpenList(ListPage.Audio);
    void ListBack_Click(object sender, RoutedEventArgs e) => CloseList();
    void ListRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadListAsync(true);

    void ListSettings_Click(object sender, RoutedEventArgs e) => OpenUri(listPage switch
    {
        ListPage.Wifi => "ms-settings:network-wifi",
        ListPage.Bluetooth => "ms-settings:bluetooth",
        _ => "ms-settings:sound",
    });

    void OpenList(ListPage page)
    {
        CloseCalendar();
        listPage = page;
        showControl = expanded = true;
        HidePassword();
        ListItems.Children.Clear();
        SetNote("");
        ListTitle.Text = page switch
        {
            ListPage.Wifi => "Wi-Fi",
            ListPage.Bluetooth => "Bluetooth",
            _ => "소리 출력",
        };
        ListScroll.ScrollToTop();
        MeasureList();
        _ = LoadListAsync(true);
    }

    void CloseList()
    {
        if (listPage == ListPage.None) return;
        listPage = ListPage.None;
        listVersion++;
        listBusy = false;
        HidePassword();
    }

    /// <summary>내용이 바뀔 때마다 목록 화면이 차지할 높이를 다시 잰다 (섬이 그 크기로 늘어난다).</summary>
    void MeasureList()
    {
        // 줄을 막 추가한 직후에는 아직 크기가 계산되지 않았으므로, 여기서는 다시 재 달라고만 해 두고
        // 실제 높이는 매 프레임(UpdateListHeight) 레이아웃이 끝난 값을 읽는다.
        ListBody.InvalidateMeasure();
    }

    void UpdateListHeight()
    {
        if (ListLayer.Visibility != Visibility.Visible) return;
        double h = ListBody.DesiredSize.Height;
        if (h > 0) listHeight = Math.Clamp(h, 90, 360);
    }

    double bluetoothRefresh = 3;
    bool bluetoothLoading;
    string bluetoothSignature = "";

    /// <summary>목록 화면이 떠 있는 동안 매 프레임 불린다.</summary>
    void TickList(double dt)
    {
        UpdateListHeight();

        // 블루투스 목록은 열어 둔 동안 3초마다 조용히 다시 읽어서, 기기가 붙거나 떨어지면 바로 반영한다
        if (listPage != ListPage.Bluetooth || listBusy) return;
        bluetoothRefresh -= dt;
        if (bluetoothRefresh > 0) return;
        bluetoothRefresh = 3;
        _ = RefreshBluetoothQuietlyAsync();
    }

    static string Signature(List<BluetoothDeviceInfo> devices) =>
        string.Join("|", devices.Select(d => $"{d.Name}:{d.Connected}:{d.IsAudio}"));

    async Task RefreshBluetoothQuietlyAsync()
    {
        if (bluetoothLoading) return;
        bluetoothLoading = true;
        int v = listVersion;
        try
        {
            var devices = await bluetooth.ListAsync();
            if (v != listVersion || listPage != ListPage.Bluetooth || listBusy) return;
            if (Signature(devices) != bluetoothSignature) ShowBluetooth(devices); // 달라졌을 때만 다시 그린다
        }
        catch (Exception ex) { App.Log(ex); }
        finally { bluetoothLoading = false; }
    }

    void SetNote(string text)
    {
        ListNote.Text = text;
        ListNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MeasureList();
    }

    async Task LoadListAsync(bool rescan)
    {
        int v = ++listVersion;
        var page = listPage;
        try
        {
            switch (page)
            {
                case ListPage.Wifi:
                {
                    ListSub.Text = "불러오는 중…";
                    var quick = await wifi.ListAsync(false);
                    if (v != listVersion) return;
                    ShowWifi(quick);
                    if (rescan)
                    {
                        ListSub.Text = "주변 검색 중…";
                        var fresh = await wifi.ListAsync(true);
                        if (v != listVersion) return;
                        ShowWifi(fresh);
                    }
                    break;
                }
                case ListPage.Bluetooth:
                {
                    ListSub.Text = "불러오는 중…";
                    var devices = await bluetooth.ListAsync();
                    if (v != listVersion) return;
                    ShowBluetooth(devices);
                    break;
                }
                case ListPage.Audio:
                {
                    // 윈도우 오디오 서비스가 바쁠 때(재생 중 전환 직후 등) 늦게 대답할 수 있으므로 화면 스레드에서 부르지 않는다
                    var devices = await Task.Run(outputs.List);
                    if (v != listVersion) return;
                    ShowAudio(devices);
                    break;
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        if (v == listVersion && !listBusy) ListSub.Text = "";
    }

    // ───────────────────────── 줄 하나 ─────────────────────────

    void AddRow(string glyph, string title, string? status, bool check, bool locked, Action? click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock Text(string text, double size, string brush, int column)
        {
            var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
            return t;
        }

        Text(glyph, 15, "TextBrush", 0).FontFamily = IconFont;
        Text(title, 12.5, "TextBrush", 1).Margin = new Thickness(8, 0, 8, 0);
        if (!string.IsNullOrEmpty(status)) Text(status, 10.5, "SubBrush", 2).Margin = new Thickness(0, 0, 8, 0);
        if (check)
        {
            var c = Text("", 12, "AccentBrush", 3);
            c.FontFamily = IconFont;
        }
        else if (locked)
        {
            var l = Text("", 10.5, "SubBrush", 3);
            l.FontFamily = IconFont;
        }

        var row = new Border
        {
            Height = 38, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 0, 11, 0),
            Background = Brushes.Transparent, Child = grid,
            Cursor = click != null ? Cursors.Hand : Cursors.Arrow,
        };
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (click != null) row.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        ListItems.Children.Add(row);
    }

    // ───────────────────────── Wi-Fi ─────────────────────────

    void ShowWifi(List<WifiNetworkInfo> networks)
    {
        ListItems.Children.Clear();
        foreach (var n in networks)
        {
            var net = n;
            string glyph = n.Bars switch { <= 1 => "", 2 => "", 3 => "", _ => "" };
            string? status = n.Connected ? "연결됨" : n.Enterprise ? "회사·학교용" : n.Known ? "저장됨" : null;
            AddRow(glyph, n.Ssid, status, n.Connected, n.Secured || n.Enterprise, () => OnWifiClick(net));
            ((FrameworkElement)ListItems.Children[^1]).Tag = net;
        }
        if (wifi.Note.Length > 0 || ListNote.Text.Length == 0) SetNote(wifi.Note);
        MeasureList();
    }

    async void OnWifiClick(WifiNetworkInfo n)
    {
        if (listBusy) return;
        if (n.Connected)
        {
            HidePassword();
            SetNote($"지금 '{n.Ssid}'에 연결되어 있어요.");
            return;
        }
        if (n.Enterprise)
        {
            HidePassword();
            SetNote("회사·학교용(802.1X) 네트워크는 아이디 인증이 필요해서 윈도우 설정에서 연결해야 해요. 오른쪽 위 톱니바퀴를 눌러 주세요.");
            return;
        }
        if (n.Secured && !n.Known)
        {
            SetNote("");
            ShowPassword(n, $"'{n.Ssid}' 비밀번호");
            return;
        }
        HidePassword();
        SetNote("");
        await ConnectWifiAsync(n, null);
    }

    async Task ConnectWifiAsync(WifiNetworkInfo n, string? password)
    {
        listBusy = true;
        PasswordGo.IsEnabled = false;
        ListSub.Text = "연결 중…";
        var status = await wifi.ConnectAsync(n, password);
        listBusy = false;
        PasswordGo.IsEnabled = true;
        ListSub.Text = "";
        if (listPage != ListPage.Wifi) return;

        if (status == WiFiConnectionStatus.Success)
        {
            HidePassword();
            SetNote($"'{n.Ssid}'에 연결했어요.");
            _ = system.RefreshAsync();
            await LoadListAsync(false);
        }
        else if (n.Secured)
        {
            // 저장된 비밀번호가 바뀌었거나 방금 입력한 게 틀린 경우 — 다시 입력받는다
            ShowPassword(n, password == null ? $"'{n.Ssid}' 비밀번호" : "비밀번호가 맞지 않거나 연결하지 못했어요. 다시 입력해 주세요");
        }
        else SetNote($"'{n.Ssid}'에 연결하지 못했어요. 신호가 약하거나 로그인 페이지가 필요한 네트워크일 수 있어요.");
    }

    // ───────────────────────── 비밀번호 입력 ─────────────────────────

    void ShowPassword(WifiNetworkInfo n, string label)
    {
        passwordFor = n;
        PasswordLabel.Text = label;
        PasswordInput.Clear();
        PasswordPanel.Visibility = Visibility.Visible;
        pinIdle = 0;
        MeasureList();
        SetInputMode(true);
    }

    void HidePassword()
    {
        passwordFor = null;
        PasswordInput.Clear();
        if (PasswordPanel.Visibility == Visibility.Visible)
        {
            PasswordPanel.Visibility = Visibility.Collapsed;
            MeasureList();
        }
        SetInputMode(false);
    }

    /// <summary>
    /// 섬은 평소에 포커스를 받지 않는 창이라 글자를 칠 수 없다.
    /// 비밀번호를 입력하는 동안에만 잠깐 보통 창처럼 포커스를 받게 한다.
    /// </summary>
    void SetInputMode(bool on, UIElement? focus = null)
    {
        if (inputMode != on)
        {
            inputMode = on;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = on ? ex & ~Native.WS_EX_NOACTIVATE : ex | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

            if (on) previousForeground = Native.GetForegroundWindow();
            else if (IsActive && previousForeground != IntPtr.Zero && previousForeground != hwnd)
                Native.SetForegroundWindow(previousForeground); // 입력이 끝나면 쓰던 창으로 포커스를 돌려준다
        }
        if (!on) return;
        Activate();
        Native.SetForegroundWindow(hwnd);
        var target = focus ?? PasswordInput; // 비밀번호 칸 또는 캘린더 메모 칸
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            target.Focus();
            Keyboard.Focus(target);
        });
    }

    void PasswordInput_MouseDown(object sender, MouseButtonEventArgs e) => SetInputMode(true);

    void PasswordInput_Changed(object sender, RoutedEventArgs e) =>
        PasswordPlaceholder.Visibility = PasswordInput.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitPassword();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HidePassword();
            e.Handled = true;
        }
    }

    void PasswordGo_Click(object sender, RoutedEventArgs e) => SubmitPassword();

    async void SubmitPassword()
    {
        if (listBusy || passwordFor == null) return;
        string password = PasswordInput.Password;
        if (password.Length < 8)
        {
            PasswordLabel.Text = "Wi-Fi 비밀번호는 8자 이상이에요";
            return;
        }
        var target = passwordFor;
        PasswordInput.Clear(); // 입력한 비밀번호는 윈도우에 넘긴 뒤 화면에 남겨 두지 않는다
        await ConnectWifiAsync(target, password);
    }

    // ───────────────────────── 블루투스 ─────────────────────────

    void ShowBluetooth(List<BluetoothDeviceInfo> devices)
    {
        bluetoothSignature = Signature(devices);
        bluetoothRefresh = 3;
        ListItems.Children.Clear();
        foreach (var d in devices)
        {
            var device = d;
            string status = d.Connected ? "연결됨" : d.IsAudio ? "눌러서 연결" : "연결 안 됨";
            AddRow(d.IsAudio ? "" : "", d.Name, status, d.Connected, false, () => OnBluetoothClick(device));
        }
        AddRow("", "새 기기 추가…", null, false, false, () => OpenUri("ms-settings:bluetooth"));
        if (!system.BluetoothOn) SetNote("블루투스가 꺼져 있어요. 제어 센터에서 Bluetooth 타일을 눌러 켜 주세요.");
        else if (devices.Count == 0) SetNote("페어링된 기기가 없어요. '새 기기 추가…'로 윈도우에서 한 번 페어링하면 여기에 나타나요.");
        else if (ListNote.Text.Length > 0 && !listBusy) SetNote("");
        MeasureList();
    }

    async void OnBluetoothClick(BluetoothDeviceInfo d)
    {
        if (listBusy) return;
        if (!d.IsAudio)
        {
            SetNote(d.Connected
                ? $"'{d.Name}'은(는) 연결되어 있어요."
                : $"'{d.Name}'은(는) 기기 전원을 켜면 스스로 연결돼요. 여기서 직접 연결·해제할 수 있는 건 헤드폰·스피커 같은 소리 기기예요.");
            return;
        }

        listBusy = true;
        bool connect = !d.Connected;
        ListSub.Text = connect ? "연결 중…" : "연결 끊는 중…";
        SetNote("");
        bool sent = await bluetooth.SetConnectedAsync(d, connect);
        if (sent)
        {
            // 실제로 붙거나 끊기는 데 몇 초 걸리므로 상태가 바뀔 때까지 잠깐 지켜본다
            for (int i = 0; i < 9 && listPage == ListPage.Bluetooth; i++)
            {
                await Task.Delay(1000);
                var now = (await bluetooth.ListAsync()).FirstOrDefault(x => x.Address == d.Address);
                if (now != null && now.Connected == connect) break;
            }
        }
        listBusy = false;
        if (listPage != ListPage.Bluetooth) return;
        ListSub.Text = "";
        await LoadListAsync(false);
        if (!sent) SetNote($"'{d.Name}'에 연결 요청을 보내지 못했어요. 기기가 켜져 있고 가까이 있는지 확인해 주세요.");
        _ = system.RefreshAsync();
    }

    // ───────────────────────── 소리 출력 ─────────────────────────

    void ShowAudio(List<AudioOutputInfo> devices)
    {
        ListItems.Children.Clear();
        foreach (var d in devices)
        {
            var device = d;
            AddRow("", d.Name, d.IsDefault ? "사용 중" : null, d.IsDefault, false, () => SwitchOutput(device));
        }
        if (devices.Count == 0) SetNote("사용할 수 있는 출력 장치가 없어요.");
        MeasureList();
    }

    async void SwitchOutput(AudioOutputInfo device)
    {
        if (device.IsDefault || listBusy) return;
        listBusy = true;
        ListSub.Text = "바꾸는 중…";
        SetNote("");
        App.Crumb("소리 출력 전환");
        bool ok = false;
        List<AudioOutputInfo> devices = new();
        try
        {
            // 재생 중인 소리를 새 장치로 옮기는 동안 윈도우가 몇 초 걸릴 수 있다 — 화면은 멈추지 않게 백그라운드에서
            ok = await Task.Run(() => outputs.SetDefault(device.Id));
            devices = await Task.Run(outputs.List);
        }
        catch (Exception ex) { App.Log(ex); }
        listBusy = false;
        if (listPage != ListPage.Audio) return;
        ListSub.Text = "";
        if (devices.Count > 0) ShowAudio(devices);
        if (!ok) SetNote("출력 장치를 바꾸지 못했어요.");
        system.RefreshVolume(); // 볼륨 슬라이더가 새 장치를 따라가도록
    }
}
