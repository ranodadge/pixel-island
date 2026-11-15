using System.Collections.Concurrent;
using System.Management;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace DynamicIsland;

/// <summary>제어 센터가 다루는 것들: 볼륨, Wi-Fi/블루투스 켜고 끄기, 화면 밝기.</summary>
public sealed class SystemControlService : IDisposable
{
    readonly Dispatcher ui = Dispatcher.CurrentDispatcher;

    // 윈도우 오디오 서비스는 출력 장치를 바꾸는 중이거나 재생 중인 소리를 옮길 때 대답이 늦어질 수 있다.
    // 그래서 볼륨 관련 호출은 전부 이 전용 스레드에서만 하고, 화면 스레드는 결과만 받아 쓴다.
    readonly BlockingCollection<Action> audioQueue = new();
    readonly object volumeGate = new();
    MMDeviceEnumerator? enumerator;   // 오디오 스레드 전용
    MMDevice? device;                 // 오디오 스레드 전용
    double pendingVolume = -1;
    bool volumeQueued;
    long volumeSetAt;

    Radio? wifi, bluetooth;
    bool refreshing, brightnessBusy;
    int pendingBrightness = -1;
    long radiosTriedAt = -100000, brightnessSetAt;

    public event Action? Changed;

    public bool VolumeAvailable { get; private set; }
    public double Volume { get; private set; }
    public bool Muted { get; private set; }

    public bool WifiAvailable => wifi != null;
    public bool WifiOn => wifi?.State == RadioState.On;
    /// <summary>지금 인터넷에 연결된 네트워크 이름과, 그게 Wi-Fi인지.</summary>
    public string NetworkName { get; private set; } = "";
    public bool NetworkIsWifi { get; private set; }

    public bool BluetoothAvailable => bluetooth != null;
    public bool BluetoothOn => bluetooth?.State == RadioState.On;
    public string BluetoothDevices { get; private set; } = "";

    /// <summary>윈도우가 무선 장치 제어를 막았을 때 보여 줄 안내.</summary>
    public string RadioNote { get; private set; } = "";

    public bool BrightnessAvailable { get; private set; }
    public double Brightness { get; private set; }

    public SystemControlService()
    {
        var thread = new Thread(() =>
        {
            foreach (var work in audioQueue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { App.Log(ex); }
            }
        }) { IsBackground = true, Name = "audio-control" };
        thread.Start();
    }

    void OnAudioThread(Action work)
    {
        if (!audioQueue.IsAddingCompleted) audioQueue.Add(work);
    }

    // ───────────────────────── 볼륨 (아래 Attach/Detach는 오디오 스레드에서만) ─────────────────────────

    void AttachVolume()
    {
        try
        {
            enumerator ??= new MMDeviceEnumerator();
            var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device != null && device.ID == d.ID) d.Dispose();
            else
            {
                DetachVolume();
                device = d;
                d.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
            Publish(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute, true);
        }
        catch
        {
            DetachVolume();
            Publish(0, false, false);
        }
    }

    void DetachVolume()
    {
        var d = device;
        device = null;
        if (d == null) return;
        try { d.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
        try { d.Dispose(); } catch { }
    }

    void Publish(double volume, bool muted, bool available) => ui.BeginInvoke(() =>
    {
        // 방금 사용자가 슬라이더로 정한 값은, 늦게 도착한 예전 값으로 덮어쓰지 않는다
        if (Environment.TickCount64 - volumeSetAt > 500)
        {
            Volume = volume;
            Muted = muted;
        }
        VolumeAvailable = available;
        Changed?.Invoke();
    });

    // 키보드 볼륨 키나 다른 앱이 바꾼 볼륨도 따라간다 (윈도우가 부르는 스레드에서 들어온다)
    void OnVolume(AudioVolumeNotificationData data) => Publish(data.MasterVolume, data.Muted, true);

    public void SetVolume(double v)
    {
        v = Math.Clamp(v, 0, 1);
        Volume = v;
        if (v > 0) Muted = false;
        volumeSetAt = Environment.TickCount64;
        lock (volumeGate)
        {
            pendingVolume = v;
            if (volumeQueued) return; // 끄는 동안 값이 쏟아져도 가장 최근 값 하나만 적용한다
            volumeQueued = true;
        }
        OnAudioThread(() =>
        {
            double value;
            lock (volumeGate)
            {
                value = pendingVolume;
                volumeQueued = false;
            }
            try
            {
                if (device == null) AttachVolume();
                if (device == null) return;
                var vol = device.AudioEndpointVolume;
                vol.MasterVolumeLevelScalar = (float)value;
                if (value > 0 && vol.Mute) vol.Mute = false;
            }
            catch { AttachVolume(); }
        });
    }

    public void ToggleMute()
    {
        bool mute = !Muted;
        Muted = mute;
        volumeSetAt = Environment.TickCount64;
        Changed?.Invoke();
        OnAudioThread(() =>
        {
            try
            {
                if (device == null) AttachVolume();
                if (device != null) device.AudioEndpointVolume.Mute = mute;
            }
            catch { AttachVolume(); }
        });
    }

    /// <summary>기본 출력 장치가 바뀌었을 수 있을 때 볼륨 대상을 다시 잡는다. 기다리지 않고 바로 돌아온다.</summary>
    public void RefreshVolume() => OnAudioThread(AttachVolume);

    // ───────────────────────── Wi-Fi / 블루투스 ─────────────────────────

    async Task LoadRadiosAsync()
    {
        radiosTriedAt = Environment.TickCount64;
        try
        {
            var access = await Radio.RequestAccessAsync();
            RadioNote = access == RadioAccessStatus.Allowed ? "" : "윈도우 설정 > 개인 정보 > 무선 장치에서 앱의 제어를 허용해 주세요";
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind == RadioKind.WiFi && wifi == null) { wifi = r; r.StateChanged += OnRadioChanged; }
                else if (r.Kind == RadioKind.Bluetooth && bluetooth == null) { bluetooth = r; r.StateChanged += OnRadioChanged; }
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void OnRadioChanged(Radio sender, object args) => ui.BeginInvoke(() => { _ = RefreshAsync(); });

    public Task SetWifiAsync(bool on) => SetRadioAsync(wifi, on);
    public Task SetBluetoothAsync(bool on) => SetRadioAsync(bluetooth, on);

    async Task SetRadioAsync(Radio? radio, bool on)
    {
        if (radio == null) return;
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            RadioNote = result == RadioAccessStatus.Allowed ? "" : "윈도우가 무선 장치 제어를 막았어요 (설정 > 개인 정보 > 무선 장치)";
        }
        catch (Exception ex) { App.Log(ex); }
        Changed?.Invoke();
        await RefreshAsync();
    }

    static (string name, bool wifi) ReadNetwork()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p == null) return ("", false);
            if (p.IsWlanConnectionProfile)
            {
                try
                {
                    var ssid = p.WlanConnectionProfileDetails?.GetConnectedSsid();
                    if (!string.IsNullOrEmpty(ssid)) return (ssid, true);
                }
                catch { }
                return (p.ProfileName ?? "", true);
            }
            return (p.ProfileName ?? "", false);
        }
        catch { return ("", false); }
    }

    static async Task<string> ReadBluetoothAsync()
    {
        var names = new List<string>();
        var selectors = new[]
        {
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
        };
        foreach (var selector in selectors)
        {
            try
            {
                foreach (var d in await DeviceInformation.FindAllAsync(selector))
                    if (!string.IsNullOrWhiteSpace(d.Name) && !names.Contains(d.Name)) names.Add(d.Name);
            }
            catch { }
        }
        return names.Count switch { 0 => "", 1 => names[0], _ => $"{names[0]} 외 {names.Count - 1}대" };
    }

    // ───────────────────────── 화면 밝기 (노트북 내장 화면) ─────────────────────────

    static double? ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject o in searcher.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]) / 100.0;
        }
        catch { }
        return null;
    }

    static void WriteBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            foreach (ManagementObject o in searcher.Get())
                using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
        }
        catch { }
    }

    public void SetBrightness(double v)
    {
        Brightness = Math.Clamp(v, 0, 1);
        pendingBrightness = (int)Math.Round(Brightness * 100);
        brightnessSetAt = Environment.TickCount64;
        if (brightnessBusy) return;
        brightnessBusy = true;
        // 드래그하는 동안 값이 쏟아지므로, 가장 최근 값만 차례로 적용한다
        Task.Run(() =>
        {
            int value;
            while ((value = Interlocked.Exchange(ref pendingBrightness, -1)) >= 0) WriteBrightness(value);
            brightnessBusy = false;
        });
    }

    // ───────────────────────── 상태 새로 읽기 ─────────────────────────

    public async Task RefreshAsync()
    {
        RefreshVolume();
        if (refreshing) return;
        refreshing = true;
        try
        {
            if ((wifi == null || bluetooth == null) && Environment.TickCount64 - radiosTriedAt > 15000) await LoadRadiosAsync();

            (NetworkName, NetworkIsWifi) = await Task.Run(ReadNetwork);
            BluetoothDevices = BluetoothOn ? await ReadBluetoothAsync() : "";

            var b = await Task.Run(ReadBrightness);
            BrightnessAvailable = b != null;
            // 방금 사용자가 움직인 값은 덮어쓰지 않는다
            if (b != null && Environment.TickCount64 - brightnessSetAt > 1500) Brightness = b.Value;
        }
        catch (Exception ex) { App.Log(ex); }
        finally { refreshing = false; }
        Changed?.Invoke();
    }

    public void Dispose() => audioQueue.CompleteAdding();
}
