using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace DynamicIsland;

// ═════════════════════════ Wi-Fi ═════════════════════════

public sealed class WifiNetworkInfo
{
    public string Ssid = "";
    public int Bars;
    /// <summary>비밀번호(PSK)가 필요한 네트워크.</summary>
    public bool Secured;
    /// <summary>회사/학교용 802.1X — 아이디와 인증서가 필요해서 윈도우 설정에서 연결해야 한다.</summary>
    public bool Enterprise;
    public bool Connected;
    /// <summary>이 PC에 저장된 적이 있어 비밀번호 없이 바로 연결을 시도할 수 있는 네트워크.</summary>
    public bool Known;
    public WiFiAvailableNetwork Network = null!;
}

/// <summary>주변 Wi-Fi 찾기와 연결. 비밀번호는 윈도우에 그대로 넘기기만 하고 앱에는 저장하지 않는다.</summary>
public sealed class WifiService
{
    WiFiAdapter? adapter;

    /// <summary>목록을 못 가져왔을 때 사용자에게 보여 줄 이유.</summary>
    public string Note { get; private set; } = "";

    async Task<bool> EnsureAdapterAsync()
    {
        if (adapter != null) return true;
        try
        {
            var access = await WiFiAdapter.RequestAccessAsync();
            if (access != WiFiAccessStatus.Allowed)
            {
                Note = "윈도우가 Wi-Fi 목록 접근을 막았어요. 설정 > 개인 정보 > 위치에서 위치 서비스와 데스크톱 앱 접근을 켜 주세요.";
                return false;
            }
            adapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
            if (adapter == null) Note = "이 PC에서 Wi-Fi 어댑터를 찾지 못했어요.";
            return adapter != null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Note = "Wi-Fi 목록을 읽을 수 없어요. 위치 서비스가 꺼져 있으면 켜 주세요.";
            return false;
        }
    }

    /// <param name="rescan">true면 주변을 새로 검색하고(몇 초 걸림), false면 윈도우가 마지막으로 본 목록을 바로 돌려준다.</param>
    public async Task<List<WifiNetworkInfo>> ListAsync(bool rescan)
    {
        var list = new List<WifiNetworkInfo>();
        Note = "";
        if (!await EnsureAdapterAsync()) return list;
        try
        {
            if (rescan)
            {
                try { await adapter!.ScanAsync(); }
                catch (Exception ex) { App.Log(ex); }
            }

            string current = "";
            try
            {
                var profile = await adapter!.NetworkAdapter.GetConnectedProfileAsync();
                if (profile is { IsWlanConnectionProfile: true })
                {
                    try { current = profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? ""; } catch { }
                    if (current.Length == 0) current = profile.ProfileName ?? "";
                }
            }
            catch { }

            var known = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var p in await Task.Run(() => NetworkInformation.GetConnectionProfiles()))
                    if (p.IsWlanConnectionProfile && !string.IsNullOrEmpty(p.ProfileName)) known.Add(p.ProfileName);
            }
            catch { }

            // 같은 이름(SSID)의 공유기 여러 대는 가장 신호가 센 것 하나로 합친다
            var bySsid = new Dictionary<string, WifiNetworkInfo>(StringComparer.Ordinal);
            foreach (var n in adapter!.NetworkReport.AvailableNetworks)
            {
                if (string.IsNullOrWhiteSpace(n.Ssid)) continue;
                if (bySsid.TryGetValue(n.Ssid, out var seen) && seen.Bars >= n.SignalBars) continue;
                var auth = n.SecuritySettings.NetworkAuthenticationType;
                bool enterprise = auth is NetworkAuthenticationType.Wpa or NetworkAuthenticationType.Rsna
                    or NetworkAuthenticationType.Wpa3 or (NetworkAuthenticationType)13; // 13 = WPA3-Enterprise (새 윈도우에만 있는 값)
                bool open = auth is NetworkAuthenticationType.None or NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.Owe;
                bySsid[n.Ssid] = new WifiNetworkInfo
                {
                    Ssid = n.Ssid,
                    Bars = n.SignalBars,
                    Enterprise = enterprise,
                    Secured = !open && !enterprise,
                    Connected = n.Ssid == current,
                    Known = known.Contains(n.Ssid),
                    Network = n,
                };
            }
            list = bySsid.Values.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars).ThenBy(n => n.Ssid).ToList();
            if (list.Count == 0) Note = "주변에서 찾은 네트워크가 없어요. Wi-Fi가 꺼져 있으면 켠 뒤 새로 고쳐 주세요.";
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Note = "Wi-Fi 목록을 읽는 중 문제가 생겼어요.";
        }
        return list;
    }

    /// <param name="password">null이면 저장된 정보(또는 비밀번호 없는 네트워크)로 연결을 시도한다.</param>
    public async Task<WiFiConnectionStatus> ConnectAsync(WifiNetworkInfo network, string? password)
    {
        if (adapter == null) return WiFiConnectionStatus.UnspecifiedFailure;
        try
        {
            WiFiConnectionResult result;
            if (password == null) result = await adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic);
            else
            {
                var credential = new PasswordCredential { Password = password };
                result = await adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic, credential);
            }
            return result.ConnectionStatus;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return WiFiConnectionStatus.UnspecifiedFailure;
        }
    }

    public void Disconnect()
    {
        try { adapter?.Disconnect(); } catch (Exception ex) { App.Log(ex); }
    }
}

// ═════════════════════════ 블루투스 ═════════════════════════

public sealed class BluetoothDeviceInfo
{
    public string Id = "", Name = "";
    /// <summary>일반(클래식)과 저전력(LE) 두 가지로 모두 등록된 기기는 ID가 둘이다.</summary>
    public string ClassicId = "", LeId = "";
    /// <summary>콜론 없는 대문자 12자리 주소 (예: A1B2C3D4E5F6).</summary>
    public string Address = "";
    public bool Connected;
    /// <summary>연결됨으로 판단한 근거들 (확인용).</summary>
    public string Signals = "";
    /// <summary>헤드폰·스피커처럼 소리 장치로 잡혀 있어서 앱에서 연결/해제를 시킬 수 있는 기기.</summary>
    public bool IsAudio;
    /// <summary>이 기기에 딸린 소리 끝점들의 ID.</summary>
    public List<string> Endpoints = new();
}

/// <summary>
/// 페어링된 블루투스 기기 목록과 연결/해제.
/// 윈도우는 "이 기기에 연결"을 공개 API로 주지 않아서, 소리 장치는 오디오 드라이버에 재연결을 요청하는 방식으로 붙인다.
/// 키보드·마우스 같은 기기는 켜면 스스로 붙으므로 상태만 보여 준다.
/// </summary>
public sealed class BluetoothService
{
    static readonly string[] Props = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress" };

    static string AddressOf(DeviceInformation d) =>
        d.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) && a is string s
            ? s.Replace(":", "").ToUpperInvariant() : "";

    public async Task<List<BluetoothDeviceInfo>> ListAsync()
    {
        // 1. 페어링된 기기 (일반 + 저전력)
        var byAddress = new Dictionary<string, BluetoothDeviceInfo>();
        var paired = new (string selector, bool le)[]
        {
            (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false),
            (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true),
        };
        foreach (var (selector, le) in paired)
        {
            try
            {
                foreach (var d in await DeviceInformation.FindAllAsync(selector, Props))
                {
                    if (string.IsNullOrWhiteSpace(d.Name)) continue;
                    string address = AddressOf(d);
                    string key = address.Length > 0 ? address : d.Id;
                    if (!byAddress.TryGetValue(key, out var info))
                        byAddress[key] = info = new BluetoothDeviceInfo { Id = d.Id, Name = d.Name, Address = address };
                    if (le) info.LeId = d.Id; else info.ClassicId = d.Id;
                    if (d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true) info.Signals += "aep ";
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }
        var list = byAddress.Values.ToList();

        // 2. "지금 연결됨" 판단 — 윈도우가 기기 종류에 따라 알려 주는 경로가 달라서 여러 신호를 함께 본다
        //    (a) 연결된 기기만 찾는 질의
        var connectedSelectors = new[]
        {
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
        };
        foreach (var selector in connectedSelectors)
        {
            try
            {
                foreach (var d in await DeviceInformation.FindAllAsync(selector, Props))
                {
                    string address = AddressOf(d);
                    var hit = list.FirstOrDefault(x => x.ClassicId == d.Id || x.LeId == d.Id || (address.Length > 0 && x.Address == address));
                    if (hit != null) hit.Signals += "query ";
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        //    (b) 기기 하나하나에 직접 물어보기
        await Task.WhenAll(list.Select(async d =>
        {
            try
            {
                if (d.ClassicId.Length > 0)
                {
                    using var dev = await BluetoothDevice.FromIdAsync(d.ClassicId);
                    if (dev?.ConnectionStatus == BluetoothConnectionStatus.Connected) d.Signals += "device ";
                }
                if (d.LeId.Length > 0)
                {
                    using var dev = await BluetoothLEDevice.FromIdAsync(d.LeId);
                    if (dev?.ConnectionStatus == BluetoothConnectionStatus.Connected) d.Signals += "le ";
                }
            }
            catch { }
        }));

        //    (c) 소리 기기라면: 그 기기의 소리 끝점이 지금 살아 있는지
        try
        {
            var endpoints = await Task.Run(BluetoothAudio.FindEndpoints);
            var outputs = await Task.Run(() => new AudioOutputService().List());
            foreach (var d in list)
            {
                // 끝점이 물린 드라이버 ID에 기기 주소가 들어 있으면 그 기기의 소리 끝점이다
                foreach (var e in endpoints)
                {
                    if (d.Address.Length != 12 || !e.FilterId.Contains(d.Address, StringComparison.OrdinalIgnoreCase)) continue;
                    d.IsAudio = true;
                    if (!d.Endpoints.Contains(e.Id)) d.Endpoints.Add(e.Id);
                    if (e.Active && !d.Signals.Contains("sound")) d.Signals += "sound ";
                }
                // 주소로는 안 잡히는 방식으로 붙는 기기도 있어서, 지금 살아 있는 출력 장치 이름에 기기 이름이 들어 있는지도 본다
                // (예: "헤드폰(○○의 AirPods Pro - Find My)")
                if (d.Name.Length < 3) continue;
                foreach (var o in outputs)
                {
                    if (!o.Name.Contains(d.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    d.IsAudio = true;
                    if (!d.Endpoints.Contains(o.Id)) d.Endpoints.Add(o.Id);
                    if (!d.Signals.Contains("sound")) d.Signals += "sound ";
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }

        foreach (var d in list) d.Connected = d.Signals.Length > 0;
        return list.OrderByDescending(d => d.Connected).ThenByDescending(d => d.IsAudio).ThenBy(d => d.Name).ToList();
    }

    /// <summary>소리 장치를 연결하거나 끊는다. 요청을 보냈으면 true (실제로 붙는 데는 몇 초 걸린다).</summary>
    public Task<bool> SetConnectedAsync(BluetoothDeviceInfo device, bool connect) =>
        Task.Run(() =>
        {
            try { return BluetoothAudio.Request(device.Address, device.Endpoints, connect); }
            catch (Exception ex) { App.Log(ex); return false; }
        });
}

/// <summary>블루투스 소리 장치의 오디오 드라이버에 "다시 연결"/"연결 끊기"를 요청한다.</summary>
static class BluetoothAudio
{
    const int Render = 0, Capture = 1, Active = 1, Unplugged = 8, ClsCtxAll = 23;
    static readonly Guid KsPropSetBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const int OneShotReconnect = 0, OneShotDisconnect = 1, KsPropertyTypeGet = 1;

    /// <summary>소리 끝점 하나: 끝점 ID, 그 끝점이 물려 있는 드라이버(KS 필터) ID, 지금 살아 있는지.</summary>
    public readonly record struct Endpoint(string Id, string FilterId, bool Active);

    /// <summary>
    /// 연결돼 있거나 연결이 끊긴 채 남아 있는 모든 소리 끝점.
    /// 블루투스 기기의 끝점은 드라이버 ID 안에 그 기기의 주소가 들어 있다.
    /// (반드시 백그라운드 스레드에서 부를 것 — 화면 스레드에서 부르면 응답 없이 멈춘다)
    /// </summary>
    public static List<Endpoint> FindEndpoints()
    {
        var list = new List<Endpoint>();
        Visit((e, _) => list.Add(e));
        return list;
    }

    /// <summary>기기 주소나 끝점 ID로 그 기기의 드라이버를 찾아 재연결/해제를 요청한다.</summary>
    public static bool Request(string address, ICollection<string> endpointIds, bool connect)
    {
        int sent = 0;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit((e, enumerator) =>
        {
            bool mine = (address.Length == 12 && e.FilterId.Contains(address, StringComparison.OrdinalIgnoreCase)) || endpointIds.Contains(e.Id);
            if (!mine || !done.Add(e.FilterId)) return;

            if (enumerator.GetDevice(e.FilterId, out var filter) != 0) return;
            var ksId = typeof(IKsControl).GUID;
            if (filter.Activate(ref ksId, ClsCtxAll, IntPtr.Zero, out var ksObj) != 0) return;
            var property = new KsProperty
            {
                Set = KsPropSetBtAudio,
                Id = connect ? OneShotReconnect : OneShotDisconnect,
                Flags = KsPropertyTypeGet,
            };
            if (((IKsControl)ksObj).KsProperty(ref property, Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _) == 0) sent++;
        });
        return sent > 0;
    }

    static void Visit(Action<Endpoint, IMMDeviceEnumerator> visit)
    {
        var enumerator = CreateEnumerator();
        foreach (int flow in new[] { Render, Capture })
        {
            if (enumerator.EnumAudioEndpoints(flow, Active | Unplugged, out var endpoints) != 0) continue;
            endpoints.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (endpoints.Item(i, out var endpoint) != 0) continue;
                    if (endpoint.GetId(out string id) != 0) continue;
                    endpoint.GetState(out int state);
                    var topologyId = typeof(IDeviceTopology).GUID;
                    if (endpoint.Activate(ref topologyId, ClsCtxAll, IntPtr.Zero, out var topologyObj) != 0) continue;
                    var topology = (IDeviceTopology)topologyObj;
                    if (topology.GetConnector(0, out var connector) != 0) continue;
                    if (connector.GetDeviceIdConnectedTo(out string filterId) != 0 || string.IsNullOrEmpty(filterId)) continue;
                    visit(new Endpoint(id, filterId, state == Active), enumerator);
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
    }

    // ── 출력 장치 목록 ──
    // NAudio로 이름을 읽으면 속성을 하나씩 전부 훑느라, 백그라운드 스레드에서는 장치 하나에 2초씩 걸린다.
    // 그래서 필요한 속성(표시 이름) 하나만 직접 읽고, 한 번 읽은 이름은 기억해 둔다.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Names = new();
    static PropertyKey FriendlyNameKey = new() { FmtId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 14 };

    public static List<AudioOutputInfo> ListOutputs()
    {
        var list = new List<AudioOutputInfo>();
        var enumerator = CreateEnumerator();
        string current = "";
        if (enumerator.GetDefaultAudioEndpoint(Render, 1, out var def) == 0) def.GetId(out current);
        if (enumerator.EnumAudioEndpoints(Render, Active, out var endpoints) != 0) return list;
        endpoints.GetCount(out int count);
        for (int i = 0; i < count; i++)
        {
            try
            {
                if (endpoints.Item(i, out var endpoint) != 0 || endpoint.GetId(out string id) != 0) continue;
                if (!Names.TryGetValue(id, out var name))
                {
                    name = ReadName(endpoint);
                    if (name.Length > 0) Names[id] = name;
                    else name = "이름 없는 장치";
                }
                list.Add(new AudioOutputInfo(id, name, id == current));
            }
            catch (Exception ex) { App.Log(ex); }
        }
        return list;
    }

    static string ReadName(IMMDevice endpoint)
    {
        if (endpoint.OpenPropertyStore(0, out var store) != 0) return "";
        var value = new PropVariant();
        try
        {
            if (store.GetValue(ref FriendlyNameKey, out value) != 0) return "";
            const ushort VT_LPWSTR = 31;
            return value.Vt == VT_LPWSTR && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
        }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid FmtId; public int Pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    // 윈도우의 장치 열거기는 프로세스에 하나뿐인 객체라서, .NET 쪽 포장(RCW)도 하나를 모두가 나눠 쓴다.
    // 그 포장의 형식은 "처음 만든 쪽"이 정하는데, NAudio는 자기 형식이 아니면 형 변환 오류를 낸다.
    // 그래서 NAudio가 반드시 먼저 만들게 하고(Keeper), 여기서는 같은 객체를 인터페이스로만 빌려 쓴다.
    static readonly MMDeviceEnumerator Keeper = new();

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int clsCtx, in Guid iid, out IntPtr instance);

    static IMMDeviceEnumerator CreateEnumerator()
    {
        GC.KeepAlive(Keeper);
        var unknown = new Guid("00000000-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(CoCreateInstance(EnumeratorClsid, IntPtr.Zero, ClsCtxAll, unknown, out var ptr));
        try { return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(ptr); }
        finally { Marshal.Release(ptr); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KsProperty { public Guid Set; public int Id; public int Flags; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out int count);
        [PreserveSig] int GetConnector(int index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }
}

// ═════════════════════════ 소리 출력 장치 ═════════════════════════

public sealed record AudioOutputInfo(string Id, string Name, bool IsDefault);

/// <summary>스피커/헤드폰 등 출력 장치 목록과 기본 장치 바꾸기.</summary>
public sealed class AudioOutputService
{
    public List<AudioOutputInfo> List()
    {
        var list = new List<AudioOutputInfo>();
        try { list = BluetoothAudio.ListOutputs(); }
        catch (Exception ex) { App.Log(ex); }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    /// <summary>모든 용도(일반·멀티미디어·통화)의 기본 출력 장치를 바꾼다.</summary>
    public bool SetDefault(string deviceId)
    {
        try
        {
            var config = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(PolicyConfigClsid)!)!;
            int hr = 0;
            for (int role = 0; role < 3; role++) hr |= config.SetDefaultEndpoint(deviceId, role);
            return hr == 0;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    // 윈도우 소리 설정이 내부적으로 쓰는 인터페이스. 앞의 열 개는 자리만 맞추고 쓰지 않는다.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
