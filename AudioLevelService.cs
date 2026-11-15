using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace DynamicIsland;

/// <summary>
/// 스피커로 나가는 소리를 잠깐 들여다보고(WASAPI 루프백) 주파수 대역별 크기만 계산한다.
/// 소리 자체는 저장하거나 어디로 보내지 않는다. 음악이 재생 중일 때만 켜진다.
///
/// 캡처를 열고 닫는 일은 윈도우 오디오 서비스가 바쁠 때(출력 장치를 바꾸는 순간 등) 오래 걸릴 수 있어서
/// 전부 백그라운드에서 하고, 화면 쪽은 "켜져 있으면 좋겠다/아니다"만 알려 준다.
/// </summary>
public sealed class AudioLevelService : IDisposable
{
    public const int Bands = 8;
    const int N = 1024;
    static readonly int[] Edges = { 1, 3, 6, 11, 20, 38, 70, 130, 240 };

    readonly float[] bands = new float[Bands];
    readonly float[] buffer = new float[N];
    readonly Complex[] fft = new Complex[N];
    readonly double[] db = new double[Bands];
    readonly float[] window = new float[N];
    readonly object gate = new(), life = new();
    double ceiling = -80;
    int pos, working;
    long lastData, lastLoud, lastCheck, retryAt;
    volatile bool wanted, running;
    WasapiLoopbackCapture? capture;   // life 잠금 안에서만 만들고 없앤다
    string deviceId = "";

    public AudioLevelService()
    {
        for (int i = 0; i < N; i++) window[i] = (float)FastFourierTransform.HannWindow(i, N);
    }

    /// <summary>
    /// 화면 쪽에서 수시로 부른다. 바로 돌아오고, 필요한 때만 백그라운드에서 캡처를 켜거나 끈다.
    /// 켜져 있는 동안에는 2초마다 기본 출력 장치가 바뀌었는지 보고, 바뀌었으면 새 장치로 옮겨 간다.
    /// </summary>
    public void SetWanted(bool on)
    {
        wanted = on;
        long now = Environment.TickCount64;
        bool active = running;
        if (on == active && !(on && now - lastCheck > 2000)) return;
        if (on && !active && now < retryAt) return;
        if (Interlocked.Exchange(ref working, 1) == 1) return;
        lastCheck = now;
        Task.Run(() =>
        {
            try { Reconcile(); }
            catch (Exception ex) { App.Log(ex); }
            finally { Interlocked.Exchange(ref working, 0); }
        });
    }

    void Reconcile()
    {
        lock (life)
        {
            if (!wanted)
            {
                StopCore();
                return;
            }
            string current = DefaultDeviceId();
            if (running && current == deviceId) return;

            StopCore();
            try
            {
                var c = new WasapiLoopbackCapture();
                c.DataAvailable += OnData;
                // 장치가 사라지면 캡처 스레드가 스스로 끝난다. 여기서는 표시만 내리고, 정리는 다음 점검 때 한다
                // (캡처 스레드 안에서 자기 자신을 정리하면 서로 기다리다 멈춘다).
                c.RecordingStopped += (_, _) => { if (ReferenceEquals(capture, c)) running = false; };
                c.StartRecording();
                capture = c;
                deviceId = current;
                running = true;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                retryAt = Environment.TickCount64 + 30000;
            }
        }
    }

    void StopCore()
    {
        var c = capture;
        capture = null;
        running = false;
        if (c != null)
        {
            try { c.DataAvailable -= OnData; c.StopRecording(); } catch { }
            try { c.Dispose(); } catch { }
        }
        lock (gate) Array.Clear(bands);
    }

    static string DefaultDeviceId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch { return ""; }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture c) return;
        var f = c.WaveFormat;
        int ch = f.Channels, bps = f.BitsPerSample / 8, frame = ch * bps;
        if (frame == 0) return;
        for (int i = 0; i + frame <= e.BytesRecorded; i += frame)
        {
            float v = 0;
            for (int k = 0; k < ch; k++)
            {
                int o = i + k * bps;
                v += bps == 4 ? BitConverter.ToSingle(e.Buffer, o)
                   : bps == 2 ? BitConverter.ToInt16(e.Buffer, o) / 32768f
                   : 0;
            }
            buffer[pos++] = v / ch;
            if (pos == N)
            {
                Analyze();
                pos = 0;
            }
        }
        lastData = Environment.TickCount64;
    }

    void Analyze()
    {
        for (int i = 0; i < N; i++)
        {
            fft[i].X = buffer[i] * window[i];
            fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 10, fft);

        double loudest = -200;
        for (int b = 0; b < Bands; b++)
        {
            double max = 0;
            for (int k = Edges[b]; k < Edges[b + 1]; k++)
            {
                double mag = Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y);
                if (mag > max) max = mag;
            }
            db[b] = 20 * Math.Log10(max + 1e-10) + b * 2.5; // 고음은 원래 작으니 조금 올려 준다
            if (db[b] > loudest) loudest = db[b];
        }

        // 볼륨을 작게 듣든 크게 듣든 똑같이 움직이도록, 최근에 가장 컸던 소리를 기준으로 맞춘다
        const double range = 34;
        ceiling = Math.Max(loudest, ceiling - 0.06);
        if (ceiling < -80) ceiling = -80;   // 잡음 수준의 소리까지 키우지는 않는다
        bool audible = loudest > -95;
        if (audible) lastLoud = Environment.TickCount64;

        lock (gate)
        {
            for (int b = 0; b < Bands; b++)
                bands[b] = audible ? (float)Math.Clamp((db[b] - (ceiling - range)) / range, 0, 1) : 0;
        }
    }

    /// <summary>대역 값을 복사한다. 최근에 실제로 소리가 나고 있었으면 true (음소거·무음이면 false).</summary>
    public bool Read(float[] dest)
    {
        lock (gate) Array.Copy(bands, dest, Math.Min(dest.Length, Bands));
        long now = Environment.TickCount64;
        return running && now - lastData < 300 && now - lastLoud < 600;
    }

    public void Dispose()
    {
        wanted = false;
        // 끝낼 때는 기다리지 않는다 (오디오 서비스가 대답이 없어도 앱 종료가 걸리지 않게)
        Task.Run(() => { lock (life) StopCore(); });
    }
}
